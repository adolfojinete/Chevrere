# ADR-005 — Catálogo global + opt-in por Store

## Estado

Aceptado

## Contexto

Chevrere es una sola marca hacia el consumidor. Si cada asociado inventa su propio catálogo, se duplican SKUs, fotos, analítica y precios. Al mismo tiempo, cada dark store decide qué vende.

## Decisión

Chevrere posee el catálogo maestro (`Category`, `GlobalProduct`). Las stores optan a esos productos mediante `StoreProduct` (`IsEnabled`).

- Cada presentación comercial es un `GlobalProduct` (SKU distinto). No hay `ProductVariant` en esta fase.
- No hay `ParentCategoryId`: la jerarquía se pospone hasta que exista un caso de uso real.
- `GlobalProduct` y `Category` no tienen `TenantId`.
- `StoreProduct` pertenece al tenant y se ancla con FK `(StoreId, TenantId) → Store(Id, TenantId)`.
- Enable es idempotente: si ya está habilitado, se confirma el estado. Disable pone `IsEnabled = false` y no borra la fila.
- Un producto inactivo o una categoría inactiva no es comercialmente disponible. No se desactivan en cascada las filas hijas.
- Pricing, imágenes, impuestos e inventario quedan fuera.

## Consecuencias

- La app consumidor futura filtra stores candidatas y luego `StoreProduct.Enabled` ∩ producto activo ∩ categoría activa.
- Inventory y Pricing podrán colgar de `StoreProduct` sin redefinir el catálogo.
- Catalog.Application no referencia Tenancy. La existencia de Store se valida por puerto `IStoreAccess`.
