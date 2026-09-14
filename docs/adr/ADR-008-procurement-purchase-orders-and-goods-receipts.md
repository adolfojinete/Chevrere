# ADR-008: Procurement — órdenes de compra y recepción de mercancía

## Estado

Aceptado — Fase 5.

## Contexto

Inventory (ADR-007) sabe cuántas unidades hay y por qué, pero sus únicas entradas eran manuales (`Initialize`, `AdjustmentIncrease`). El stock real de una dark store entra por una puerta: llega mercancía de un proveedor contra un pedido. Sin ese documento, cada ingreso queda registrado como un ajuste sin proveedor, sin costo y sin nada que conciliar.

Procurement responde “qué le pedimos a quién, a qué costo, y qué llegó”.

## Decisión

Se crea el módulo `Procurement` (Domain / Application / Infrastructure). No referencia Inventory, Catalog ni Pricing: habla con Inventory por un puerto en SharedKernel y con Tenancy/Catalog por un puerto de solo lectura implementado en su Infrastructure.

### Modelo

- `Supplier`: proveedor del tenant. `Code` único por tenant (normalizado a mayúsculas), email normalizado a minúsculas. Activable/desactivable; un proveedor inactivo no admite órdenes nuevas.
- `PurchaseOrder`: agregado raíz con sus `PurchaseOrderItem`. Pertenece a un tenant y a una store.
- `PurchaseOrderItem`: `OrderedQuantity`, `ReceivedQuantity`, `UnitCost` (`Money`). `RemainingQuantity` derivado.
- `GoodsReceipt` + `GoodsReceiptItem`: documento **inmutable** de lo que llegó, con snapshot before/after por línea y el estado que la orden alcanzó por esa recepción.

### Máquina de estados

```text
Draft ──approve──> Approved ──receive(parcial)──> PartiallyReceived ──receive(resto)──> Received
  │                    │                              │
  └──cancel──> Cancelled <──cancel──┘                 └──receive(parcial)──┘
```

- Solo un `Draft` se edita: una vez aprobada, la orden es un compromiso con el proveedor.
- Solo `Approved` o `PartiallyReceived` reciben mercancía.
- Una orden con mercancía ya recibida **no** se cancela (`purchase_order.not_cancellable`): habría que devolverla, y eso es otro documento.
- `Approve` y `Cancel` son idempotentes por estado: repetirlos no falla ni duplica auditoría.
- La orden pasa a `Received` solo cuando **todas** sus líneas están completas.

`Receive` valida todas las líneas (pertenencia, duplicados, cantidad > 0, cantidad ≤ pendiente) **antes** de mutar cualquier cosa: una recepción rechazada deja el agregado intacto.

Editar un draft **reutiliza** la línea de un producto que sigue en la orden en vez de borrarla e insertarla: el índice único `(purchase_order_id, global_product_id)` no tolera delete + insert en la misma transacción.

### Money en SharedKernel

`Money` y `CurrencyCodes` se mueven de `Pricing.Domain.ValueObjects` a `SharedKernel.Domain.ValueObjects`. El costo de compra y el precio de venta son el mismo concepto de dinero (positivo, COP, escala 2), y duplicar el value object para que Procurement no referencie Pricing habría dejado dos definiciones divergiendo. Pricing pasa a consumir el `Money` compartido.

### Contrato de entrada a Inventory

Inventory expone un puerto en SharedKernel, no un tipo propio:

```csharp
namespace Chevrere.SharedKernel.Inventory;

public interface IInventoryInboundService
{
    Task<IReadOnlyList<InventoryInboundLineResult>> ApplyGoodsReceiptAsync(
        InventoryInboundRequest request, CancellationToken cancellationToken);

    void DiscardPending(IReadOnlyList<InventoryInboundLineResult> applied);
}
```

Consecuencias buscadas:

- `Procurement.Application` **no** referencia `Inventory.Domain` ni `Inventory.Application`. Un test de arquitectura lo verifica.
- Inventory no conoce a sus llamadores: no referencia Procurement.
- La implementación (`InventoryInboundService`) **solo stagea** escrituras; el dueño de la transacción es Procurement.
- `InventoryMovementType.Receipt` con `ReferenceType = "GoodsReceiptItem"` y `ReferenceId = GoodsReceiptItem.Id` hace cada movimiento trazable hasta su documento.
- Índice único filtrado `(type, reference_type, reference_id) WHERE reference_id IS NOT NULL`: la base rechaza postear dos veces la misma línea recibida.

Si la store aún no gestionaba stock del producto, el ingreso crea el `InventoryItem` en cero y postea el `Receipt` (`CreateForReceipt`). No emite `InitialStock` ni auditoría de Inventory: la recepción ya es el documento que lo explica. Si ya había stock, el movimiento se suma al balance existente sin tocar `Reserved`.

`StoreProduct` debe existir y el `GlobalProduct` estar activo. Lo valida Procurement antes de recibir (`procurement.product.not_offered`, `procurement.product.not_active`), no Inventory.

### Atomicidad

Una recepción escribe en un solo `SaveChanges`: cantidades de la orden, `GoodsReceipt` + líneas, `InventoryItem` + `InventoryMovement`, `IdempotentOperation` y `AuditEvent` (`GoodsReceiptRecorded`). El stock no se mueve sin el documento que lo justifica, ni al revés.

### Numeración de documentos

`PO-000001` / `GR-000001` salen de secuencias PostgreSQL (`procurement_purchase_order_number_seq`, `procurement_goods_receipt_number_seq`), no de `MAX(number) + 1`.

- Dos requests concurrentes nunca obtienen el mismo número.
- `nextval` no se revierte con la transacción: un número puede quedar sin usar. Se acepta: el requisito es unicidad, no continuidad contable.
- La unicidad real la garantizan `UNIQUE(tenant_id, number)` y `UNIQUE(tenant_id, receipt_number)`.

### Idempotencia

`POST purchase-orders` y `POST purchase-orders/{id}/receipts` exigen `Idempotency-Key` (mismas reglas que ADR-007). Operaciones `ProcurementPurchaseOrderCreate` y `ProcurementReceive`.

Fingerprint canónico:

- crear orden: store, proveedor, notas y líneas ordenadas por `GlobalProductId`;
- recibir: store, orden, notas y líneas ordenadas por `PurchaseOrderItemId`.

Ordenar las líneas hace que el mismo pedido enviado con las líneas en otro orden sea el mismo pedido, no una key reusada.

Los cambios de estado (`approve`, `cancel`) no consumen key: son idempotentes por naturaleza.

### Concurrencia

Igual que en Inventory 4.1: el perdedor de una carrera con **misma key** puede ver `23505` sobre `idempotent_operations` o conflicto `xmin` sobre `PurchaseOrder`. En ambos casos limpia **solo** las entries de su intento (orden y líneas, recibo y líneas, movimientos e items de Inventory vía `DiscardPending`, `IdempotentOperation`, `AuditEvent`) — sin `ChangeTracker.Clear()` — y replaya desde la operación committed.

El replay se reconstruye desde el `GoodsReceipt` persistido, **nunca** desde el estado actual de la orden: una recepción posterior pudo llevarla a `Received`, pero quien repite su key debe volver a ver `PartiallyReceived` y sus cantidades before/after originales. Por eso `PurchaseOrderStatusAfter` y el snapshot por línea se persisten en lugar de derivarse.

Keys distintas siguen compitiendo: un success y un 409.

### Integridad en base de datos

- `xmin` en `Supplier`, `PurchaseOrder` y `PurchaseOrderItem`.
- FKs compuestas con tenant hacia `Tenant`, `Store`, `Supplier`, `PurchaseOrder`, `StoreProduct` y `GlobalProduct`: una orden no puede apuntar a un proveedor de otro tenant.
- CHECKs: `ordered_quantity > 0`, `received_quantity >= 0`, `received_quantity <= ordered_quantity`, `unit_cost_amount > 0`, `received_after = received_before + received_quantity`.
- Query filters por tenant en `Supplier`, `PurchaseOrder` y `GoodsReceipt`.

La regla de sobre-recepción vive dos veces: en el dominio (mensaje útil) y en un CHECK (última línea de defensa).

### Alcance explícitamente fuera

Devoluciones a proveedor, notas de crédito, costo promedio ponderado, lotes y vencimientos, órdenes multi-store, recepción sin orden previa, cuentas por pagar, adjuntos de factura.

## Consecuencias

Catalog responde “qué es”; Pricing “cuánto cuesta vender”; Procurement “a quién le compramos y qué llegó”; Inventory “cuántas unidades hay y por qué”. El ledger de Inventory pasa a tener entradas trazables a un proveedor y a un costo, base para costeo y conciliación futura sin acoplar Inventory a Procurement.
