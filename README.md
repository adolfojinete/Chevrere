# YaaJuu

YaaJuu es la marca oficial vigente del producto.

Plataforma SaaS + quick-commerce sobre una red de dark stores independientes.

Esta fase entrega la fundación técnica, la administración central de asociados, el catálogo comercial, pricing, inventario, abastecimiento, discovery hacia el consumidor y pedidos: onboarding atómico de Tenant, Franchisee, Owner, primera Store, Plan y Subscription; ciclo de vida (activar, suspender, reactivar) sin borrar datos; catálogo global YaaJuu y habilitación por dark store; precio sugerido global y override por Store; ledger de inventario por dark store; proveedores, órdenes de compra y recepción de mercancía; cobertura geolocalizada (PostGIS) y catálogo comercial anónimo; carrito y pedidos con reserva de stock; auditoría; multi-tenancy; y autenticación de plataforma.

## Objetivo

Funcionalidad mínima, arquitectura seria. Un solo backend desplegable. Módulos internos. PostgreSQL único.

## Arquitectura

Monolito modular en .NET 10 / ASP.NET Core.

```text
YaaJuu
├── src
│   ├── YaaJuu.Api
│   ├── BuildingBlocks
│   │   ├── YaaJuu.SharedKernel
│   │   └── YaaJuu.Infrastructure
│   └── Modules
│       ├── Identity     (Domain / Application / Infrastructure)
│       ├── Tenancy      (Domain / Application / Infrastructure)
│       ├── Subscriptions(Domain / Application / Infrastructure)
│       ├── Catalog      (Domain / Application / Infrastructure)
│       ├── Pricing      (Domain / Application / Infrastructure)
│       ├── Inventory    (Domain / Application / Infrastructure)
│       ├── Procurement  (Domain / Application / Infrastructure)
│       ├── Consumer     (Domain / Application / Infrastructure)
│       ├── Orders       (Domain / Application / Infrastructure)
│       └── Payments     (Domain / Application / Infrastructure)
├── tests
│   ├── YaaJuu.UnitTests
│   ├── YaaJuu.IntegrationTests
│   └── YaaJuu.ArchitectureTests
├── deploy/docker-compose.yml
└── docs
```

Detalle: [docs/architecture.md](docs/architecture.md).

## Requisitos

- .NET SDK 10.0.401 o superior (feature band 10.0)
- Docker (**PostGIS** local y tests de integración — imagen `postgis/postgis:17-3.5-alpine`)
- Herramientas EF Core: `dotnet tool install --global dotnet-ef`

## PostgreSQL local (PostGIS)

```powershell
cd deploy
docker compose up -d
```

La imagen es PostGIS (`postgis/postgis:17-3.5-alpine`), no Postgres plano: Consumer Discovery resuelve cobertura con `geography(Point,4326)`.
Los nombres físicos locales de Postgres (base, usuario, volume `chevrere_pgdata` y variables `CHEVRERE_DB_*`) se mantienen a propósito para no romper entornos existentes. No coinciden con la marca.
Valores de desarrollo (no son credenciales de producción):

- Host: `localhost`
- Port: `5432` (override: `CHEVRERE_DB_PORT`)
- Database: `chevrere`
- Username: `chevrere`
- Password: `chevrere_dev_only` (override: `CHEVRERE_DB_PASSWORD`)

## Configuración

La API lee `appsettings.json`, `appsettings.Development.json`, variables de entorno y User Secrets.

Connection string:

```text
Host=localhost;Port=5432;Database=chevrere;Username=chevrere;Password=chevrere_dev_only
```

Variable de entorno equivalente:

```text
ConnectionStrings__Database=Host=localhost;Port=5432;Database=chevrere;Username=chevrere;Password=...
```

En Development hay una `Jwt:SigningKey` de placeholder. Cámbiala. En cualquier otro ambiente la llave es obligatoria y no debe vivir en el repositorio.

## User Secrets y SuperAdmin

No hay contraseña de SuperAdmin en el repositorio. El seeder solo crea el usuario si existe password.

```powershell
cd src\YaaJuu.Api
dotnet user-secrets init
dotnet user-secrets set "Bootstrap:SuperAdmin:Email" "admin@yaajuu.local"
dotnet user-secrets set "Bootstrap:SuperAdmin:Password" "<elige-una-password-fuerte>"
dotnet user-secrets set "Jwt:SigningKey" "<llave-de-al-menos-32-caracteres>"
```

La password debe cumplir Identity: mínimo 10 caracteres, mayúscula, minúscula, número y símbolo.

Variables de entorno alternativas:

```text
Bootstrap__SuperAdmin__Email=admin@yaajuu.local
Bootstrap__SuperAdmin__Password=...
Jwt__SigningKey=...
YAAJUU_PAYMENT_SECRETS_KEY=...   # 32 bytes base64 o clave maestra para AES-GCM de secretos Wompi (no reutilizar JWT)
Payments__Wompi__DefaultRedirectUrl=https://app.example/payments/return
```

Payments multi-comercio: cada Tenant configura sus llaves Wompi vía Admin. Los secretos se almacenan cifrados; GET nunca los devuelve. Detalle: [docs/adr/ADR-011-payments-wompi-multi-merchant.md](docs/adr/ADR-011-payments-wompi-multi-merchant.md).

## Migraciones

```powershell
dotnet ef migrations add InitialCreate --project src\BuildingBlocks\YaaJuu.Infrastructure --startup-project src\YaaJuu.Api --output-dir Persistence/Migrations
dotnet ef database update --project src\BuildingBlocks\YaaJuu.Infrastructure --startup-project src\YaaJuu.Api
```

La API aplica las migraciones pendientes al arrancar, en cualquier ambiente. No se usa `EnsureCreated()`. También puedes aplicarlas a mano:

```powershell
dotnet ef database update --project src\BuildingBlocks\YaaJuu.Infrastructure --startup-project src\YaaJuu.Api
```

## Ejecución

```powershell
dotnet restore
dotnet build
cd deploy
docker compose up -d
cd ..
dotnet run --project src\YaaJuu.Api
```

- API HTTP: `http://localhost:5088`
- Swagger (Development): `http://localhost:5088/swagger`
- Health: `http://localhost:5088/health`
- Liveness: `http://localhost:5088/health/live`
- Readiness (incluye PostgreSQL): `http://localhost:5088/health/ready`

## Flujo mínimo en Swagger

1. Configura el SuperAdmin (User Secrets).
2. `POST /api/v1/auth/login`
3. Authorize con `Bearer {token}`
4. `POST /api/v1/admin/franchisees`
5. `GET /api/v1/admin/franchisees/{id}`
6. `POST /api/v1/admin/franchisees/{id}/activate`
7. `POST /api/v1/admin/franchisees/{id}/suspend`
8. `POST /api/v1/admin/franchisees/{id}/reactivate`
9. `GET /api/v1/admin/audit-events`
10. `POST /api/v1/admin/categories` y `POST /api/v1/admin/products`
11. Login Owner → `GET /api/v1/business/catalog/products`
12. `POST /api/v1/business/stores/{storeId}/products/{productId}/enable`
13. `PUT /api/v1/admin/products/{productId}/price` (SuggestedPrice)
14. Owner → `PUT/GET/DELETE /api/v1/business/stores/{storeId}/products/{productId}/price`
15. Owner → inventory list/get/movements + initialize/adjustments/waste (header `Idempotency-Key`)
16. Admin lectura → `GET /api/v1/admin/stores/{storeId}/inventory`
17. Owner → `POST /api/v1/business/suppliers`
18. Owner → `POST /api/v1/business/stores/{storeId}/purchase-orders` (header `Idempotency-Key`)
19. Owner → `POST .../purchase-orders/{id}/approve`
20. Owner → `POST .../purchase-orders/{id}/receipts` (header `Idempotency-Key`) → mueve stock
21. Admin lectura → `GET /api/v1/admin/stores/{storeId}/purchase-orders` y `.../goods-receipts/{receiptId}`
22. Admin → `PUT /api/v1/admin/stores/{storeId}/service-area` + `.../enable`
23. Anónimo → `POST /api/v1/consumer/coverage` y `POST /api/v1/consumer/catalog/search` (lat/lon en body)
24. Login Consumer → `PUT /api/v1/consumer/cart/items/{productId}` y `POST /api/v1/consumer/orders` (header `Idempotency-Key`)

## Tests

```powershell
dotnet test
```

Los tests de integración levantan **PostGIS** con Testcontainers (`postgis/postgis:17-3.5-alpine`). Docker debe estar en ejecución.

```powershell
dotnet test --collect:"XPlat Code Coverage"
```

Quedan fuera de cobertura (no bajan el porcentaje):

- migraciones EF (`**/Migrations/**`)
- composition root (`Program.cs`)
- factory de diseño de EF (`YaaJuuDbContextFactory`)

Eso se configura en `tests/coverlet.runsettings` y con `[ExcludeFromCodeCoverage]`. Sonar usa las mismas exclusiones en `sonar-project.properties`.

La solución incluye `SonarAnalyzer.CSharp` (el mismo motor de reglas que SonarQube). Las advertencias de Sonar se tratan como error de compilación, excepto en migraciones EF generadas. Cuando instales SonarQube/SonarCloud, usa `sonar-project.properties`.

## Decisiones importantes

- UUID v7 para identificadores públicos.
- UTC (`DateTimeOffset`) en dominio y persistencia.
- Sin soft delete genérico: se usa `Status`.
- Sin wrapper `{ success, data }`: REST + ProblemDetails.
- Paginar listados desde el primer día.
- Precio `$350.000 COP` solo como seed configurable (`PlanSeed`), no como constante de negocio.
- `FranchiseeStatus` ≠ `SubscriptionStatus`.
- Suspender no elimina.
- Consumer Payment ≠ SaaS Subscription.
- `Money` vive en SharedKernel: el costo de compra y el precio de venta son el mismo concepto.
- Números de documento (`PO-`, `GR-`, `ORD-`) desde secuencias PostgreSQL: únicos, no necesariamente consecutivos.
- Procurement mueve stock por un puerto de SharedKernel; Orders reserva stock por otro puerto del mismo kernel. Ninguno referencia Inventory.
- Área de servicio (`StoreServiceArea`) ≠ dirección operativa de la Store; APIs de consumidor no filtran store/tenant/stock/coords.
- Radio de entrega 100..50 000 m (decisión de producto). Suscripción SaaS más allá de `Tenant.Status` = Decision Pending.
- El JWT del consumidor no lleva tenant. `Confirm` de un pedido no compromete inventario.

## Documentación

- [docs/architecture.md](docs/architecture.md)
- [docs/adr/ADR-001-modular-monolith.md](docs/adr/ADR-001-modular-monolith.md)
- [docs/adr/ADR-002-postgresql.md](docs/adr/ADR-002-postgresql.md)
- [docs/adr/ADR-003-multitenancy.md](docs/adr/ADR-003-multitenancy.md)
- [docs/adr/ADR-004-identity.md](docs/adr/ADR-004-identity.md)
- [docs/adr/ADR-005-global-catalog-store-catalog.md](docs/adr/ADR-005-global-catalog-store-catalog.md)
- [docs/adr/ADR-006-pricing-global-suggested-store-override.md](docs/adr/ADR-006-pricing-global-suggested-store-override.md)
- [docs/adr/ADR-007-inventory-ledger-and-balances.md](docs/adr/ADR-007-inventory-ledger-and-balances.md)
- [docs/adr/ADR-008-procurement-purchase-orders-and-goods-receipts.md](docs/adr/ADR-008-procurement-purchase-orders-and-goods-receipts.md)
- [docs/adr/ADR-009-consumer-discovery-geolocation-and-catalog.md](docs/adr/ADR-009-consumer-discovery-geolocation-and-catalog.md)
- [docs/adr/ADR-010-orders-cart-and-inventory-reservations.md](docs/adr/ADR-010-orders-cart-and-inventory-reservations.md)
