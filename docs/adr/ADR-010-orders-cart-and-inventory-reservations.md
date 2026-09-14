# ADR-010: Cart, Orders e Inventory Reservations

## Estado

Aceptado — Fase 7.

## Contexto

El consumidor ya descubre cobertura y catálogo (ADR-009). Falta el acto de comprar: un carrito ligado a la dark store que lo cubre, un pedido con snapshot comercial, y una reserva de stock que impide vender dos veces la última unidad.

Inventory (ADR-007) ya tenía `Reserved` como hueco. Sin un agregado de reserva y un ledger `Reservation` / `ReservationReleased` / `ReservationCommitted`, ese hueco no era auditable ni idempotente.

## Decisión

Se crea el módulo `Orders` (Domain / Application / Infrastructure) y el agregado `InventoryReservation` en Inventory. Orders no referencia Inventory ni Consumer: reserva stock por `IInventoryReservationService` (SharedKernel) y resuelve la store por `IConsumerStoreResolver` (SharedKernel.Discovery).

### Identidad del consumidor

- Rol `RoleNames.Consumer` (string, no hay enum `UserRole`) y policy `AuthorizationPolicies.Consumer`.
- `ApplicationUser.TenantId` queda **null**. El JWT del consumidor no lleva tenant. `TenantId` / `StoreId` del carrito y del pedido salen de la store resuelta.
- Tests y seed de consumidores: `IIdentityProvisioning.AddConsumerAsync(email, password, displayName)`.

### Cart

- Un carrito `Active` por consumidor (índice único parcial). `Converted` al crear el pedido.
- Vacío puede relocarse a otra store. Con ítems y otra store → `cart.fulfillment_changed` (409).
- Ver el carrito **no** crea uno vacío. PUT del ítem sí lo crea.
- DTOs de consumidor no incluyen store, tenant, coords, reservas ni inventario.

### Order

- `Place` congela SKU/nombre/marca/presentación/precio. Un rename o un cambio de precio posterior no reescribe el pedido.
- Estados: `PendingPayment` → `Cancelled` | `Expired` | `Confirmed`. Cancel/Expire/Confirm son no-op en el estado destino.
- `Confirm` **no** compromete stock. El commit de inventario (`OnHand` y `Reserved` bajan juntos) es un paso posterior.
- TTL de reserva: 15 minutos (`Orders:ReservationTtlMinutes`). Worker de expiración desactivado en `Testing` y con `Orders:ExpirationWorkerEnabled=false`.
- Números `ORD-00000001` desde `orders_order_number_seq`.
- `UNIQUE(source_cart_id)`. Idempotencia `orders.create` con fingerprint `ConsumerId + CartId + xmin del cart + ítems + TenantId + StoreId` (nunca lat/lon crudos).

### Reservas

- `InventoryReservation` por `(ReferenceType, ReferenceId)` — para Orders, `OrderItem` + id del ítem (v7 asignado en dominio **antes** de persistir).
- `InventoryItem.Reserve` exige `Available >= qty` (`inventory.insufficient_available`). Decrease/Waste hacen el mismo chequeo **antes** de `Apply`.
- Movements: `Reservation=6`, `ReservationReleased=7`, `ReservationCommitted=8`, referencia `InventoryReservation` / `reservation.Id`.
- El servicio **no** llama `SaveChanges`. `DiscardPending` es local, nunca `ChangeTracker.Clear()`.

### Cleanup del perdedor

Igual que Procurement: detach local de pedido, reservas/movements, carrito, idempotencia y auditoría. Replay verifica `Order.ConsumerUserId` del usuario actual.

## Consecuencias

El catálogo anónimo sigue siendo público. Cart y Orders exigen JWT Consumer. El back-office ve pedidos de su store (404 si es ajena). Admin lee cualquier pedido. `Reserved` deja de ser un hueco: cada unidad reservada tiene fila, movement y fecha de expiración.
