# Chevrere

Plataforma SaaS + quick-commerce sobre una red de dark stores independientes.

Esta fase entrega la fundación técnica, la administración central de asociados, el catálogo comercial, pricing, inventario y abastecimiento: onboarding atómico de Tenant, Franchisee, Owner, primera Store, Plan y Subscription; ciclo de vida (activar, suspender, reactivar) sin borrar datos; catálogo global Chevrere y habilitación por dark store; precio sugerido global y override por Store; ledger de inventario por dark store; proveedores, órdenes de compra y recepción de mercancía que alimenta ese ledger; auditoría; multi-tenancy; y autenticación de plataforma.

## Objetivo

Funcionalidad mínima, arquitectura seria. Un solo backend desplegable. Módulos internos. PostgreSQL único.

## Arquitectura

Monolito modular en .NET 10 / ASP.NET Core.

```text
Chevrere
├── src
│   ├── Chevrere.Api
│   ├── BuildingBlocks
│   │   ├── Chevrere.SharedKernel
│   │   └── Chevrere.Infrastructure
│   └── Modules
│       ├── Identity     (Domain / Application / Infrastructure)
│       ├── Tenancy      (Domain / Application / Infrastructure)
│       ├── Subscriptions(Domain / Application / Infrastructure)
│       ├── Catalog      (Domain / Application / Infrastructure)
│       ├── Pricing      (Domain / Application / Infrastructure)
│       ├── Inventory    (Domain / Application / Infrastructure)
│       └── Procurement  (Domain / Application / Infrastructure)
├── tests
│   ├── Chevrere.UnitTests
│   ├── Chevrere.IntegrationTests
│   └── Chevrere.ArchitectureTests
├── deploy/docker-compose.yml
└── docs
```

Detalle: [docs/architecture.md](docs/architecture.md).

## Requisitos

- .NET SDK 10.0.401 o superior (feature band 10.0)
- Docker (PostgreSQL local y tests de integración)
- Herramientas EF Core: `dotnet tool install --global dotnet-ef`

## PostgreSQL local

```powershell
cd C:\Users\adolf\source\repos\Chevrere\deploy
docker compose up -d
```

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
cd C:\Users\adolf\source\repos\Chevrere\src\Chevrere.Api
dotnet user-secrets init
dotnet user-secrets set "Bootstrap:SuperAdmin:Email" "admin@chevrere.local"
dotnet user-secrets set "Bootstrap:SuperAdmin:Password" "<elige-una-password-fuerte>"
dotnet user-secrets set "Jwt:SigningKey" "<llave-de-al-menos-32-caracteres>"
```

La password debe cumplir Identity: mínimo 10 caracteres, mayúscula, minúscula, número y símbolo.

Variables de entorno alternativas:

```text
Bootstrap__SuperAdmin__Email=admin@chevrere.local
Bootstrap__SuperAdmin__Password=...
Jwt__SigningKey=...
```

## Migraciones

```powershell
cd C:\Users\adolf\source\repos\Chevrere
dotnet ef migrations add InitialCreate --project src\BuildingBlocks\Chevrere.Infrastructure --startup-project src\Chevrere.Api --output-dir Persistence/Migrations
dotnet ef database update --project src\BuildingBlocks\Chevrere.Infrastructure --startup-project src\Chevrere.Api
```

La API aplica las migraciones pendientes al arrancar, en cualquier ambiente. No se usa `EnsureCreated()`. También puedes aplicarlas a mano:

```powershell
dotnet ef database update --project src\BuildingBlocks\Chevrere.Infrastructure --startup-project src\Chevrere.Api
```

## Ejecución

```powershell
cd C:\Users\adolf\source\repos\Chevrere
dotnet restore
dotnet build
cd deploy
docker compose up -d
cd ..
dotnet run --project src\Chevrere.Api
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

## Tests

```powershell
dotnet test
```

Los tests de integración levantan PostgreSQL con Testcontainers. Docker debe estar en ejecución.

```powershell
dotnet test --collect:"XPlat Code Coverage"
```

Quedan fuera de cobertura (no bajan el porcentaje):

- migraciones EF (`**/Migrations/**`)
- composition root (`Program.cs`)
- factory de diseño de EF (`ChevrereDbContextFactory`)

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
- Números de documento (`PO-`, `GR-`) desde secuencias PostgreSQL: únicos, no necesariamente consecutivos.
- Procurement mueve stock por un puerto de SharedKernel; no referencia Inventory.

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
