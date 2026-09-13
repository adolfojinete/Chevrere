# ADR-003 — Multi-tenancy

## Estado

Aceptado

## Contexto

Un asociado no puede ver datos de otro. Tenant, Franchisee y Store son conceptos distintos. El cliente no es fuente de verdad del tenant.

## Decisión

- Tenant = aislamiento SaaS.
- Franchisee = empresa operadora.
- Store = dark store, 1..N por franchisee.
- `TenantId` viaja en el JWT.
- Query filters de EF Core + policies.
- Usuarios de plataforma tienen `TenantId` nulo y omiten el filtro.

## Consecuencias

- Endpoints de negocio no aceptan `tenantId` del body/query.
- Una Store no puede cruzar tenants.
- La futura configuración de pagos puede anclarse a Franchisee sin rehacer el modelo.
