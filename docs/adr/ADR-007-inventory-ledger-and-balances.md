# ADR-007: Inventory — ledger inmutable + balances actuales

## Estado

Aceptado — Fase 4.

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

Mutaciones cuantitativas **no** son naturalmente idempotentes. Header `Idempotency-Key` obligatorio (8..128, case-sensitive, sin whitespace). Building block `idempotent_operations` con `UNIQUE(TenantId, Operation, IdempotencyKey)` y fingerprint SHA-256 del payload canónico. Solo se persiste tras éxito. Mismo key + mismo payload → replay. Mismo key + payload distinto → `idempotency.key.reused`. Fallos de negocio/concurrencia no consumen la key.

### Concurrencia e integridad

- Optimistic concurrency con `xmin` en `InventoryItem`.
- Conflictos → 409 sin retry automático de decrementos.
- CHECKs PostgreSQL: `on_hand >= 0`, `reserved >= 0`, `reserved <= on_hand`.
- FK a `Store` y `StoreProduct` (compuestas con tenant). Unique por store+product.

### Alcance explícitamente fuera

Orders/reservas API, Receipt/Procurement, transfers, lots, FIFO, costos, Redis, RabbitMQ, soft-delete de inventory.

Reservas futuras: `Reserved` queda preparado en el modelo; endpoints Reserve/Release/Commit no se exponen aún.

## Consecuencias

Catalog responde “qué es”; Pricing “cuánto cuesta”; Inventory “cuántas unidades hay y por qué”. La disponibilidad comercial compuesta (enabled + precio + available) se compondrá más adelante (Orders/App), sin acoplar Inventory a Pricing.
