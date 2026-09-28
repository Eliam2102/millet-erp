# Usuarios seed del modo `FakeForLocalDev`

Este documento describe el modo de pruebas aisladas `FakeForLocalDev`. El
desarrollo compartido usa `EntraId` por defecto. Para activar el modo de
prueba, configura explícitamente `Auth__Mode=FakeForLocalDev` y
`Auth__InitialAdminEntraOid=dev-superadmin` en el backend, junto con
`VITE_AUTH_MODE=FakeForLocalDev` en un archivo `.env.*.local` del frontend.
Los dos lados deben usar el mismo modo. No publiques esos ajustes locales.

Detalle de la decisión: [ADR-0015](decisiones/0015-local-dev-auth.md).

---

## Estado actual (Phase 1)

El `BootstrapSuperAdminHostedService` solo crea el **SuperAdmin** y la
empresa inicial dev en el primer arranque. Los demás usuarios seed previstos
en ADR-0015 todavía no existen en código y se sembrarán en PRs posteriores
cuando los módulos de negocio los necesiten.

| Usuario              | OID sintético       | Rol         | Empresa                        | Estado | Para qué probar |
|----------------------|---------------------|-------------|--------------------------------|--------|-----------------|
| Super Admin (Dev)    | `dev-superadmin`    | super-admin | Millet ERP - Empresa Inicial Dev | ✅ implementado | Cualquier endpoint, RBAC granular, bootstrap, multi-empresa con `Bypass()`. |
| Dev Admin EmpresaA   | `dev-admin-empresa-a` | administrador | Empresa A                    | ⬜ planeado | Administración a nivel empresa sin permisos cross-empresa. |
| Dev Cobrador         | `dev-cobrador`      | cobrador    | Empresa A                      | ⬜ planeado | Cuentas por cobrar, aplicación de pagos, antigüedad de saldos. |
| Dev Facturador       | `dev-facturador`    | facturador  | Empresa A                      | ⬜ planeado | Emisión CFDI 4.0, timbrado, cancelación, notas de crédito. |
| Dev Auditor          | `dev-auditor`       | auditor     | Empresa A                      | ⬜ planeado | Lectura cross-módulo y `auditoria.log.leer`; no modifica datos. |
| Dev Multi-empresa    | `dev-multiempresa`  | distintos   | Empresa A + Empresa B          | ⬜ planeado | Switch de empresa activa, reglas de aislamiento cross-empresa. |

---

## Cómo funciona end-to-end

1. **Backend**: `Auth__Mode=FakeForLocalDev` como variable local.
   El `AuthModeValidator` falla en arranque si este modo se intenta fuera de
   `Development`. El `BootstrapSuperAdminHostedService` crea idempotentemente
   el rol `super-admin`, el usuario con `EntraOid = dev-superadmin`, la
   empresa inicial (`MID010101AAA / Millet Dev`) y la asignación
   usuario↔empresa↔rol.
2. **Frontend**: `VITE_AUTH_MODE=FakeForLocalDev` en `.env.development.local`. El
   `<DevUserSelector />` muestra las tarjetas de usuarios seed y al hacer
   clic llama a `POST /api/dev/fake-login` con el `oid` correspondiente.
3. **`/api/dev/fake-login`**: responde solo en `Development` con
   `Auth:Mode == FakeForLocalDev`; en los demás casos devuelve 404.
   Devuelve un JWT firmado por `Auth:Jwt:SigningKey`.
4. **Backend valida el JWT** con `JwtBearer` y popula `HttpContext.User`. El
   `CurrentUserContext` y el `CurrentEmpresaContext` leen claims y el
   `PermissionAuthorizationHandler` resuelve los permisos via
   `InMemoryPermissionCache`.

---

## Por qué los `oid` no son GUIDs

En producción los `oid` reales de Entra son GUIDs. Los seed usan strings
como `dev-superadmin` por diseño: el primer caracter no-hex hace **imposible**
que un usuario seed se confunda con uno real, incluso si por error un dump
de dev acabara importado en otro ambiente.

---

## Cómo agregar un nuevo usuario seed

Cuando llegue el PR que necesite (por ejemplo) un `dev-cobrador`:

1. Extender `BootstrapSuperAdminHostedService` o crear un
   `BootstrapDevSeedUsersHostedService` separado, condicional a
   `Auth:Mode == FakeForLocalDev`. Mantener idempotencia y bypass del global
   query filter por empresa.
2. Asegurar que el rol del usuario exista en `Identidad.RolesSeed` con los
   permisos canónicos correspondientes (no asignar permisos arbitrarios al
   vuelo — los permisos son catálogo cerrado en `PermisosCanonicos`).
3. Agregar la tarjeta correspondiente en el `<DevUserSelector />` del
   frontend (componente con import condicional `import.meta.env.DEV` para
   que tree-shaking lo elimine del bundle de producción).
4. Actualizar la tabla de este documento (estado ⬜ → ✅).
5. Actualizar [CONTRIBUTING.md](../CONTRIBUTING.md) si la lista visible de
   usuarios cambió.

---

## Garantías de seguridad (resumen)

- **Disponibilidad condicionada**: el endpoint exige `Development` y
  `FakeForLocalDev` en cada solicitud.
- **Validación en arranque**: `AuthModeValidator.EnsureAllowedForEnvironment`
  rechaza `FakeForLocalDev` fuera de Development.
- **Re-validación en request**: el handler del endpoint vuelve a chequear
  `Auth:Mode` y devuelve 404 si no es Development (defense in depth).
- **Bicep en QA/Prod**: `Auth:Mode` se inyecta hardcodeado a `EntraId` desde
  Key Vault; no se puede sobrescribir vía env var ni app setting manual.
- **Frontend**: `<DevUserSelector />` se importa con guard
  `if (import.meta.env.DEV)` y el bundler lo elimina del bundle de prod.

---

## Troubleshooting: Desincronización Frontend/Backend

Si en tu ambiente local el frontend siempre te abre el inicio de sesión real de Microsoft Entra ID pero tú esperabas ver los botones falsos (o viceversa), **tu frontend y tu backend están desincronizados**.

Esto ocurre comúnmente porque Vite prioriza el archivo `.env.local` sobre `.env.development`.

### Escenario: El frontend pide Entra ID pero el backend usa el modo de prueba
- **Causa**: una variable local del backend sobreescribe el modo `EntraId` compartido.
- **Solución para usar Entra**: elimina esa variable o usa `Auth__Mode=EntraId`; conserva `VITE_AUTH_MODE=EntraId` y completa los IDs locales del frontend.
- **Solución para una prueba aislada con usuarios sintéticos**: configura ambos lados en `FakeForLocalDev` y el OID sintético del SuperAdmin en el backend.
