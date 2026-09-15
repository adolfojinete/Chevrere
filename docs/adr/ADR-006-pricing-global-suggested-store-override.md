# ADR-006: Pricing — precio sugerido global + override por Store

## Estado

Aceptado — Fase 3.

## Contexto

YaaJuu administra un catálogo global compartido. Cada dark store opera de forma independiente y necesita autonomía comercial sobre el precio de venta, sin perder la orientación de marca de la plataforma.

## Decisión

Se crea el módulo `Pricing` (Domain / Application / Infrastructure) separado de Catalog.

- `GlobalProductPrice`: precio sugerido vigente de YaaJuu por `GlobalProduct` (sin `TenantId`).
- `StoreProductPrice`: override vigente por `(Store, GlobalProduct)` (tenant-scoped).
- Modelo temporal versionado: `ValidFrom` / `ValidTo`; vigente cuando `ValidTo IS NULL`.
- Un cambio de precio cierra la versión anterior y abre una nueva en la misma transacción.
- `EffectivePrice = StoreOverride ?? SuggestedGlobal ?? null`.
- Source derivado: `Store` | `Global` | `None` (no persistido).
- `Money(Amount, Currency)` con `COP` como única moneda admitida hoy (`CurrencyCodes`).
- Amount > 0; `numeric(18,2)`.
- Override requiere que exista `StoreProduct` (aunque esté disabled).
- Se puede definir override sin suggested global.
- Idempotencia: mismo monto/moneda → success sin mutación, audit ni SaveChanges.
- Remove override cierra la vigencia (histórico conservado); delete repetido es no-op.

Integridad PostgreSQL:

- Unique parcial: un precio global vigente por producto.
- Unique parcial: un override vigente por store+product.
- FK `(StoreId, TenantId) → Store`.
- FK `(TenantId, StoreId, GlobalProductId) → StoreProduct` (alternate key).
- Check `amount > 0`.

## Consecuencias

Catalog no conoce precios. Orders futuro debe snapshottear `EffectivePrice` en el ítem; el histórico de Pricing explica vigencia, no reemplaza el snapshot del pedido.

No incluye promociones, impuestos, costos, márgenes ni inventory.
