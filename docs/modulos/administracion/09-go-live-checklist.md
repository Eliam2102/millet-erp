# Go-live Checklist — Módulo Administración

> **Construido sobre:** todos los docs anteriores (00–08) del módulo y los ADRs aplicables.
>
> **Audiencia:** ingeniería de plataforma + owner. Lista de verificación para considerar el módulo Administración listo para go-live en un ambiente (`dev`, `qa-mini`, `prod`).
>
> **Estado:** propuesta para revisión.
> **Fecha:** 2026-05-13.

---

## 0. Cómo leer

- Cada item se marca **[ ]** (pendiente) o **[x]** (cumplido).
- Severidad: **P0** bloqueante; **P1** importante (no bloquea pero hay que cerrarlo en la primera semana); **P2** trazable.
- Items con **[verificar manual]** requieren intervención humana; los demás se validan vía test/CI/healthcheck.
- Distinción entre **"Admin desplegado"** (código en `main` + deploy a ambiente) y **"Admin listo para consumir"** (otros módulos pueden empezar a usar la API/UI sin sorpresas). Algunos items aplican solo al segundo.

> Esta lista se ejecuta **al cierre de cada PR backend mergeado** (smoke obvio) y **completa al cierre de la última fase** (F-Admin-PR7). Para qa-mini/prod, se re-corre íntegramente.

---

## 1. Pre-go-live — antes del primer PR de código

### 1.1 Documentación completa

- [x] PR doc-only #1: ADR-0034 + ADR-0035 + 00-levantamiento + 01-diseño (mergeado 2026-05-13, #164).
- [x] PR doc-only #2: 02-plan + 03-pr-breakdown + 04-cuidados-infra + 05-frontend-diseno + 06-frontend-plan + 07-frontend-pr-breakdown (mergeado 2026-05-13, #165).
- [x] PR doc-only #3: 08-operacion-y-runbook + 09-go-live-checklist (este PR).
- [x] Asunciones A1–A7 cerradas por el owner.
- [ ] [verificar manual] Owner ha revisado y aprobado plan y PR breakdown.

### 1.2 ADRs aplicables firmados

- [x] ADR-0034 — modelo híbrido (Aceptada).
- [x] ADR-0035 — re-localización SharedKernel (Aceptada).
- [x] ADRs heredados aplicables: ADR-0003, 0005, 0007, 0008, 0011, 0028, 0030, 0031, 0033.

### 1.3 Auto-mode preparado

- [x] Hook `validate-auto-merge.ps1` reconoce `admin/*` (mergeado en PR #164).
- [x] Memoria `feedback_no_commits` documenta el scope ampliado.

---

## 2. Por cada PR backend mergeado

Estos checks se ejecutan al cierre de cada `F-Admin-PR*` (parcialmente automatizados via CI).

### 2.1 CI verde

- [ ] **[P0]** `Build backend` pass.
- [ ] **[P0]** `Build frontend` pass.
- [ ] **[P0]** `Lint frontend` pass.
- [ ] **[P0]** `Unit tests backend` pass.

### 2.2 Migraciones aplicables

- [ ] **[P0]** `dotnet ef migrations list --context <DbContextRelevante>` muestra la migración nueva sin gaps.
- [ ] **[P0]** `dotnet ef database update --context <DbContextRelevante>` aplica sin errores en dev.
- [ ] **[P0]** `dotnet ef migrations script <prev> <new>` adjunto al PR y revisado por el reviewer (§2.1 del 04-cuidados).
- [ ] **[P0]** Si la migración seedea datos, es **idempotente** (`ON CONFLICT DO NOTHING`).

### 2.3 Health checks

- [ ] **[P0]** `/health/ready` responde 200 después del deploy.
- [ ] **[P0]** `MigrationsAppliedHealthCheck` cubre el `DbContext` afectado (si es nuevo).

### 2.4 Permisos canónicos

- [ ] **[P0]** Constantes nuevas agregadas a `PermisosCanonicos.cs`.
- [ ] **[P0]** Migración seed presente para los nuevos permisos.
- [ ] **[P0]** Tabla `identidad.permisos` contiene todos los `PermisosCanonicos.Todos` (test integration).
- [ ] **[P1]** GUIDs deterministas (§3.3 del 04-cuidados).

### 2.5 Auditoría

- [ ] **[P0]** Aggregates nuevos implementan `IAuditable` (§4.1 del 04-cuidados).
- [ ] **[P0]** PATCH a settings emite `AuditLogEntry` (§4.2 del 04-cuidados).
- [ ] **[P1]** Batch operations emiten **un solo** `AuditLogEntry` consolidado (§4.3 del 04-cuidados).

### 2.6 Idempotencia HTTP

- [ ] **[P0]** Endpoints `PATCH`/`POST` mutativos requieren `Idempotency-Key` (§5.1 del 04-cuidados).
- [ ] **[P0]** Si el PR introduce `ReservarFolioCommand`, test integration con 50 reservas paralelas pasa (§13.2 del 04-cuidados).

### 2.7 Multi-empresa y segmentación por sucursal

- [ ] **[P0]** Queries filtran por `EmpresaId` activo (§6.1 del 04-cuidados).
- [ ] **[P0]** Test de aislamiento cross-tenant pasa.
- [ ] **[P0]** Si el PR expone un endpoint "por sucursal" (listar/consultar), llama a `SucursalScopeGuard.VerificarAsync` con el permiso de bypass propio del recurso (§6.4-§6.5 del 04-cuidados, ADR-0051).
- [ ] **[P0]** Test integration: usuario sin `UsuarioSucursal` para la sucursal consultada y sin permiso de bypass recibe 403 `SUCURSAL_NO_ASOCIADA`.
- [ ] **[P1]** El rol "corporativo" del seed/bootstrap recibe explícitamente el permiso de bypass del recurso nuevo (no asumir herencia por prefijo).

### 2.8 Concurrencia

- [ ] **[P0]** Aggregates nuevos heredan de `BaseEntity` con `Version IsConcurrencyToken` (§7.1 del 04-cuidados).
- [ ] **[P0]** Test de edición concurrente verifica 409 Conflict.

### 2.9 Eventos de integración

- [ ] **[P0]** Eventos publicados via Outbox (§8.1 del 04-cuidados).
- [ ] **[P0]** Si aplica, listeners cross-módulo materializan estados dependientes (ej. `EmpresaCreadaEvent` → `ComprasSettings`, §8.3 del 04-cuidados).

### 2.10 Tests

- [ ] **[P0]** Cobertura por slice: unit (domain) + unit (application) + integration (API).
- [ ] **[P0]** Happy path + 403 sin permiso + invariantes.
- [ ] **[P1]** Tests E2E Playwright para PRs frontend.

### 2.11 Auto-mode N2

- [ ] **[P0]** PR mergeable solo cuando `gh pr checks` reporta todos los buckets `pass`/`skipping`.
- [ ] **[P0]** Hook bloquea `gh pr merge` si CI no está verde.

---

## 3. Por cada PR frontend mergeado

Adicionales a los checks generales:

### 3.1 OpenAPI tipos TS

- [ ] **[P0]** Tipos TS regenerados tras cambios en endpoint (`openapi.json` actualizado).

### 3.2 Patrones UI

- [ ] **[P0]** Master-detail P3 implementado correctamente (lista 320px sticky + panel detalle).
- [ ] **[P0]** "Nuevo X" usa Sheet P4 (NO modal).
- [ ] **[P0]** Items dentro de master usan **inline forms** (memoria `feedback_inline_no_modal_para_items`).
- [ ] **[P1]** Bandeja read-only SAT muestra banner "Mantenido vía migración SAT".

### 3.3 Engrane y registry

- [ ] **[P0]** Card del módulo agregada a su barrel `<modulo>/admin.ts`.
- [ ] **[P0]** Filtrado por permisos funciona (`useAdminRegistry()`).
- [ ] **[P0]** Engrane visible sólo si hay al menos una card con permiso.

### 3.4 Auto-render settings

- [ ] **[P0]** Si el módulo expone `SettingsSchema`, `/admin/<modulo>/settings` renderiza correctamente.
- [ ] **[P0]** Items con `Mostrar = Custom` linkean a `RutaCustom`.
- [ ] **[P1]** `AlertaCambio` muestra confirm dialog antes de PATCH.

### 3.5 Accesibilidad

- [ ] **[P1]** Lighthouse accessibility ≥ 95 en la página principal del PR.
- [ ] **[P1]** Tabbing lógico en forms.
- [ ] **[P1]** `aria-label` en iconos sin texto.

---

## 4. Bootstrap del ambiente (al deploy de cualquier ambiente)

### 4.1 Bootstrap super-administrador

- [ ] **[P0]** Secrets en Key Vault: `BootstrapSuperAdminEmail` (y `BootstrapSuperAdminEntraIdObjectId` en qa/prod).
- [ ] **[P0]** `BootstrapSuperAdminHostedService` ejecutó al startup (logs).
- [ ] **[P0]** Query SQL del §1.1 del 08-runbook retorna ≥ 1 fila.

### 4.2 Seeds aplicados

- [ ] **[P0]** Permisos canónicos seedeados (todos los `PermisosCanonicos.Todos`).
- [ ] **[P0]** 7 roles MVP seedeados (verificar query del §1.3 del 08-runbook).
- [ ] **[P0]** Catálogos SAT: FormasPago, UsosCfdi, RegimenFiscal, UnidadMedida, Incoterms (counts esperados §1.4 del 08-runbook).
- [ ] **[P0]** Monedas base: MXN, USD, EUR.
- [ ] **[P0]** Parámetros globales: TimezoneDefault, FormatoFecha, RedondeoMonetario, IdiomaDefault.
- [ ] **[P1]** Empresa MVP creada (manual via UI o seed específico del ambiente).
- [ ] **[P1]** Sucursales y departamentos de la empresa MVP creados.

### 4.3 Mapeo Entra ID

- [ ] **[P1]** Grupos Entra ID del cliente identificados.
- [ ] **[P1]** Mapeo `Grupo → Rol` configurado vía `/admin/roles/$id` tab "Grupos Entra ID" (§1.6 del 08-runbook).

### 4.4 Series y folios

- [ ] **[P1]** Series mínimas creadas por empresa para los módulos activos (§1.7 del 08-runbook).
- [ ] **[P1]** `Compras.FolioSecuencia` migrada a `Administracion.Serie` (F-Admin-PR6.2).
- [ ] **[P2]** `PLATFORM-TODO(<FolioSecuenciaDeprecate>)` rastreado.

### 4.5 Tipos de cambio

- [ ] **[P1]** Si hay módulos multimoneda activos (Compras OC importaciones), tipo de cambio del día registrado para cada moneda relevante.
- [ ] **[P2]** `PLATFORM-TODO(<TipoCambioSync>)` rastreado para sync automatizado futuro.

---

## 5. E2E del super-admin (smoke completo)

Estos tests E2E ejecutan en Playwright + verifican el flujo crítico end-to-end. Pasan al cierre de cada feature relevante.

### 5.1 Identidad

- [ ] **[P0]** Super-admin puede crear un usuario.
- [ ] **[P0]** Super-admin puede crear un rol.
- [ ] **[P0]** Super-admin puede asignar permisos a un rol vía matriz.
- [ ] **[P0]** Super-admin puede asignar rol a un usuario en una empresa.
- [ ] **[P0]** El usuario recién creado puede hacer login y ve las opciones según su rol.
- [ ] **[P0]** Super-admin **no puede** desactivarse si es el único super-admin.

### 5.2 Organización

- [ ] **[P0]** Super-admin puede crear una empresa.
- [ ] **[P0]** Super-admin puede agregar sucursales vía inline form.
- [ ] **[P0]** Super-admin puede agregar departamentos vía inline form.
- [ ] **[P0]** `EmpresaCreadaEvent` se publica y Compras recibe `ComprasSettings` por defecto.

### 5.3 Catálogos

- [ ] **[P0]** Admin de catálogos puede registrar tipo de cambio del día para USD.
- [ ] **[P1]** Admin de catálogos puede crear nueva CondicionesPago.
- [ ] **[P1]** Listas SAT (FormasPago, UsosCfdi, RegimenFiscal) son read-only desde UI.

### 5.4 Series

- [ ] **[P0]** Admin organizacional puede crear una serie con cada modo de reinicio (Eterno, Anual, Mensual).
- [ ] **[P0]** Preview del próximo folio es correcto en cada modo.
- [ ] **[P0]** 50 reservas concurrentes (load test) no producen folios duplicados.

### 5.5 Datos Maestros

- [ ] **[P1]** Admin de datos maestros puede dar de alta un proveedor.
- [ ] **[P1]** Filtros server-side funcionan en bandeja de proveedores.

### 5.6 Settings y auto-render

- [ ] **[P0]** Compras card en `/admin` linkea a `/compras/configuracion` (custom).
- [ ] **[P0]** PATCH al `AutoGenerarOcAlAutorizar` desde `/compras/configuracion` emite `AuditLogEntry`.
- [ ] **[P1]** Cuando otro módulo (Facturación, CxP, etc.) agregue settings auto-rendered, `/admin/<modulo>/settings` los renderiza correctamente.

### 5.7 Auditoría

- [ ] **[P0]** Auditor puede consultar bitácora con rango de fechas.
- [ ] **[P0]** Consulta sin rango retorna 400 con mensaje claro.
- [ ] **[P0]** Detalle JSON diff visible en drawer.

### 5.8 Visibilidad del engrane

- [ ] **[P0]** Usuario con cero permisos admin **no ve** el engrane en topbar.
- [ ] **[P0]** Usuario con `compras.configuracion.leer` **ve** el engrane y al hacer click solo ve la card de Compras.
- [ ] **[P0]** Super-admin ve el engrane y todas las cards.

---

## 6. Performance baseline (al cierre de F-Admin-PR7)

- [ ] **[P1]** `GET /api/v1/admin/empresas` (10 empresas) responde < 200ms.
- [ ] **[P1]** `GET /api/v1/admin/usuarios?empresaId=X` (100 usuarios) responde < 500ms.
- [ ] **[P1]** `GET /api/v1/admin/auditoria?desde=Y&hasta=Z` (rango 30 días, 10K entradas) responde < 2s con paginación.
- [ ] **[P1]** Landing `/admin` Visible en < 200ms post-mount (registry es client-side).
- [ ] **[P0]** `ReservarFolioCommand` bajo carga (50 concurrent) no produce duplicados y mantiene `p99 < 100ms`.

---

## 7. Plan de rollback

### 7.1 Rollback de un PR específico

**Cuándo:** un PR mergeado introduce regresión crítica (P0/P1) imposible de hotfixear en < 1 hora.

**Procedimiento:**

1. `git revert <commit-sha>` en `main`.
2. Revertir migraciones EF Core (si las hubo):
   ```bash
   dotnet ef database update --context <DbContext> --target <previous_migration>
   ```
3. Crear PR de rollback. Bypass del auto-mode si la situación lo amerita (con permiso explícito del owner).
4. Mergear.
5. Documentar en post-mortem.

**Mitigación de migraciones destructivas:** ADR-0035 explícitamente NO renombra schemas físicos en Fase A para mantener rollback simple.

### 7.2 Rollback de toda el área Admin

**Cuándo:** decisión arquitectónica reversa.

**Procedimiento:**

1. Disable feature flag `/admin` (deuda futura — feature flags no existen aún en MVP).
2. Si no hay flag: ocultar engrane editando `Topbar.tsx` y deshabilitar rutas `/admin/*` en router.
3. Datos persisten (sin DROP de tablas). El área queda "dormida".

**Probabilidad:** baja. ADR-0034 y ADR-0035 están firmados.

### 7.3 Rollback del bootstrap del super-admin

**Si el bootstrap creó un super-admin no deseado:**

```sql
UPDATE identidad.usuarios SET activo = false WHERE email = '<email-incorrecto>';
```

Luego corregir secret en Key Vault y re-startear (idempotencia garantiza que el correcto se crea sin duplicar).

---

## 8. Cierre de dependencias de plataforma

### 8.1 Cerrados al go-live de F-Admin-PR7

- [ ] **[P0]** `<AuditUI>` — endpoint `GET /api/v1/admin/auditoria` activo + UI funcional.
- [ ] **[P0]** `<SettingsAutoRender>` — contrato `SettingsSchema` activo + UI auto-render funcional.
- [ ] **[P1]** `<FolioSecuenciaDeprecate>` — `Compras.FolioSecuencia` deprecada con TODO; cerrar cuando todos los módulos consuman `ReservarFolioCommand`.

### 8.2 Pendientes post-go-live

- `<EntraIdMapping>` — sync automatizado.
- `<TipoCambioSync>` — sync DOF/Banxico.
- `<SchemaRename>` — Fase B ADR-0035.
- `<EntraIdResolver>` — wireup Graph API real.
- `<EntraDirectorio>` — adaptador Graph del directorio Entra (buscar/crear cuentas) para el alta unificada.
- `<ServiceBusWireup>` — eventos cross-process.
- `<CatalogosSapImport>` — migración masiva.

---

## 9. Comunicación al go-live

### 9.1 Anuncio interno

Al cierre de la última fase (F-Admin-PR7 + UF-Admin-PR7), comunicar:

- Lista de funcionalidades activas en `/admin/*`.
- 7 roles MVP disponibles y sus permisos.
- Procedimientos del 08-runbook accesibles para el equipo.
- Cómo solicitar nuevos permisos / cambio de rol.
- Contacts del §7 del 08-runbook.

### 9.2 Capacitación

- Sesión con super-admins (owner + owner-delegate): walkthrough completo del área Admin.
- Sesión con admins de departamento: alta de usuarios, asignación de roles, consulta de bitácora.
- Documentación de usuario final (deuda separada, no Admin-específica).

---

## 10. Definición de "Done"

El módulo Administración está **Done** cuando:

1. ✅ Los 19 PRs backend están en `main`.
2. ✅ Los 10 PRs frontend están en `main`.
3. ✅ Los 3 PRs doc-only están en `main` (#164, #165, este PR).
4. ✅ Todos los checks de §2 (por PR) cumplidos para cada PR mergeado.
5. ✅ Todos los checks de §3 cumplidos para PRs frontend.
6. ✅ Bootstrap del ambiente target completo (§4).
7. ✅ E2E del super-admin pasan (§5).
8. ✅ Performance baseline cumplido (§6).
9. ✅ Dependencias de plataforma cerradas que correspondan al MVP (§8.1).
10. ✅ Comunicación interna y capacitación completadas (§9).

A partir de aquí, el módulo Administración es exemplar para los 9 módulos restantes del back-office.

---

## Rev.

- **2026-05-13** — Rev. 1. Go-live checklist inicial. Autor: Claude.
