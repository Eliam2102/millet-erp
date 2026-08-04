# PR D — Integraciones.Aw Endpoints REST + Application Queries/Commands

Branch: `feature/integraciones-aw-endpoints` (sucede a PR C en main).

Implementa los 6 endpoints REST del módulo Integraciones.Aw bajo
`/api/v1/integraciones/aw/cotizaciones/*`, los 2 commands nuevos
(`Reintentar`, `MarcarResueltoManual`), las 3 queries
(`ObtenerDetalle`, `ListarCotizaciones`, `ObtenerHistorial`), Problem
Details types canónicos del módulo, y modificaciones autorizadas a
PR B (jerarquía de exceptions + nuevo método `Reintentar()` en el
aggregate).

Diseño de referencia: `docs/integration/01-api-contract.md`.

---

## Hallazgos de TAREA 0

- **A.** RegistrarCotizacionEdiHandler + Response ✓ (PR B). Workers ✓ (PR C).
  9 PermisosCanonicos.IntegracionesAw* declarados.
- **B.** Patrón endpoints Compras: `MapGroup("/api/v1/<modulo>/...")` +
  `[FromQuery]` + `RequireAuthorization(PermissionPolicyProvider.Prefix + ...)`
  + DTOs Application reusados como HTTP DTOs (sin carpeta `Contracts/`).
- **C.** `ICurrentUserContext.UserId` unifica SP/humano (PR A) ✓ — audit
  transparente.
- **D.** `ConflictException` (409), `EntityNotFoundException` (404),
  `ForbiddenException` (403), `BusinessRuleException` (422) ✓.
- **E.** Exceptions PR B `: DomainException` ✓ — re-jerarquía autorizada
  aplicada.
- **F.** `PagedResponse<T>` Compras `(Items, Offset, Limit, Total)` ✓ —
  Aw-local idéntico creado.
- **G.** Scalar configurado ✓ (Program.cs líneas 264, 573).

### Bloqueador detectado en TAREA 0 (resuelto Opción A)

`ConflictException` era `sealed` — incompatible con la modificación
autorizada del v3 (cambiar herencia de exceptions PR B). **Opción A
elegida**: des-sellar `ConflictException` (1 línea, retrocompat 100%).
Documentado en el archivo afectado.

---

## Cambios

### Modificaciones autorizadas (cross-PR)

**SharedKernel** (autorizado por bloqueador TAREA 0):
- `ConflictException.cs`: removido `sealed`, comentario de razón (PR D).

**PR B exceptions** (autorizado por v3):
- `QuoteReferenceDuplicadaException.cs`: hereda de `ConflictException`
  (era `DomainException`). Pasa code constante a base ctor.
- `InvalidStateTransitionException.cs`: hereda de `ConflictException`
  (era `DomainException`). Pasa code constante a base ctor.

**PR B aggregate** (autorizado por v3):
- `EntidadExterna.cs`: agregado método público `Reintentar()` que valida
  `Estado ∈ {FailedDrop, ManuallyResolved}` → transiciona a `Submitted`,
  resetea `RetryCount=0` + `LastError=null`. Throws
  `InvalidStateTransitionException` si estado no permite. Comment incluye
  `// PLATFORM-TODO(<ResubmittedAtTracking>)`.

**Tests de PR B**:
- `EntidadExternaStateMachineTests.cs`: agregados 6 tests de `Reintentar()`
  cubriendo happy + 5 estados que tiran (Submitted, AwaitingCorrelation,
  Correlated, FailedCorrelation, ManuallyResolved happy).

### Application — Commands nuevos

`backend/src/Integraciones.Aw/Application/Commands/`:

- **`ReintentarCotizacion/`** — Command + Handler + Response.
  - Carga entidad por id (404 si no existe).
  - Llama `entidad.Reintentar()` (aggregate valida estado → 409 si no aplica).
  - Emite `AwCotizacionRecibida` al Outbox para reactivar `AwDropWorker`.
  - SaveChanges + retorna response con id, quoteRef, estado, reintentadoEn.
- **`MarcarResueltoManual/`** — Command + Handler + Response.
  - Valida que `Nota` no esté vacía (`BusinessRuleException` 422).
  - Valida que el caller tenga `UserId` (`ForbiddenException` 403 si SP).
  - Carga entidad (404 si no existe).
  - Comment inline documentando intención: command rechaza estados activos
    (Submitted/AwaitingCorrelation) — el aggregate técnicamente los permite
    pero el caso de uso administrativo requiere esperar al desenlace.
  - Llama `entidad.MarcarResueltoManual(nota, operadorId)`.

### Application — Queries nuevas

`backend/src/Integraciones.Aw/Application/Queries/`:

- **`ObtenerCotizacionDetalle/`** — Query + Handler + Response.
  - Carga aggregate + correlación más reciente + 10 envíos más recientes.
  - Empresa filter automático (global query filter).
- **`ListarCotizaciones/`** — Query + Handler + `CotizacionResumenItem`
  + `PagedResponse<T>` (Aw-local, idéntico a Compras).
  - Filtros opcionales: estado, desde, hasta, quoteReference (exact),
    quoteReferenceSearch (`ILike` Postgres, min 4 chars), awDocId.
  - Paginación offset-based (default 50, max 200).
  - Sort `SubmittedAt DESC` con tiebreaker por `Id`.
- **`ObtenerHistorialCotizacion/`** — Query + Handler + `HistorialResponse`
  + `HistorialItem`.
  - Retorna envíos en orden cronológico ascendente (por `AttemptNumber`).

### Endpoints REST

`backend/src/Api/Endpoints/IntegracionesAw/IntegracionesAwEndpoints.cs`:

| Método | Ruta                              | Permiso requerido                            |
|--------|-----------------------------------|----------------------------------------------|
| POST   | `/cotizaciones`                   | `integraciones.aw.cotizaciones.crear`         |
| GET    | `/cotizaciones`                   | `integraciones.aw.cotizaciones.consultar`     |
| GET    | `/cotizaciones/{id}`              | `integraciones.aw.cotizaciones.consultar`     |
| GET    | `/cotizaciones/{id}/historial`    | `integraciones.aw.cotizaciones.consultar`     |
| POST   | `/cotizaciones/{id}/reintentar`   | `integraciones.aw.cotizaciones.reintentar`    |
| POST   | `/cotizaciones/{id}/marcar-resuelto` | `integraciones.aw.cotizaciones.reintentar` |

- Auth via `[RequireAuthorization(PermissionPolicyProvider.Prefix + ...)]`.
- POSTs declaran `.WithMetadata(new RequireIdempotencyKeyAttribute())`.
- OpenAPI annotations: `WithSummary`, `WithDescription`, `Produces<>`,
  `ProducesProblem`. Tag `"Integraciones AW"` para agrupar en Scalar.
- Query params: `[FromQuery]` con camelCase (alinea al body JSON del repo).

### Problem Details

`backend/src/Api/Web/ProblemDetailsCatalog/IntegracionesAwProblemTypes.cs`:

| Type URL                                                          | HTTP | Origen                                      |
|-------------------------------------------------------------------|------|---------------------------------------------|
| `https://millet-erp/errors/aw_cotizacion_no_encontrada`           | 404  | `EntityNotFoundException`                   |
| `https://millet-erp/errors/aw_quote_reference_duplicada`          | 409  | `QuoteReferenceDuplicadaException`          |
| `https://millet-erp/errors/aw_invalid_state_transition`           | 409  | `InvalidStateTransitionException`           |
| `https://millet-erp/errors/aw_nota_requerida`                     | 422  | `BusinessRuleException` (MarcarResuelto)    |
| `https://millet-erp/errors/aw_operador_requerido`                 | 403  | `ForbiddenException` (MarcarResuelto sin user) |
| `https://millet-erp/errors/idempotency_in_progress`               | 409  | Middleware (idempotency key conflict)       |
| `https://millet-erp/errors/cross_tenant_violation`                | 403  | `CrossTenantViolationException`             |

**Sin cambios al `GlobalExceptionHandler`** — `ConflictException` ya
mapea a 409 (línea 74); el `Type` URL se construye automáticamente
desde `code.ToLowerInvariant()`.

### appsettings + Program.cs

- `Program.cs`: agregada llamada a `MapIntegracionesAwEndpoints()` después
  del map de Identidad (línea ~523).
- `Directory.Packages.props`: agregado `Microsoft.EntityFrameworkCore.InMemory`
  9.0.4 para tests unit que usan DbContext fake.
- `Aw.UnitTests.csproj`: agrega referencia al package InMemory.

---

## Verificación

- `dotnet build Millet.sln` → **0 errors**, 4 warnings pre-existentes
  (version conflict MSB3277).
- **Unit tests verdes**: 71 (`Aw.UnitTests`)
  - PR B Domain state machine: 21 (incluyendo 6 nuevos de `Reintentar()`)
  - PR C adapters + workers + meter: 32
  - PR D handlers: 18 (Reintentar 4, MarcarResuelto 7, Queries 7)
- **Integration tests verdes**:
  - `Aw.IntegrationTests` (wiring): 8 ✓
  - `Api.IntegrationTests/Hubs/IntegracionesAwHubSmokeTests`: 2 ✓
  - `Api.IntegrationTests/IntegracionesAw/CotizacionesEndpointsTests`
    (nuevos PR D): **8 ✓**
- **Sin regresión**: SharedKernel 76, Compras 405.

### Cobertura E2E del PR D (8 tests)

1. `PostCotizacion_SinToken_Retorna401` — auth required
2. `GetCotizaciones_SinToken_Retorna401` — auth required
3. `PostCotizacion_HappyPath_Retorna202_ConId` — happy path
4. `PostCotizacion_QuoteReferenceDuplicada_Retorna409` — exception → 409
5. `GetDetalle_IdNoExiste_Retorna404` — empresa filter + null detalle
6. `GetCotizaciones_HappyPath_Retorna200_ConPagedResponse` — paged shape
7. `GetCotizaciones_FiltroEstado_FiltraCorrectamente` — query filter
8. `PostReintentar_CotizacionEnSubmitted_Retorna409` — InvalidState → 409

**Diferidos a PR siguiente** (`// PLATFORM-TODO(<AwE2EStateSetup>)`): tests
que requieren manipular el aggregate directamente en BD para llegar a
estados intermedios (ej. `Reintentar` desde `FailedDrop`, `MarcarResuelto`
desde `FailedCorrelation`). Necesitan un helper de seed via repo + bypass.

---

## Decisiones cerradas / divergencias documentadas

- **Enums se serializan como int** (default ASP.NET Core), no PascalCase
  string como sugería el v1 del prompt. Compras usa el mismo patrón —
  introducir `JsonStringEnumConverter` global sería breaking change cross-PR.
  Tests E2E asertan `GetInt32()` sobre `estado`. Si el frontend prefiere
  strings, configurar `JsonStringEnumConverter` en un PR posterior y
  actualizar contrato.
- **`MarcarResueltoManualCommand` valida `Estado ∈ {FailedDrop, FailedCorrelation}`**
  — más estricto que el aggregate (que permite cualquier estado activo
  no-terminal). Comment inline documenta intención.
- **`ConflictException` des-sellada** — autorizado por usuario después
  de detectar bloqueador en TAREA 0. Cambio retrocompat 100%.
- **Carpeta `Contracts/` NO creada** — DTOs Application reusados como
  HTTP DTOs (patrón Compras). El doc 01-api-contract.md sigue siendo
  referencia conceptual; el código serializa el shape directo.

---

## PLATFORM-TODOs introducidos

- `<RateLimitingPerSpn>` — rate limit por SP (ej. Glass Agent 100 req/min).
- `<AdminMassRetry>` — endpoint admin para reintentar N cotizaciones a la vez.
- `<CotizacionExportCsv>` — export CSV/Excel para reporting financiero.
- `<ResubmittedAtTracking>` — campo `ResubmittedAt` si surge necesidad
  de auditar reintentos individualmente (hoy `SubmittedAt` no se actualiza
  por diseño).
- `<AwE2EStateSetup>` — helper de seed para tests E2E de estados
  intermedios (Reintentar desde FailedDrop, MarcarResuelto desde
  FailedCorrelation).
- `<ForceResolveInFlight>` — endpoint admin para forzar `MarcarResuelto`
  desde estados activos si el caso de uso aparece.

---

## Cómo probar localmente

```bash
dotnet build backend/Millet.sln  # 0 errors

dotnet test backend/tests/Integraciones.Aw.UnitTests/         # 71 tests
dotnet test backend/tests/Integraciones.Aw.IntegrationTests/  #  8 tests

# Endpoints E2E (requieren BD dev levantada con bootstrap completo):
dotnet test backend/tests/Api.IntegrationTests/ \
    --filter "FullyQualifiedName~CotizacionesEndpointsTests"  # 8 tests
```

Para probar manualmente con un curl + JWT del SuperAdmin (dev):

```bash
# 1. Obtener token via fake-login
TOKEN=$(curl -s -X POST http://localhost:5000/api/dev/fake-login \
  -H "Content-Type: application/json" \
  -d '{"entraOid":"dev-superadmin","email":"superadmin@dev.local","nombre":"SA"}' \
  | jq -r .accessToken)

# 2. POST cotización
curl -X POST http://localhost:5000/api/v1/integraciones/aw/cotizaciones \
  -H "Authorization: Bearer $TOKEN" \
  -H "Idempotency-Key: $(uuidgen)" \
  -H "Content-Type: application/json" \
  -d '{
    "quoteReference":"Q-2026-99001",
    "ediContent":"...200+ chars...#END#",
    "customerTaxId":"EXT",
    "customerName":"Cliente Test",
    "source":"glass_agent",
    "itemsCount":1,
    "payloadOriginalJson":"{}"
  }'
```

OpenAPI doc disponible en `/scalar/v1` con tag "Integraciones AW".

---

## Runbook validación E2E post-deploy a dev

1. Verificar que el deploy aplicó las migraciones de PR B (sin nuevas en PR D).
2. `curl /scalar/v1` → ver los 6 endpoints nuevos bajo "Integraciones AW".
3. POST con SP token de Glass Agent (KV secret `glass-agent-spn-client-secret`):
   - Validar 202 + id retornado.
   - Verificar en App Insights traces: `RegistrarCotizacionEdi` activity.
4. Verificar fila en `integraciones_aw.entidad_externa` + evento en
   `integraciones_aw.integration_events_outbox`.
5. Esperar 30-60s (PollingInterval del OutboxPublisherWorker) — el evento
   debería desaparecer del outbox (publicado a Service Bus).
6. Si SER-DATA tiene drop service running + HC up: AwDropWorker recoge
   el evento, hace HTTP POST → entidad pasa a `AwaitingCorrelation`.
7. Si A+W persiste el `quote_reference` en `pool_auftrag` con
   `auftragsnummer_kunde`, el `AwCorrelationWorker` lo encuentra en
   próximo ciclo (default 30s) y marca `Correlated`.
8. GET `/cotizaciones/{id}` con humano + permiso → debe mostrar la entidad
   en estado actual + correlación.

---

## Lo que NO se modificó

- ✗ `frontend/` (UI bandeja queda para PR de frontend posterior)
- ✗ `on-prem/` / drop service
- ✗ Otros módulos backend (Compras, DatosMaestros, Catalogos,
  Administracion, Almacen, Identidad)
- ✗ `infra/` (sin cambios Bicep — PR C ya configuró todo)
- ✗ Workers PR C (sin modificaciones)
- ✗ Migraciones EF Core (no hay schema changes en PR D)
- ✗ Workflows CI/CD

## Gaps conocidos para PRs futuros

- **UI bandeja en frontend** (PR siguiente — TanStack Router + tabla cotizaciones).
- **Tests E2E de estados intermedios** (Reintentar/MarcarResuelto desde
  FailedDrop/FailedCorrelation) — requiere helper de seed.
- **Rate limiting per SPN** (`<RateLimitingPerSpn>`).
- **Mass retry endpoint** (`<AdminMassRetry>`).
- **Export CSV** (`<CotizacionExportCsv>`).
- **Endpoints read-only A+W** (Pedidos, Clientes, Articulos, Inventario)
  — los 6 permisos están seedados desde PR A pero los endpoints son scope
  de PR posterior.
