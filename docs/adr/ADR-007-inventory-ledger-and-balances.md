# ADR-007: Inventory — ledger inmutable + balances actuales

## Estado

Aceptado — Fase 4 / endurecido en Fase 4.1.

## Contexto

Inventory debe responder cuántas unidades físicas tiene una Store de un producto, y también por qué. Un campo `Stock` mutable no basta: no hay historial auditable ni defensa frente a retries HTTP o escrituras concurrentes.

## Decisión

Se crea el módulo `Inventory` (Domain / Application / Infrastructure), separado de Catalog, Pricing y Orders.

### Modelo

- `InventoryItem`: balance actual denormalizado (`OnHand`, `Reserved`) por `(TenantId, StoreId, GlobalProductId)`.
- `Available = OnHand - Reserved` (derivado, no persistido).
- `InventoryMovement`: ledger inmutable con `OnHandDelta` / `ReservedDelta` y snapshots before/after.
- Un `InventoryItem` existe solo cuando se gestiona stock (Initialize). `StoreProduct` puede existir sin item.
- Cantidades enteras (`long` / `bigint`). Sin decimales en MVP.
- `Initialize(0)` crea el item **sin** movement de delta 0. `Initialize(n>0)` crea item + `InitialStock`.
- Ajustes y merma requieren `Reason` (máx. 500). Reinitialize → conflicto.
- Adjust/Waste sobre item inexistente → `inventory.not_initialized` (no auto-crear).

### Consistencia

Toda mutación de balance (excepto Initialize 0) inserta exactamente un movement en la misma transacción / `SaveChanges`, junto con `AuditEvent` e `IdempotentOperation`.

### Idempotencia

Mutaciones cuantitativas **no** son naturalmente idempotentes. Header `Idempotency-Key` obligatorio (8..128, case-sensitive, sin whitespace). Building block `idempotent_operations` con `UNIQUE(TenantId, Operation, IdempotencyKey)` y fingerprint SHA-256 del payload canónico (`InvariantCulture` para cantidades). Solo se persiste tras éxito. Mismo key + mismo payload → replay. Mismo key + payload distinto → `idempotency.key.reused`. Fallos de negocio **no** consumen la key (pueden reintentarse tras corregir).

### Idempotency under concurrency

Dos requests concurrentes con **same key + same payload** pueden ambos pasar el pre-check y construir un intento local. El perdedor puede observar:

1. `23505 UniqueViolation` sobre `idempotent_operations`, o
2. conflicto `xmin` sobre `InventoryItem`.

En ambos casos el handler:

1. limpia **solo** las entries del intento fallido (`InventoryItem` Added/Modified, `InventoryMovement` Added, `AuditEvent` Added, `IdempotentOperation` Added) — sin detach global ni `ChangeTracker.Clear()`;
2. consulta la `IdempotentOperation` **committed** (`AsNoTracking`);
3. si hash coincide → replay success; si no hay row → conflicto original (`already_initialized` / concurrency); si hash difiere → `idempotency.key.reused`.

Keys distintas siguen compitiendo normalmente: un success y un 409 de concurrencia/negocio. No se convierte cualquier `ConcurrencyConflictException` en replay.

### Replay exacto (snapshot)

El replay **no** usa el balance actual de `InventoryItem` (podría haber mutado después). Usa el snapshot del ledger:

- Adjust / Waste / Initialize(n>0): `ResourceId` → `InventoryMovement`; respuesta = `OnHandAfter` / `ReservedAfter` / `Available`.
- Initialize(0): respuesta fija `0/0/0` con `MovementId = null` (el resultado original es siempre cero).

Así, `same key` ⇒ **mismo resultado semántico**, no el estado posterior de la Store.

### Concurrencia e integridad

- Optimistic concurrency con `xmin` en `InventoryItem`.
- Conflictos entre intenciones distintas → 409 sin retry automático de decrementos.
- CHECKs PostgreSQL: `on_hand >= 0`, `reserved >= 0`, `reserved <= on_hand`.
- FK a `Store` y `StoreProduct` (compuestas con tenant). Unique por store+product.

### Alcance explícitamente fuera

Transfers, lots, FIFO, costos, Redis, RabbitMQ, soft-delete de inventory. Retención/cleanup de `idempotent_operations` queda pendiente. El commit de stock al confirmar un pedido (pago) queda para una fase posterior: `Order.Confirm` no llama `CommitReservation`.

Reservas: `Reserved` está respaldado por `InventoryReservation` y movements `Reservation` / `ReservationReleased` / `ReservationCommitted` (Fase 7, [ADR-010](ADR-010-orders-cart-and-inventory-reservations.md)). Decrease/Waste rechazan cantidades por encima de `Available`.

## Consecuencias

Catalog responde “qué es”; Pricing “cuánto cuesta”; Inventory “cuántas unidades hay y por qué”. La disponibilidad comercial compuesta (enabled + precio + available) se compondrá más adelante (Orders/App), sin acoplar Inventory a Pricing.
