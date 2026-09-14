# ADR-009: Consumer Discovery — geolocalización y catálogo comercial

## Estado

Aceptado — Fase 6.

## Contexto

Hasta la Fase 5 el back-office sabe quién opera cada dark store, qué vende, a qué precio y cuánto stock tiene. El consumidor aún no puede preguntar “¿me entregan aquí?” ni “¿qué puedo comprar?” sin revelar la red de operadores detrás de la marca única Chevrere.

Consumer Discovery responde esas dos preguntas sin filtrar identidad de store, tenant, coordenadas operativas ni stock.

## Decisión

Se crea el módulo `Consumer` (Domain / Application / Infrastructure). Su Domain y Application **no** referencian Catalog, Pricing, Inventory ni Tenancy: la composición comercial y geoespacial ocurre solo en Infrastructure. Tests de arquitectura lo verifican, incluido que Domain no dependa de NetTopologySuite, Npgsql ni EF.

### Área de servicio, no dirección de la Store

`Store` ya tiene `AddressInternal`, `Latitude?`, `Longitude?` y `StoreStatus` para operación interna. Consumer Discovery **no** los usa en APIs de consumidor.

Se introduce `StoreServiceArea`:

| Campo | Rol |
|---|---|
| `Latitude` / `Longitude` | Centro del área (doubles en dominio) |
| `ServiceRadiusMeters` | Alcance de entrega, 100..50 000 m (decisión de producto + CHECK) |
| `Location` | Columna sombra PostGIS `geography(Point,4326)` (X=lon, Y=lat, SRID 4326) |
| `IsEnabled` | Apertura explícita al consumidor |

La primera configuración nace con `IsEnabled = false`: dibujar el área no es abrir la tienda. `Configure` / `Enable` / `Disable` son idempotentes (mismos valores → sin `UpdatedAt` ni auditoría). Enable/Disable sin área configurada → `service_area.not_configured` (404).

### PostGIS

PostgreSQL pasa de `postgres:17-alpine` a `postgis/postgis:17-3.5-alpine` (docker-compose y Testcontainers). La migración `AddConsumerDiscovery` habilita la extensión `postgis`. Npgsql usa `UseNetTopologySuite()`; el paquete `Npgsql.EntityFrameworkCore.PostgreSQL.NetTopologySuite` 10.0.0 acompaña a Npgsql EF 10.0.0.

Resolución de store elegible (`IConsumerStoreResolver`):

```text
ST_DWithin(location, punto_consumidor, service_radius_meters)
  AND is_enabled
  AND Store.Status = Active
  AND Tenant.Status = Active
ORDER BY ST_Distance(...), store_id
LIMIT 1
```

Un solo store por coordenada. Empate de distancia se desempata por `StoreId`. No hay `FulfillmentContextId`: cobertura, catálogo, categorías y detalle re-resuelven siempre desde lat/lon.

### Elegibilidad SaaS

`Tenant.Status == Active` **y** `Store.Status == Active`. Suspender un Franchisee suspende el Tenant: la cobertura desaparece. Una suscripción más allá del status del Tenant (Trial, PastDue, etc.) queda como **Decision Pending**: no se inventa billing en esta fase; el gate operativo es el status del Tenant.

### Visibilidad comercial

Un producto aparece al consumidor solo si:

```text
Category.Status = Active
AND GlobalProduct.Status = Active   -- IsActive computado se ignora en SQL
AND StoreProduct.IsEnabled
AND existe precio vigente (ValidTo IS NULL): COALESCE(store override, global suggested)
AND existe InventoryItem con (OnHand - Reserved) > 0
AND moneda COP vía el precio vigente
```

El DTO de lista/detalle expone `Id` = `GlobalProductId`, SKU, nombre, marca, presentación, descripción, categoría, precio y moneda. **Nunca** store/tenant ids, lat/lon, stock, distancia ni dirección.

### Superficies HTTP

| Endpoint | Auth |
|---|---|
| `POST /api/v1/consumer/coverage` | Anónimo |
| `POST /api/v1/consumer/catalog/search` | Anónimo |
| `POST /api/v1/consumer/categories` | Anónimo |
| `POST /api/v1/consumer/products/{productId}` | Anónimo — 404 sin cobertura o no visible |
| Admin `.../stores/{id}/service-area` GET/PUT/enable/disable | PlatformStaff lectura; PlatformOperators escritura |
| Business GET `.../stores/{id}/service-area` | FranchiseeOwner, solo su store |

Coordenadas van en el body (no en la URL) para no filtrarlas a access logs. Lecturas de consumidor no auditan ni hacen `SaveChanges`. No se registra `LogInformation` de coordenadas exactas del consumidor.

### Persistencia

- Tabla `store_service_areas`, UNIQUE(`store_id`), FK (`store_id`, `tenant_id`) → `stores` Restrict
- CHECK radio 100..50000, índice GiST sobre `location`, `xmin` vía `ConfigureXminConcurrency`
- Query filter por `TenantId` (como el resto de tablas tenant-scoped); el resolver anónimo usa SQL + `IgnoreQueryFilters` localizado

### Auditoría

Solo mutaciones reales de área: `store_service_area.configured` / `.enabled` / `.disabled`.

## Consecuencias

- El consumidor ve una sola marca; la dark store detrás es un detalle interno.
- Domain permanece libre de NTS/EF: la geometría es un detalle de Infrastructure.
- Coverage y catálogo no pueden diverger: comparten el mismo resolver.
- Requiere imagen PostGIS en local y en CI/Testcontainers; plain Postgres ya no alcanza.
- Billing SaaS fino (suscripción PastDue vs Active) queda explícitamente pendiente.
