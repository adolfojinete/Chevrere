# ADR-001 — Monolito modular

## Estado

Aceptado

## Contexto

YaaJuu necesita una base de producto real, no una PoC. Aún no existen equipos múltiples, tráfico de consumidores ni bounded contexts operativos que justifiquen distribución.

## Decisión

Un único backend desplegable (.NET 10) con módulos internos (Identity, Tenancy, Subscriptions). Fronteras por proyecto Domain/Application/Infrastructure.

## Consecuencias

- Un pipeline, un proceso, una base de datos.
- Onboarding atómico con un solo `DbContext`.
- La extracción futura de un módulo no requiere reescribir el dominio si se respetan los puertos.
