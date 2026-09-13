# ADR-002 — PostgreSQL + EF Core

## Estado

Aceptado

## Contexto

Se necesita un motor relacional, transaccional y conocido para un SaaS multi-tenant inicial.

## Decisión

PostgreSQL como única base. EF Core + Npgsql. Migraciones. `snake_case`. JSONB solo en auditoría.

## Consecuencias

- No MongoDB, no múltiples bases por módulo.
- Transacciones ACID para onboarding.
- Evolución de esquema controlada. Prohibido `EnsureCreated()`.
