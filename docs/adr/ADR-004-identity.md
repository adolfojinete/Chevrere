# ADR-004 — ASP.NET Core Identity + JWT

## Estado

Aceptado

## Contexto

Se requiere autenticación de plataforma y de asociados, hash estándar de contraseñas y RBAC. No se implementará criptografía propia.

## Decisión

ASP.NET Identity (`IdentityCore`) para almacenamiento y hash. JWT Bearer para la API. Roles de plataforma y `FranchiseeOwner`. Bootstrap del SuperAdmin por User Secrets / variables de entorno, nunca por SQL con password en claro.

## Consecuencias

- Sin cookies de Identity en la API.
- El Owner se inserta en la misma transacción del onboarding, sin `UserManager.CreateAsync` (ese método haría `SaveChanges` intermedio).
- Policies (`PlatformOperators`, `PlatformStaff`, `FranchiseeOwner`) en lugar de `[Authorize]` genérico.
