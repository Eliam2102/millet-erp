# Cuidados de infraestructura — Módulo Administración

> **Construido sobre:** [01-diseno.md](01-diseno.md) (Rev. 2), [02-plan-implementacion.md](02-plan-implementacion.md) (Rev. 1), [03-pr-breakdown.md](03-pr-breakdown.md) (Rev. 1).
>
> **Hereda contexto de:** [`docs/modulos/compras-ordenes-compra/04-cuidados-infra.md`](../compras-ordenes-compra/04-cuidados-infra.md) y [`docs/modulos/compras-requisiciones/04-cuidados-infra.md`](../compras-requisiciones/04-cuidados-infra.md). Los cuidados de plataforma compartida (interceptors, auditoría, soft delete, Money, IClock, idempotencia HTTP, etc.) son los mismos. Este documento **enfatiza lo nuevo de Admin**.
>
> **Estado:** propuesta para revisión.
> **Fecha:** 2026-05-13.

---

## 0. Cómo leer

Cada cuidado tiene cuatro líneas:

- **Qué**: la regla en una frase.
- **Por qué importa**: una o dos frases que justifican el costo.
- **Cómo lo verificamos**: test, gate de CI, checklist de PR, o revisión manual concreta.
- **Qué pasa si lo ignoramos**: el modo de falla.

Priorización dentro de cada bloque: **P0** = bloqueante para release; **P1** = importante; **P2** = nice-to-have trazable.

> **Reuso primero (regla del proyecto):** muchos cuidados ya están verificados en Compras (RQ + OC). Donde aplique, este doc dice "ver §X del 04 de Compras" en lugar de duplicar.

---

## 1. Multi-DbContext (ADR-0030 + memoria `feedback_dbcontext_nuevo_checklist`)

> Ref: F-Admin-PR0.

### 1.1 [P0] Cada nuevo DbContext requiere actualizar Program.cs + deploy-app-dev.yml en el MISMO PR

- **Qué**: F-Admin-PR0 introduce 4 DbContexts nuevos (`AdministracionDbContext`, `CatalogosDbContext`, `DatosMaestrosDbContext`, `AlmacenDbContext`). Cada uno requiere:
  1. `services.AddDbContext<XxxDbContext>(...)` en `Program.cs`.
  2. `services.Configure<MigrationsHealthCheckOptions>(opts => opts.DbContexts.Add(typeof(XxxDbContext)))`.
  3. Entry correspondiente en la matriz de `deploy-app-dev.yml` (sección de migraciones).
- **Por qué importa**: si falta cualquiera, el deploy a dev falla con `/health/ready` 503 (memoria del incidente que dio origen al checklist).
- **Cómo lo verificamos**: checklist obligatorio del PR; CI ejecuta `/health/ready` después de aplicar migraciones.
- **Qué pasa si lo ignoramos**: deploy bloqueado, debugging tedioso.

### 1.2 [P0] Fase A NO renombra schema físico

- **Qué**: los 4 DbContexts nuevos apuntan a tablas en schema `compartido` por `ToTable("...", schema: "compartido")`. **NO** se ejecuta `ALTER SCHEMA compartido RENAME TO admin`.
- **Por qué importa**: la decisión está cerrada en ADR-0035. Renombrar schema introduce riesgo desproporcionado en MVP.
- **Cómo lo verificamos**: revisar `OnModelCreating` de cada DbContext en el PR; reviewer rechaza si encuentra schema distinto a `compartido` para tablas pre-existentes.
- **Qué pasa si lo ignoramos**: BI/dashboards externos rompen sin aviso; rollback complejo.

### 1.3 [P0] Tablas NUEVAS sí pueden ir a schemas propios

- **Qué**: tablas que **nacen** en este área (`admin.parametros_globales`, `admin.series`, `admin.secuencias_folio`, `compartido.tipos_cambio` por compatibilidad con Moneda existente, etc.) van al schema del módulo dueño cuando aplique Fase B (diferido). En Fase A, todas a `compartido` para mantener un único schema físico.
- **Por qué importa**: coherencia con Fase A. La transición a schemas físicos propios se hace en bloque cuando se ejecute Fase B.
- **Cómo lo verificamos**: checklist; el migration script debe declarar `schema: "compartido"`.
- **Qué pasa si lo ignoramos**: mezcla de schemas en BD; transición a Fase B se vuelve compleja.

---

## 2. Migraciones EF Core (ADR-0005)

> Ref: F-Admin-PR0, PR2.x, PR3.x, PR4.x, PR4.5, PR5.x, PR6.x, PR7.x.

### 2.1 [P0] Cada migración revisada como SQL, no como C#

Mismo cuidado que §1.1 del 04 de Compras OC. Aplica a las **~20 migraciones** que introduce Admin. Reviewer rechaza el PR si el `dotnet ef migrations script` no está adjunto.

### 2.2 [P0] Refactor de namespaces NO genera migraciones nuevas

- **Qué**: F-Admin-PR0 mueve entidades entre namespaces. **No debe generar `dotnet ef migrations add`** porque el schema físico es idéntico al anterior. Si el equipo ejecuta `dotnet ef migrations add` por hábito, EF detectará cambios en `Designer.cs` (que tienen el namespace embebido) y producirá una migración vacía o errónea.
- **Por qué importa**: una migración fantasma confunde el historial.
- **Cómo lo verificamos**: revisión del PR. Si hay nuevas migraciones en `<Modulo>/Infrastructure/Migrations/` con `Up()` vacío o solo cambios de metadata, **rechazar**.
- **Qué pasa si lo ignoramos**: historial sucio, riesgo de divergencia entre `Designer.cs` y el modelo real.

### 2.3 [P0] Seeds idempotentes con `ON CONFLICT DO NOTHING`

- **Qué**: migraciones que seedean catálogos (permisos canónicos, roles MVP, FormasPago SAT, UsosCfdi SAT, RegimenFiscal SAT) deben ser idempotentes vía `ON CONFLICT DO NOTHING`.
- **Por qué importa**: ambientes pueden ya tener filas seedeadas manualmente; la migración debe coexistir.
- **Cómo lo verificamos**: revisión del SQL del migration script; test de re-aplicación (correr la migración dos veces en dev).
- **Qué pasa si lo ignoramos**: deploy falla con violación de UNIQUE; rollback no trivial.

### 2.4 [P0] Migración aditiva en F-Admin-PR6.2 sobre Compras

- **Qué**: F-Admin-PR6.2 toca `Compras.FolioSecuencia` (deprecación gradual) **sin destruirla**. La tabla queda activa, marcada con `PLATFORM-TODO(<FolioSecuenciaDeprecate>)`.
- **Por qué importa**: OCs en flight pueden tener referencias activas. La migración debe ser aditiva.
- **Cómo lo verificamos**: tests integration que crean OC antes y después del PR — ambas coexisten.
- **Qué pasa si lo ignoramos**: OCs huérfanas, folios duplicados.

---

## 3. Permisos canónicos (ADR-0007)

> Ref: F-Admin-PR1.2, PR2.3, PR3.2, PR4.2, PR4.5, PR5.x, PR6.1, PR7.x.

### 3.1 [P0] Cada PR de feature agrega sus permisos a `PermisosCanonicos.cs` + migración seed

- **Qué**: añadir constantes a `PermisosCanonicos.cs` y migración EF Core con `HasData` (delta-detection) o `InsertData` (explícita) para seedear en `identidad.permisos` con GUIDs deterministas.
- **Por qué importa**: si el permiso existe en código pero no en BD, `RequirePermissionAttribute` falla en runtime con 500.
- **Cómo lo verificamos**: test integration que verifica `identidad.permisos` contiene todos los `PermisosCanonicos.Todos`.
- **Qué pasa si lo ignoramos**: 500 en endpoints recién deployados.

### 3.2 [P0] Roles MVP seedeados via `BootstrapSuperAdminHostedService` (no migración)

- **Qué**: F-Admin-PR3.3 extiende `BootstrapSuperAdminHostedService.cs` para seedear los 7 roles base. **No** se hace via migración seed.
- **Por qué importa**: los roles dependen de los permisos seedeados; el hosted service espera a que todos los DbContexts estén ready y los permisos materializados.
- **Cómo lo verificamos**: test integration que ejecuta el hosted service en ambiente limpio y verifica los 7 roles existen con sus permisos.
- **Qué pasa si lo ignoramos**: orden de seed indeterminado; super-admin sin permisos.

### 3.3 [P1] GUIDs deterministas para permisos canónicos

- **Qué**: los GUIDs de permisos canónicos se generan deterministamente desde el string canónico (`Guid.Parse(...)` con namespace fijo) para que sean estables across environments.
- **Por qué importa**: si el mismo permiso tiene GUID distinto en dev/qa/prod, las queries por `PermisoId` fallan al promover datos.
- **Cómo lo verificamos**: test que verifica el mapeo `Canonico → Guid` es estable.
- **Qué pasa si lo ignoramos**: bugs de "funciona en dev, no en prod" por desajuste de GUIDs.

---

## 4. Auditoría (ADR-0008)

> Ref: F-Admin-PR1.1 (PATCH settings), PR2.3, PR3.2, PR4.2, PR5.x, PR6.1, PR7.x.

### 4.1 [P0] Todas las entidades de Admin son `IAuditable`

- **Qué**: Empresa, Sucursal, Departamento, Rol, Usuario, Moneda, TipoCambio, CondicionesPago, Incoterm, Transportista, UnidadMedida, Proveedor, Articulo, Serie, ParametroGlobal, Almacen — todas implementan `IAuditable`.
- **Por qué importa**: el `AuditSaveChangesInterceptor` produce `AuditLogEntry` solo para entidades marcadas como auditables.
- **Cómo lo verificamos**: test que verifica cada PATCH/POST emite `AuditLogEntry`.
- **Qué pasa si lo ignoramos**: bandeja de auditoría incompleta.

### 4.2 [P0] PATCH settings emite `AuditLogEntry`

- **Qué**: el handler genérico de `PATCH /api/v1/{modulo}/settings/{clave}` (PR1.1) emite `AuditLogEntry` con `Detalle.before` y `Detalle.after` (jsonb).
- **Por qué importa**: cambios de configuración son críticos; la bandeja de auditoría debe trazarlos.
- **Cómo lo verificamos**: test integration que PATCHea un setting y verifica `AuditLogEntry` con before/after.
- **Qué pasa si lo ignoramos**: cambios silenciosos al setting; sin trazabilidad.

### 4.3 [P1] Batch atómico de permisos a rol = un solo `AuditLogEntry`

- **Qué**: `AsignarPermisosARolCommand` (PR3.2) procesa N permisos en una transacción y emite **un único** `AuditLogEntry` con el delta consolidado.
- **Por qué importa**: si emite uno por permiso, la bandeja se contamina con cientos de filas por cada cambio de rol.
- **Cómo lo verificamos**: test que asigna 5 permisos y verifica 1 sola entry de audit.
- **Qué pasa si lo ignoramos**: bandeja inusable.

### 4.4 [P0] Bandeja de auditoría requiere rango de fechas obligatorio

- **Qué**: F-Admin-PR7.2 expone `GET /api/v1/admin/auditoria?desde=&hasta=...`. Sin `desde+hasta`, retorna 400 ProblemDetails. Rango máximo: 90 días.
- **Por qué importa**: la tabla `audit_log_entries` crece millones de filas. Sin filtro temporal, query revienta el servidor.
- **Cómo lo verificamos**: test que llama sin rango → 400. Test que llama con rango > 90 días → 400.
- **Qué pasa si lo ignoramos**: query indexed scan sobre toda la tabla; timeout; impacto multiusuario.

---

## 5. Idempotencia HTTP (ADR-0020)

> Ref: F-Admin-PR1.1 (PATCH settings), PR2.3, PR3.2, PR4.2, PR5.x, PR6.1 (ReservarFolio), PR7.1.

### 5.1 [P0] PATCH/POST mutativos requieren `Idempotency-Key`

- **Qué**: todos los endpoints `PATCH` y `POST` (excepto smoke y queries) requieren header `Idempotency-Key: <uuid-v4>`.
- **Por qué importa**: la red puede retransmitir; sin idempotencia, doble alta de Empresa/Rol/Usuario.
- **Cómo lo verificamos**: tests integration por endpoint.
- **Qué pasa si lo ignoramos**: duplicados visibles al usuario.

### 5.2 [P0] `ReservarFolioCommand` es **estrictamente** idempotente

- **Qué**: `POST /api/v1/admin/series/{id}/reservar` con la misma `Idempotency-Key` retorna el **mismo folio** sin avanzar la secuencia.
- **Por qué importa**: si el cliente reintenta y la secuencia avanza, el folio anterior queda huérfano. Pierdes el correlativo.
- **Cómo lo verificamos**: test que llama 3× con la misma key y verifica 1 sola fila en `secuencias_folio` con `UltimoNumero += 1`.
- **Qué pasa si lo ignoramos**: huecos en correlativos (auditoría fiscal pregunta por qué falta el folio X).

### 5.3 [P0] `ReservarFolioCommand` es **transaccionalmente atómico** bajo concurrencia

- **Qué**: SELECT FOR UPDATE sobre `secuencias_folio` + UPDATE + COMMIT en una sola transacción. N reservas concurrentes sin la misma key → folios consecutivos sin colisión.
- **Por qué importa**: race condition con N usuarios reservando folio simultáneamente.
- **Cómo lo verificamos**: test integration con `Task.WhenAll` lanzando 50 reservas en paralelo; verifica los 50 folios son únicos y consecutivos.
- **Qué pasa si lo ignoramos**: dos OCs con el mismo folio. Auditoría revienta.

---

## 6. Multi-empresa (ADR-0011)

> Ref: todos los PRs de Admin.

### 6.1 [P0] Endpoints admin respetan `ICurrentEmpresaContext`

- **Qué**: queries (`ListarEmpresasQuery`, `ListarSucursalesQuery`, etc.) filtran por `EmpresaId` activo. Excepción: super-admin con permiso especial `admin.empresas.leer_transversal` ve todas (no MVP, futuro).
- **Por qué importa**: aislamiento multi-tenant. Un admin de Empresa A no debe ver datos de Empresa B.
- **Cómo lo verificamos**: tests integration que cambian `EmpresaId` activo y verifican aislamiento.
- **Qué pasa si lo ignoramos**: leak de datos cross-tenant.

### 6.2 [P0] `UsuarioEmpresaRol` filtra el acceso

- **Qué**: el `RequirePermissionAttribute` ya consulta `UsuarioEmpresaRol` para validar `(UsuarioId, EmpresaId, RolId).Permisos` vía cache. Permiso `admin.empresas.editar` en Empresa A **no** habilita editar Empresa B.
- **Por qué importa**: aislamiento.
- **Cómo lo verificamos**: tests del attribute con usuario que tiene rol en A pero no en B; intentar PATCH B → 403.
- **Qué pasa si lo ignoramos**: privilegios cruzados.

### 6.3 [P1] Cache de autorización se invalida con `RolPermisosActualizadosEvent`

- **Qué**: cuando un rol cambia permisos (PR3.2), se publica `RolPermisosActualizadosEvent`. Listener invalida cache de autorización para todos los usuarios con ese rol.
- **Por qué importa**: sin invalidación, el cambio se aplica solo en futuras sesiones; el usuario activo sigue con permisos antiguos.
- **Cómo lo verificamos**: test integration que cambia permisos a un rol y verifica usuarios activos pierden/ganan acceso en el siguiente request.
- **Qué pasa si lo ignoramos**: cambios de seguridad no efectivos hasta logout/login.

---

## 7. Concurrencia (ADR-0012)

> Ref: F-Admin-PR2.3, PR3.2, PR4.2, PR6.1.

### 7.1 [P0] Todos los aggregates de Admin tienen `Version IsConcurrencyToken`

- **Qué**: Empresa, Sucursal, Departamento, Rol, Usuario, Moneda, Serie, ParametroGlobal, Almacen — todos heredan de `BaseEntity` con `Version` como concurrency token (ADR-0012 Capa 1).
- **Por qué importa**: dos admins editan la misma Empresa simultáneamente → último gana sin aviso. Optimistic locking previene esto.
- **Cómo lo verificamos**: tests integration que simulan edición concurrente verifican el segundo PATCH falla con 409 Conflict.
- **Qué pasa si lo ignoramos**: pérdida silenciosa de cambios.

### 7.2 [P1] Soft-lock real (UF8 de Compras) **no aplica a Admin** en MVP

- **Qué**: el módulo de admin no participa del `CollaborationHub` SignalR en MVP. Las pantallas admin son típicamente de un solo editor a la vez.
- **Por qué importa**: el costo del soft-lock para entidades que rara vez se editan concurrentemente no se justifica.
- **Cómo lo verificamos**: revisión del PR. Sin código del Hub en Admin.
- **Qué pasa si lo ignoramos**: complejidad innecesaria.

---

## 8. Eventos de integración (ADR-0009 — Outbox)

> Ref: F-Admin-PR2.3, PR3.2, PR4.2.

### 8.1 [P0] Eventos publicados via Outbox, no in-process directo

- **Qué**: `EmpresaCreadaEvent`, `SucursalCreadaEvent`, `RolPermisosActualizadosEvent`, `UsuarioRolAsignadoEvent`, `UsuarioRolRevocadoEvent` se publican vía `IIntegrationEventPublisher` (que escribe al outbox).
- **Por qué importa**: garantías transaccionales. Si el handler falla después de publicar, el evento debe revertir.
- **Cómo lo verificamos**: tests integration verifican `integration_event_outbox` tiene el evento tras el handler.
- **Qué pasa si lo ignoramos**: eventos perdidos o duplicados.

### 8.2 [P1] Mientras no haya Service Bus real, listeners in-process via MediatR

- **Qué**: en dev, `IIntegrationEventPublisher` escribe al outbox + dispatcher local que ejecuta listeners in-process via MediatR `INotification`. Cuando Service Bus esté wireado (post-MVP), el dispatcher cambia a publisher real.
- **Por qué importa**: el wireup de Service Bus es deuda de plataforma (`<ServiceBusWireup>`). No bloquea el MVP de Admin.
- **Cómo lo verificamos**: configuración `appsettings.json` `EventPublisher: InProcess` en dev; revisión que listeners están registrados.
- **Qué pasa si lo ignoramos**: módulos que esperan eventos (ej. Compras esperando `EmpresaCreadaEvent` para crear `ComprasSettings` por defecto) no reciben.

### 8.3 [P0] Compras escucha `EmpresaCreadaEvent` para crear `ComprasSettings` por defecto

- **Qué**: listener en `Compras` consume `EmpresaCreadaEvent` y crea una fila `compras.settings` con defaults (heredados de ADR-0033). Sin esto, OC no puede operar para la empresa nueva.
- **Por qué importa**: la creación de una empresa debe dejar el sistema en estado consistente.
- **Cómo lo verificamos**: test integration crea empresa, verifica `compras.settings` tiene fila con `auto_generar_oc_al_autorizar=false`.
- **Qué pasa si lo ignoramos**: la empresa queda sin settings de Compras; intentar autorizar RQ falla con FK violation.

---

## 9. Health checks (ADR-0019)

> Ref: F-Admin-PR0, todos los PRs con DbContext nuevo.

### 9.1 [P0] `/health/ready` cubre los 4 DbContexts nuevos

- **Qué**: `MigrationsAppliedHealthCheck` debe incluir `AdministracionDbContext`, `CatalogosDbContext`, `DatosMaestrosDbContext`, `AlmacenDbContext` en su configuración.
- **Por qué importa**: si la migración no se aplicó, el endpoint responde 200 sin saberlo y la app crashea en runtime.
- **Cómo lo verificamos**: smoke en CI que ejecuta `/health/ready` después de migrar.
- **Qué pasa si lo ignoramos**: deploy falsamente verde.

---

## 10. Deploy a dev (memoria `feedback_dbcontext_nuevo_checklist`)

> Ref: F-Admin-PR0.

### 10.1 [P0] `deploy-app-dev.yml` actualizado en el mismo PR

- **Qué**: F-Admin-PR0 modifica `.github/workflows/deploy-app-dev.yml` para que el step de migración aplique los 4 DbContexts nuevos:
  ```yaml
  - name: Apply migrations
    run: |
      dotnet ef database update --context AdministracionDbContext --project backend/src/Administracion --startup-project backend/src/Api
      dotnet ef database update --context CatalogosDbContext --project backend/src/Catalogos --startup-project backend/src/Api
      dotnet ef database update --context DatosMaestrosDbContext --project backend/src/DatosMaestros --startup-project backend/src/Api
      dotnet ef database update --context AlmacenDbContext --project backend/src/Almacen --startup-project backend/src/Api
  ```
- **Por qué importa**: sin el step, las migraciones no se aplican en dev; `/health/ready` 503.
- **Cómo lo verificamos**: checklist del PR; CI ejecuta el workflow.
- **Qué pasa si lo ignoramos**: deploy a dev bloqueado.

---

## 11. Contrato `SettingsSchema` (A7=b)

> Ref: F-Admin-PR1.1.

### 11.1 [P0] Endpoint genérico resuelve provider por `{modulo}` con failing-fast

- **Qué**: `GET /api/v1/{modulo}/settings/schema` y `PATCH /api/v1/{modulo}/settings/{clave}` resuelven `ISettingsSchemaProvider` vía `IEnumerable` filtrado por `Provider.Modulo`. Si no hay provider para `{modulo}` → 404.
- **Por qué importa**: una URL ambigua confunde al usuario.
- **Cómo lo verificamos**: test integration que llama `GET /api/v1/inexistente/settings/schema` → 404.
- **Qué pasa si lo ignoramos**: 500 confusos.

### 11.2 [P0] `Mostrar = Custom` requiere `RutaCustom`

- **Qué**: validator en `ISettingsSchemaProvider` valida que si `item.Mostrar = Custom` entonces `item.RutaCustom` no es null.
- **Por qué importa**: el frontend rompe si intenta linkear a null.
- **Cómo lo verificamos**: test unitario en cada provider.
- **Qué pasa si lo ignoramos**: link roto en `/admin`.

### 11.3 [P1] PATCH valida el tipo del valor contra `item.Tipo` antes de persistir

- **Qué**: `Bool` requiere `true|false`, `Int` requiere entero, etc. Si el body no matchea → 422 con `ValidationProblemDetails`.
- **Por qué importa**: persistir `valor` corrupto rompe el read del schema.
- **Cómo lo verificamos**: tests integration por cada tipo de setting.
- **Qué pasa si lo ignoramos**: setting con valor inválido; PATCH siguiente devuelve datos inconsistentes.

---

## 12. Logs y observabilidad (ADR-0006)

### 12.1 [P0] Handlers admin loggean con Serilog estructurado

- **Qué**: cada handler loggea entrada/salida con `_logger.LogInformation("...")` + campos estructurados (`empresaId`, `usuarioId`, `recurso`).
- **Por qué importa**: depurar cross-empresa requiere correlación.
- **Cómo lo verificamos**: revisión del PR; smoke test que verifica al menos un log estructurado por handler.
- **Qué pasa si lo ignoramos**: incidentes opacos.

### 12.2 [P1] PII no se loggea (email, RFC en logs)

- **Qué**: emails de usuarios y RFCs en logs van enmascarados (`@anonimizado`, primeros 3 char + `***`). El `LogMasker` ya está configurado.
- **Por qué importa**: privacidad + LFPDPPP.
- **Cómo lo verificamos**: revisión + test snapshot de log entries.
- **Qué pasa si lo ignoramos**: leak de PII en logs.

---

## 13. Tests (ADR-0016)

### 13.1 [P0] Cobertura por slice — unit + integration donde aplique

- **Qué**: cada PR cubre:
  - Domain: tests unitarios de invariantes.
  - Application: tests unitarios de handler + validator.
  - API: tests integration HTTP→BD (al menos happy path + 403 sin permiso).
- **Por qué importa**: regresiones tempranas.
- **Cómo lo verificamos**: CI obligatorio antes de merge.
- **Qué pasa si lo ignoramos**: bugs en producción.

### 13.2 [P1] Tests de concurrencia para `ReservarFolioCommand`

- **Qué**: F-Admin-PR6.1 incluye test que lanza 50 reservas en paralelo y verifica:
  - 50 folios únicos.
  - 50 folios consecutivos.
  - `secuencias_folio.UltimoNumero` final = inicial + 50.
- **Por qué importa**: race conditions son indetectables sin este tipo de test.
- **Cómo lo verificamos**: test ya escrito en el PR.
- **Qué pasa si lo ignoramos**: duplicados de folio en producción bajo carga.

---

## 14. Política de commits y auto-mode (memoria `feedback_no_commits`)

### 14.1 [P0] Branches `admin/*` → auto-mode N2 activo

- **Qué**: cualquier branch que matchee `^admin/` puede `gh pr merge --squash --delete-branch` sin pedir permiso explícito, **si y solo si** `gh pr checks` reporta todos los buckets en `pass` o `skipping`.
- **Por qué importa**: velocidad del flujo + safety net via hook.
- **Cómo lo verificamos**: hook `.claude/hooks/validate-auto-merge.ps1` ya configurado (PR doc-only #1).
- **Qué pasa si lo ignoramos**: merges manuales innecesarios.

### 14.2 [P0] CI obligatorio antes de merge

- **Qué**: 4 checks (Build backend, Build frontend, Lint frontend, Unit tests backend) deben estar verdes antes del merge automático.
- **Por qué importa**: prevenir regresiones.
- **Cómo lo verificamos**: hook bloquea con exit 2 si no.
- **Qué pasa si lo ignoramos**: merges rotos.

---

## 15. Resumen de cuidados por PR

| PR | Cuidados clave |
|---|---|
| F-Admin-PR0 | §1.1, §1.2, §1.3, §2.2, §9.1, §10.1 |
| F-Admin-PR1.1 | §4.2, §5.1, §11.1, §11.2, §11.3 |
| F-Admin-PR1.2 | §3.1, §3.3 |
| F-Admin-PR2.x | §2.1, §3.1, §6.1, §6.2, §7.1, §8.1, §8.3 |
| F-Admin-PR3.x | §3.1, §3.2, §3.3, §4.3, §6.3, §8.1 |
| F-Admin-PR4.x | §3.1, §6.1, §6.2, §7.1, §8.1 |
| F-Admin-PR4.5 | §3.1 |
| F-Admin-PR5.x | §2.1, §2.3, §3.1 |
| F-Admin-PR6.1 | §3.1, §5.2, §5.3, §7.1, §13.2 |
| F-Admin-PR6.2 | §2.4 |
| F-Admin-PR7.x | §3.1, §4.4 |

## Rev.

- **2026-05-13** — Rev. 1. Cuidados iniciales. Autor: Claude.
