# PR Breakdown — Submódulo Requisiciones (Compras)

> **Construido sobre:** [01-diseno.md](01-diseno.md) (Rev. 11) y
> [02-plan-implementacion.md](02-plan-implementacion.md) (Rev. 5).
>
> **Estado:** Rev. 2 — consolidado tras feedback de granularidad.
> **Fecha:** 2026-05-08.

---

## 0. Cómo leer

- Cada fila es **un PR**. ID `F<fase>-PR<n>` secuencial dentro de la fase.
- **Tamaños**: XS (≤ 200 líneas netas), S (200–500), M (500–800).
  Techo absoluto: 800. Por encima, partir.
- **Riesgo de romper main**: bajo / medio / alto. Cada PR debe dejar
  `main` verde y desplegable; el riesgo se refiere al *blast radius* si
  se cuela un bug a `main`.
- **Branch naming**: `compras/<fase>-<slug-corto>` (ej.
  `compras/fase2-autorizar-command`).
- **Dependencias**: PRs previos que deben estar mergeados.
- Al final de cada fase hay una nota de **paralelización**.

> Convención: PR mergeable = build verde + tests pasando + revisión de
> 1 dev + cobertura del slice (unit donde aplique, integration donde
> haya BD/HTTP). Las migraciones EF Core deben tener un
> `dotnet ef migrations script` revisado a mano.

---

## Fase 0 — Foundation (S) ✅ Cerrada

PRs mergeados a `main`; no se reabren. Histórico para trazabilidad.

| ID | Título | PR mergeado |
|---|---|---|
| F0-PR1 | Paquetes MediatR/FluentValidation/Mapster + wiring base | #33 |
| F0-PR2 | OpenAPI nativo + Scalar UI (ADR-0017) | #35 |
| F0-PR3 | Esqueleto del módulo `Millet.Compras` | #36 |
| F0-PR4 | `ComprasDbContext` vacío + migración inicial | #37 |
| F0-PR5 | Health check de migraciones + `Compras.UnitTests` | #38 |

---

## Fase 0.b — Permisos canónicos de Compras (XS)

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| F0b-PR1 | `compras/fase0b-permisos-canonicos` | Agregar las 10 constantes `compras.requisiciones.*` a `PermisosCanonicos.Todos` con GUIDs deterministas (namespace `00000003-...`). Migración EF Core de seed (auto-detectada por delta de `HasData`). Endpoint dummy de smoke con `[RequirePermission(...)]`. Tests negativos (403 sin permiso, 200 con permiso). | `backend/src/Identidad/Domain/PermisosCanonicos.cs`, `backend/src/Identidad/Infrastructure/Migrations/<ts>_ComprasPermisos.cs`, `backend/tests/Api.IntegrationTests/Compras/PermisosTests.cs` | F0-PR5 | XS | bajo (seed idempotente) | Migración aplica; los 10 permisos aparecen en `identidad.permisos`; smoke test verifica 403/200. |

**Paralelización Fase 0.b**: 1 PR. Paralelizable a F1-PR1.

---

## Fase 1 — Walking skeleton (S)

Consolida los 4 PRs de Rev. 1 en 2: dominio+tabla en uno, endpoints REST en otro.

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| F1-PR1 | `compras/fase1-agregado-requisicion-y-tabla` | **Dominio**: agregado raíz `Requisicion` con campos mínimos (`EmpresaId`, `Folio`, `SucursalId`, `DepartamentoId`, `Estado=Borrador`). VO `Folio` con regex de formato. Enum `EstadoRequisicion` (8 valores). Hereda `BaseEntity`, implementa `IPerteneceAEmpresa`, `IAuditable`, `IFiscalmenteRelevante`. **EF Core**: tabla `compras.requisiciones` con índices del §10.1, secuencia `compras.folio_secuencias`, snake_case, `version IsConcurrencyToken` heredado. Migración. **Tests**: unitarios del agregado (crear, invariantes). | `backend/src/Compras/Domain/{Requisicion,Folio,EstadoRequisicion}.cs`, `backend/src/Compras/Infrastructure/Configurations/RequisicionConfiguration.cs`, `backend/src/Compras/Infrastructure/Migrations/<ts>_RequisicionesTabla.cs`, `backend/tests/Compras.UnitTests/Domain/*` | F0-PR4 | M | medio (modelo + migración) | Migración aplica; `\d compras.requisiciones` muestra los CHECK y UNIQUE esperados; tabla vacía. Tests del agregado pasan. |
| F1-PR2 | `compras/fase1-crear-y-obtener-requisicion` | **Crear**: `CrearRequisicionCommand` + handler MediatR + validator FluentValidation. Folio atómico vía `nextval`. Endpoint `POST /api/v1/compras/requisiciones` con `[RequirePermission("compras.requisiciones.crear")]`. Mapping con Mapster. **Obtener**: `ObtenerRequisicionPorIdQuery` + handler + endpoint `GET /api/v1/compras/requisiciones/{id}`. Permiso `compras.requisiciones.leer`. DTO con `Version` expuesto en `ETag`. **Tests**: unitarios (handler+validator) + integration (HTTP→BD). | `backend/src/Compras/Application/CrearRequisicion/*`, `backend/src/Compras/Application/ObtenerRequisicionPorId/*`, `backend/src/Api/Endpoints/Compras/RequisicionesEndpoints.cs` | F0b-PR1, F1-PR1 | M | medio | Curl POST → 201 con folio formateado; GET → 200 con ETag; 404 ProblemDetails para id inexistente. |

**Paralelización Fase 1**: F1-PR1 y F0b-PR1 paralelos. F1-PR2 espera a ambos.

---

## Fase 2 — Líneas y workflow básico (M)

Consolida los 8 PRs de Rev. 1 en 4: líneas; comandos línea; transmitir+autorizar; rechazar+eliminar+bandejas.

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| F2-PR1 | `compras/fase2-linea-y-cubrimiento` | Entidad hija `LineaRequisicion`, VO `Cubrimiento` (inmutable) con CHECKs en BD. Tabla `compras.requisicion_lineas` con `ON DELETE CASCADE` a la cabecera. Invariantes: `Cantidad>0`, no modificar estructurales si hay cubrimiento. `Notas` editable siempre que no terminal. Tests unitarios del agregado. | `backend/src/Compras/Domain/LineaRequisicion.cs`, `backend/src/Compras/Domain/Cubrimiento.cs`, `backend/src/Compras/Infrastructure/Configurations/LineaRequisicionConfiguration.cs`, `backend/src/Compras/Infrastructure/Migrations/<ts>_LineasYCubrimiento.cs` | F1-PR2 | M | medio (modelo + migración) | Migración aplica; CHECK constraints rechazan cantidades inválidas; tests pasan. |
| F2-PR2 | `compras/fase2-comandos-linea` | **Estructural**: `AgregarLineaCommand`, `ActualizarLineaCommand`, `EliminarLineaCommand` + handlers + validators + endpoints REST (`POST/PATCH/DELETE .../lineas`). Solo permitidos en `Borrador`. **Notas**: `ActualizarNotasLineaCommand` + endpoint `PATCH .../lineas/{lineaId}/notas`, permitido en cualquier estado **no terminal**, sin cambio de estado. | `backend/src/Compras/Application/Lineas/**`, `backend/src/Api/Endpoints/Compras/LineasEndpoints.cs` | F2-PR1 | S | bajo | Tests integration: agregar/editar/eliminar en Borrador OK; en EnAutorizacion → 422; notas en `EnSurtido` retorna 200; en `Cerrada` → 422. |
| F2-PR3 | `compras/fase2-transmitir-y-autorizar-v0` | **Catálogo motivos rechazo**: tabla `compras.motivos_rechazo` con `permite_texto_libre`, `aplica_a` (bitmask), `activo`. Seed inicial (RECH-DUP, RECH-INSUF, RECH-INCOR, RECH-PROV, RECH-PRESUP, RECH-OTRO). Read query + endpoint GET. **Transmitir**: `EnviarAAutorizacionCommand` + handler. Transición `Borrador → EnAutorizacion`. Valida `Lineas.Count >= 1`. Emite `RequisicionEnviadaAAutorizacionEvent` (in-proc). Endpoint `POST .../{id}/transmitir`. **Autorizar v0**: entidad `Autorizacion` + tabla `compras.requisicion_autorizaciones`. `AutorizarRequisicionCommand` con motor v0 (solo monto vs umbral por departamento). Tabla `compras.umbrales_aprobacion_departamento` con seed. Endpoint `POST .../autorizaciones`. **Sin** invocar puertos (placeholder hasta Fase 4). | `backend/src/Compras/Domain/{MotivoRechazo,Autorizacion}.cs`, `backend/src/Compras/Application/{EnviarAAutorizacion,Autorizar,Motivos}/*`, `backend/src/Compras/Domain/Matriz/IRequiereNivelEvaluator.cs`, `backend/src/Compras/Infrastructure/Migrations/<ts>_MotivosTransmitirAutorizacionesUmbrales.cs` | F2-PR2 | M | medio (motor v0 + transición state machine) | Tests: 0 líneas → 422; N líneas → 200 + `EnAutorizacion`. Monto bajo umbral → solo N1 → `Autorizada`; monto alto → N1+N2; segundo N1 rechazado por UNIQUE. |
| F2-PR4 | `compras/fase2-rechazar-eliminar-bandejas` | **Rechazar/Eliminar**: `RechazarRequisicionCommand` (`motivoId` + `motivoTexto?`) y `EliminarRequisicionCommand`. Validators verifican que el motivo existe y, si `permite_texto_libre`, exigen texto. Endpoints `POST .../rechazar` y `DELETE .../{id}`. Estados terminales `Rechazada`/`Eliminada`. **Bandejas**: `ListarRequisicionesQuery` (filtros: estado, sucursal, depto, fecha, requisitante; paginación). `ListarPendientesAutorizacionQuery` (filtra `EnAutorizacion`). Endpoints `GET .../requisiciones` y `.../pendientes-autorizacion`. Read models planos vía proyección EF (no agregados). | `backend/src/Compras/Application/{Rechazar,Eliminar,Listar}/*`, `backend/src/Api/Endpoints/Compras/{RequisicionesEndpoints,BandejasEndpoints}.cs` | F2-PR3 | M | medio (queries con potencial de explotar índices) | Tests: rechazo desde EnAutorizacion → terminal; rechazo desde Borrador → 422; eliminación desde Autorizada → 422. Bandejas P95 < 200ms con dataset 1k RQs. |

> **PLATFORM-TODO(`<CollaborationHub>`)**: en F2-PR1 dejar comentario en
> `RequisicionConfiguration.cs` declarando `Requisicion` como entidad
> con soft lock cuando el hub esté disponible. Capa 1 (`Version`) ya
> protege.

**Paralelización Fase 2**: F2-PR1 → F2-PR2 → F2-PR3 → F2-PR4 secuencial (cada uno depende del anterior, no hay paralelizable).

---

## Fase 3 — Contratos y stubs cross-module (S)

Consolida los 4 PRs de Rev. 1 en 2: puertos completos; stubs+tests.

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| F3-PR1 | `compras/fase3-puertos-cross-module` | **Puertos Almacén**: `IConsultarStockPort`, `IReservarStockPort` (con `TimeSpan ttl`), `ILiberarReservaPort`, `IGenerarMovimientoSalidaPort`. DTOs `DisponibilidadStock`, `ReservaResultado`. **Puerto OC**: `IGenerarSolicitudCompraPort`. DTO `LineaSaldo`. Eventos in-proc `OcRecepcionRegistradaEvent`. Sin implementación; solo contratos. | `backend/src/Compras/Domain/Ports/Almacen/*`, `backend/src/Compras/Domain/Ports/OrdenCompra/*` | F2-PR4 | XS | bajo | Compila. Documentado como contrato in-proc (mismo BC). |
| F3-PR2 | `compras/fase3-stubs-y-tests` | **Stubs**: `InMemoryConsultarStockPort` (configurable: % disponibilidad por artículo desde `appsettings`), `InMemoryReservarStockPort` (devuelve `ReservaId` random), `InMemoryGenerarSolicitudCompraPort` (escribe a tabla provisional `compras.oc_borrador_stub`). DI con flag `Compras:UseStubs=true`. **Falla ruidosamente si flag activo en producción**. **Tests**: suite integration que ejercita los stubs sin invocarlos desde Autorizar (todavía v0). Baseline para Fase 4. **`NoOpIntegrationEventPublisher`** registrado con `PLATFORM-TODO(<Outbox>)`. | `backend/src/Compras/Infrastructure/Stubs/*`, `backend/src/Compras/Infrastructure/Migrations/<ts>_OcBorradorStub.cs`, `backend/src/Api/Program.cs`, `backend/tests/Compras.IntegrationTests/Stubs/*`, `backend/tests/Compras.IntegrationTests/Millet.Compras.IntegrationTests.csproj` | F3-PR1 | M | medio (stubs vivos en runtime) | Stubs responden con datos configurables. En `Production`, arranque falla con mensaje claro si flag está en true. Tests pasan con `Compras:UseStubs=true` en `appsettings.Test.json`. |

**Paralelización Fase 3**: secuencial.

---

## Fase 4 — Bifurcación stock-aware (M)

Consolida los 6 PRs de Rev. 1 en 4. Mantiene F4-PR2 (reservar+bifurcar) y F4-PR3 (cancelar) como PRs separados por su perfil de riesgo.

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| F4-PR1 | `compras/fase4-consultar-stock-y-persistir-cubrimiento` | **Consultar**: extender `AutorizarRequisicionCommand.Handler` para invocar `IConsultarStockPort` por línea cuando la matriz queda satisfecha. Calcular `Cubrimiento` propuesto. **Persistir**: persistir `Cubrimiento` en cada línea + transición a `Autorizada` (todo cubierto con stock, `CantidadDeCompra=0`) o `EnSurtido` (hay saldo). Sin invocar reservar/movimiento/oc. | `backend/src/Compras/Application/Autorizar/*`, `backend/src/Compras/Domain/Requisicion.cs` (método `RegistrarCubrimiento`) | F3-PR2 | M | medio (transición state machine + handler crítico) | Tests con stub: stock total → `CantidadDeAlmacen` → `Autorizada` → `Cerrada`; stock cero → `CantidadDeCompra` → `EnSurtido`; mixto → proporcional. |
| F4-PR2 | `compras/fase4-reservar-y-bifurcar` | Invocar `IReservarStockPort` (TTL 14d), `IGenerarMovimientoSalidaPort`, `IGenerarSolicitudCompraPort` en la **misma transacción de aplicación** (using transaction scope EF). Si alguno falla, rollback y retornar 422. **PR aislado por riesgo: transacción cross-port.** | `backend/src/Compras/Application/Autorizar/*` | F4-PR1 | M | **alto** (transacción que cruza múltiples puertos) | Tests con stub que simula fallo: el agregado queda en `EnAutorizacion` (no se persiste cubrimiento). Logs verificados. |
| F4-PR3 | `compras/fase4-cancelar-requisicion` | `CancelarRequisicionCommand` + handler. Invoca `ILiberarReservaPort` (idempotente) + emite `RequisicionCanceladaEvent` (que aborta OC borrador in-proc). Endpoint `POST .../{id}/cancelar`. Permitido desde `Autorizada` o `EnSurtido`. **PR aislado por riesgo: cross-port + state machine.** | `backend/src/Compras/Application/Cancelar/*`, `backend/src/Api/Endpoints/Compras/RequisicionesEndpoints.cs` | F4-PR2 | S | medio | Tests: cancelar desde Autorizada libera reserva (verificable en stub) + aborta OC borrador. Doble cancelación → 422. |
| F4-PR4 | `compras/fase4-tests-y-eventos-dominio` | **Tests escenarios**: suite parametrizada cubriendo stock total, parcial, cero, fallo de Almacén, fallo de OC, autorización con un nivel, con dos niveles. **Eventos**: registrar `MatrizAprobacionSatisfechaEvent`, `CubrimientoRegistradoEvent` como `INotification` con sus handlers in-proc (logging, telemetría). Ya emitidos por el agregado en PRs anteriores; este PR solo wirea handlers. | `backend/tests/Compras.IntegrationTests/Bifurcacion/*`, `backend/src/Compras/Application/Eventos/*` | F4-PR3 | M | bajo (tests + handlers in-proc sin side-effects nuevos) | Cobertura > 80% del handler de Autorizar. Logs muestran los eventos. |

**Paralelización Fase 4**: F4-PR1 → F4-PR2 → F4-PR3 secuencial (mismo handler). F4-PR4 al final.

---

## Fase 5 — Recepción y cierre (S)

Consolida los 3 PRs de Rev. 1 en 1: el flujo completo de recepción es cohesivo y de bajo riesgo.

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| F5-PR1 | `compras/fase5-recepcion-cierre-saldo` | **Registrar recepción**: `RegistrarRecepcionCommand` invocado por handler in-proc del evento `OcMaterialRecibidoEvent`. Actualiza `Cubrimiento.CantidadRecibida` por línea. **Cierre automático**: cuando todas las líneas tienen `CantidadPendiente==0`, transicionar a `Cerrada` y emitir `RequisicionCerradaEvent`. **Saldo no surtido**: emitir `SaldoNoSurtidoEvent` cuando se recibe el evento de OC cerrada con saldo no entregado (informativo, A12); no abre re-autorización. | `backend/src/Compras/Application/RegistrarRecepcion/*`, `backend/src/Compras/Domain/Requisicion.cs` (métodos `RegistrarRecepcion`, `Cerrar`), `backend/src/Compras/Domain/Events/*` | F4-PR4 | S | medio (mutación cross-handler + transición de cierre) | Tests: cubrimiento se actualiza; estado se mantiene en `EnSurtido` mientras quede pendiente; última recepción cierra; recepción extra rechazada; `SaldoNoSurtidoEvent` se publica en evento OC cerrada con saldo. |

**Paralelización Fase 5**: 1 PR.

---

## Fase 6 — Eventos integración + Notificaciones (S)

> **Nota**: PRs F6-PR1 y F6-PR2 son **infraestructura de plataforma**
> que vive en `SharedKernel/Compartido` y NO es código exclusivo de
> Compras. Se ejecutan aquí porque Compras es el primer consumidor real.
> Coordinar con responsable de plataforma. **NO se consolidan**: cada
> uno tiene blast radius alto y separar facilita rollback.

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| F6-PR1 | `compras/fase6-outbox-tabla-y-publisher` | Tabla `compras.integration_events_outbox` (índice parcial `published_at IS NULL`) + `IIntegrationEventPublisher` real + interceptor en `BaseDbContext` que recolecta `IntegrationEvent`s y los inserta en outbox antes del SaveChanges. | `backend/src/SharedKernel/Application/Integration/IIntegrationEventPublisher.cs`, `backend/src/SharedKernel/Infrastructure/Outbox/**`, `backend/src/Compras/Infrastructure/Migrations/<ts>_OutboxCompras.cs` | F5-PR1 | M | alto (toca BaseDbContext y atomicidad) | Tests integration: comando que dispara evento → tabla outbox tiene fila con `published_at IS NULL` en la misma transacción. |
| F6-PR2 | `compras/fase6-publisher-worker-servicebus` | `OutboxPublisherWorker` (`IHostedService`) con `SELECT FOR UPDATE SKIP LOCKED`, retry exponencial, dead-letter (`intentos > 10`). Wiring de Service Bus client (Azure SDK) con connection string de Key Vault. | `backend/src/SharedKernel/Infrastructure/Outbox/OutboxPublisherWorker.cs`, `backend/src/Api/Program.cs` | F6-PR1 | M | alto (worker en runtime + Service Bus real) | Worker corre en dev contra emulador o Service Bus real; eventos se publican; `published_at` se actualiza. |
| F6-PR3 | `compras/fase6-eventos-compras-publicados` | Wirear emisión de los 6 eventos de integración (autorizada, rechazada, eliminada, cancelada, cerrada, saldoNoSurtido). Mappers desde domain events a integration events versión v1. | `backend/src/Compras/Application/Integration/*`, `backend/src/Compras/Application/Eventos/*` | F6-PR2 | S | medio | Test E2E: comando → outbox → Service Bus emulador → consumer dummy recibe payload v1. |
| F6-PR4 | `compras/fase6-coordinacion-notificaciones` | Documento de contrato de eventos (en `docs/integraciones/compras-eventos.md`) con shape de cada evento v1. Reemplaza el `NoOpIntegrationEventPublisher` por el real (borrar PLATFORM-TODO `<Outbox>`). Actualizar tabla §8.6 del diseño. | `docs/integraciones/compras-eventos.md`, `backend/src/Api/Program.cs`, `docs/modulos/compras-requisiciones/01-diseno.md` | F6-PR3 | XS | bajo | Doc revisado por owner; tabla §8.6 sin la fila de `<Outbox>`. |

**Paralelización Fase 6**: secuencial.

---

## Fase 7 — Catálogos reales y migración (L) ✅ CERRADA

Consolida los 7 PRs de Rev. 1 en 4. Combinación más agresiva: legacy_map + script migración van juntos. **Importante**: F7-PR4 es el PR más riesgoso del proyecto entero — mergear solo con firma del owner y dry-run sobre dump de prueba.

> **Estado final (Rev. 14 del diseño, 2026-05-08)**: Fase 7 cierra
> funcionalmente con F7-PR1 + F7-PR3 (doc-only). F7-PR2 (importer
> SAP) queda diferido post-MVP hasta que llegue el export real;
> F7-PR4 (migración legacy) fue descartado por el owner — el portal
> viejo queda read-only.

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| F7-PR1 | `compras/fase7-tablas-proveedores-articulos` | **Proveedores y Articulos** en `compartido` (cross-module, Rev. 13 del diseño): clave UNIQUE, RFC, tipo persona, condiciones de pago, estatus, naturaleza, etc. Configuración EF inline en `CompartidoDbContext`. Migración. **Seed test data** (10 articulos + 5 proveedores) vía `IHostedService` que se autoexcluye en Production. Endpoints GET en `/api/v1/catalogos/...` con permiso nuevo `compartido.catalogos.leer`. Validación cross-table en `AgregarLinea`/`ActualizarLinea`/`CrearRequisicion` (existe + Activo). | `backend/src/SharedKernel/Domain/{Proveedor,Articulo,Naturaleza,TipoPersonaProveedor,EstatusCatalogo}.cs`, `backend/src/SharedKernel/Infrastructure/Persistence/CompartidoDbContext.cs`, `backend/src/SharedKernel/Infrastructure/Persistence/Migrations/Compartido/<ts>_ProveedoresArticulos.cs`, `backend/src/SharedKernel/Infrastructure/Seed/CatalogosTestSeedHostedService.cs`, `backend/src/Api/Endpoints/Catalogos/CatalogosEndpoints.cs` | F6-PR4 | M | bajo | Migraciones aplican; seed corre en non-Production; endpoints responden 200; validación cross-table funciona; tests cubren enums + casos felices/sad. |
| ~~F7-PR2~~ | **DEFERRED post-MVP** | ~~Importer SAP~~. Decisión del owner (2026-05-08): el cliente entrega el export real más adelante; mientras tanto F7-PR1 vive con seed test data. Cuando llegue el export, este PR ejecuta el importer + amplía columnas si SAP trae más campos vía migración aditiva. | — | F7-PR1 | M | — | — |
| F7-PR3 | `compras/fase7-cleanup-stubs` | **Reducido a doc-only** (MVP): actualizar §8.6 del diseño para reflejar que los stubs Almacén/OC siguen activos hasta que esos módulos reales existan. El cleanup real se ejecuta cuando los submódulos hermanos lleguen. | `docs/modulos/compras-requisiciones/01-diseno.md` | F7-PR1 | XS | bajo | §8.6 actualizada con plazos y estado real. |
| ~~F7-PR4~~ | **SKIPPED por owner** | ~~Migración legacy de RQs vivas~~. Decisión del cliente (2026-05-08): el portal viejo queda read-only para consulta histórica. Las RQs vivas no se migran al ERP nuevo. | — | F7-PR3 | L | — | — |

**Paralelización Fase 7**: secuencial. F7-PR3 cierra fase (post F7-PR1).

---

## Fase 8 — Hardening y polish (M)

Consolida los 6 PRs de Rev. 1 en 4. F8-PR1 (idempotency middleware) se mantiene aislado por su impacto en hot path de toda la API.

> **Nota**: F8-PR1 introduce idempotencia HTTP (ADR-0020) que vive en
> `SharedKernel`. Compras es el primer consumidor real.

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| F8-PR1 | `compras/fase8-idempotency-middleware` | Tabla `core.idempotency_keys` (PK `(empresa_id, usuario_id, key)`). Middleware `IdempotencyMiddleware` con flujo del ADR-0020 (validación UUID v4, hash SHA-256 del body, storage de respuesta cacheada ≤1MB, status processing/completed/failed). `[RequireIdempotencyKey]` marker. Job de limpieza `IdempotencyKeysCleanupJob`. **PR aislado: hot path de toda la API.** | `backend/src/SharedKernel/Application/Idempotency/*`, `backend/src/SharedKernel/Infrastructure/Idempotency/*`, `backend/src/Api/Web/IdempotencyMiddleware.cs`, `backend/src/SharedKernel/Infrastructure/Persistence/Migrations/Core/<ts>_IdempotencyKeys.cs` | F7-PR4 | M | **alto** (middleware en hot path) | Tests: replay con misma key + mismo body → respuesta cacheada; con body distinto → 422; processing concurrente → 409. |
| F8-PR2 | `compras/fase8-idempotency-y-observabilidad` | **Endpoints**: decorar todos los `POST` de mutación de Compras con `[RequireIdempotencyKey]` (`/transmitir`, `/autorizaciones`, `/rechazar`, `/cancelar`, `/lineas` POST). Documentar en OpenAPI. **Observabilidad**: custom dimensions en Serilog para `RequisicionId`, `Estado`, `EmpresaId`. `Activity` spans en handlers críticos (`Autorizar`, `Cancelar`, `RegistrarRecepcion`). Dashboards en Application Insights (definidos como JSON o link). | `backend/src/Api/Endpoints/Compras/*`, `backend/src/Compras/Application/**` (decoradores MediatR), `docs/operacion/dashboards-compras.md` | F8-PR1 | S | bajo | Curl POST sin header → 400 `MISSING_IDEMPOTENCY_KEY`. Traces aparecen en App Insights con `RequisicionId` searchable. |
| F8-PR3 | `compras/fase8-perf-y-openapi-descripciones` | **Perf bandeja**: seed de 10k RQs en script (`tools/perf/seed-10k-rqs.ps1`). Benchmark de queries de bandeja con BenchmarkDotNet o `wrk`. Si P95 > 200ms, agregar índices o vista materializada (back-out a A14 si necesario). **OpenAPI**: XML doc comments en cada endpoint, request, response. `[ProducesResponseType]` para todos los códigos esperados (200, 400, 404, 409, 422). Verificación de drift con `git diff` del JSON generado. | `tools/perf/**`, `backend/src/Compras/Infrastructure/Migrations/<ts>_IndicesBandeja.cs` (condicional), `backend/src/Api/Endpoints/Compras/**` | F8-PR2 | M | medio (índices afectan escritura) | Reporte adjunto al PR con P50/P95 antes/después. `/scalar/v1` muestra el módulo completo con descripciones. |
| F8-PR4 | `compras/fase8-runbook-uat` | Documentación de operación: qué hacer si el publisher se atora, cómo liberar reservas huérfanas, cómo investigar 409 frecuentes, cómo correr el spot-check post-migración. Plan de UAT con grupo piloto. | `docs/operacion/runbook-compras.md`, `docs/operacion/plan-uat-compras.md` | F8-PR3 | XS | bajo | Owner firma el runbook. |

**Paralelización Fase 8**: secuencial.

---

## Fase 9 — A1 multidimensional + RBAC final (M)

Consolida los 5 PRs de Rev. 1 en 3.

| ID | Título | Alcance | Archivos / módulos | Deps | Tamaño | Riesgo | Mergeable cuando |
|---|---|---|---|---|---|---|---|
| F9-PR1 | `compras/fase9-aprobadores-y-naturaleza-bulk` | **Aprobadores depto**: tabla `compras.aprobadores_departamento` con vigencia (PK compuesta `(empresa, departamento, rol, vigente_desde)`). Roles: `JefeDpto`, `JefeAlmacen`, `AutorizadorN2`. Seed inicial coordinado con cliente. **Naturaleza bulk**: confirmar que `naturaleza` está poblada (revisada en F7-PR2). Endpoint admin para reclasificar artículos en bulk (post-go-live, A1 §3.bis.1). Seed default `Estandar` para cualquier artículo sin clasificación. | `backend/src/Compras/Domain/AprobadorDepartamento.cs`, `backend/src/Compras/Application/Articulos/Reclasificar/*`, `backend/src/Compras/Infrastructure/Migrations/<ts>_AprobadoresDepto.cs` | F8-PR4 | M | medio | Migración aplica; seed del cliente sin colisión. Endpoint protegido por permiso admin; tests verifican reclasificación. |
| F9-PR2 | `compras/fase9-evaluator-multidimensional-wirear` | **Evaluator**: reemplazar el motor v0 por la implementación del pseudocódigo §3.bis.2: `IRequiereNivelEvaluator` con 3 capas (departamental + monto + naturaleza). `IResolverAutorizadorPort` resuelve N1/N2 según naturaleza y sucursal. **Wirear**: reemplazar el placeholder en `AutorizarRequisicionCommand.Handler` por el evaluator real. Borrar el v0. | `backend/src/Compras/Domain/Matriz/RequiereNivelEvaluator.cs`, `backend/src/Compras/Application/Matriz/ResolverAutorizadorService.cs`, `backend/src/Compras/Application/Autorizar/*` | F9-PR1 | M | **alto** (cambia la lógica de autorización en hot path) | Tests parametrizados con la matriz del cliente: cada combinación (naturaleza × monto × depto) produce nivel correcto. Tests del handler existentes siguen pasando + nuevos casos. |
| F9-PR3 | `compras/fase9-rbac-final-mapeo-legacy` | Mapeo final de los 26 derechos legacy a permisos `compras.requisiciones.*` y nuevos roles (`JefeAlmacen`, `JefeDepartamento`, `AutorizadorN2`). Seed de roles + asignaciones por defecto. Documento que tabula el mapping. | `backend/src/Identidad/Domain/PermisosCanonicos.cs` (si requiere nuevos), `backend/src/Identidad/Infrastructure/Migrations/<ts>_RolesCompras.cs`, `docs/modulos/compras-requisiciones/05-mapeo-rbac-legacy.md` | F9-PR2 | M | medio | Sesión con cliente firma el mapping. Tests de smoke verifican que cada rol nuevo tiene los permisos esperados. |

**Paralelización Fase 9**: secuencial.

---

## Resumen de PRs por fase

| Fase | PRs Rev. 2 | Sizing fase | Notas |
|---|---|---|---|
| 0 — Foundation | 5 ✅ | S | Cerrada (#33, #35, #36, #37, #38). |
| 0.b — Permisos canónicos | 1 | XS | Paralelizable a F1-PR1. |
| 1 — Walking skeleton | 2 | M | Dominio+tabla; endpoints REST. |
| 2 — Líneas y workflow básico | 4 | M | Líneas; comandos línea; transmitir+autorizar; rechazar+bandejas. |
| 3 — Contratos y stubs | 2 | M | Puertos completos; stubs+tests. |
| 4 — Bifurcación stock-aware | 4 | M | F4-PR2 (reservar+bifurcar) y F4-PR3 (cancelar) aislados por riesgo. |
| 5 — Recepción y cierre | 1 | S | Recepción + cierre + saldo. |
| 6 — Eventos integración | 4 | S | Sin consolidar (infraestructura crítica). |
| 7 — Catálogos + migración | 4 | L | F7-PR4 (legacy completo) es el más riesgoso del proyecto. |
| 8 — Hardening | 4 | M | F8-PR1 (idempotency middleware) aislado por hot path. |
| 9 — A1 + RBAC final | 3 | M | |
| **Total** | **34** | | **29 pendientes** tras Fase 0. |

---

## Notas de proceso

### Convenciones de PR

- **Branch**: `compras/<fase>-<slug-corto>`. Ejemplos:
  `compras/fase4-cancelar-requisicion`, `compras/fase2-rechazar-eliminar-bandejas`.
- **Título**: imperativo, conciso, ≤ 72 caracteres.
- **Body**: incluir referencia al PR ID (`F1-PR1`), fase, y deps que ya
  están en main. Si hay PLATFORM-TODO nuevos, listarlos.

### Reglas duras

- Cada PR deja `main` verde y desplegable. No se admite "este rompe pero
  el siguiente arregla".
- Migraciones EF Core: el script SQL generado por
  `dotnet ef migrations script` debe ir en el cuerpo del PR (o adjunto).
  Revisión manual obligatoria.
- Si un PR cruza 800 líneas netas, se parte. Excepción justificada
  documentada en el body.
- Sin `PLATFORM-TODO` huérfanos: cada uno debe estar en la tabla §8.6
  del diseño.

### Cuándo escalar

- **F4-PR2** (transacción cross-port): si hay duda sobre el modelo
  transaccional, escalar a equipo de plataforma antes de mergear.
- **F6-PR1/PR2** (Outbox + Service Bus): coordinar con responsable de
  plataforma. Si el ticket `<Outbox>` aún está abierto en plataforma,
  el wireup puede salir de Compras y vivir en SharedKernel.
- **F7-PR4** (migración de RQs vivas): no mergear sin firma del owner
  y dry-run sobre dump de prueba al menos una vez.
- **F8-PR1** (idempotency middleware): impacta hot path de toda la API.
  Code review por dos personas obligatorio.
- **F9-PR2** (evaluator multidimensional): si la matriz del
  cliente no está confirmada, no mergear; F9 entera puede deslizarse
  hacia el final.

---

## Cambios respecto a versiones previas

### Rev. 2 — consolidación post-feedback (2026-05-08)

Tras feedback del owner: el breakdown original (Rev. 1, 53 PRs) se
diseñó asumiendo equipo de 2 devs paralelos con review cruzado. El
contexto real es 1 dev solo que también revisa, así que la granularidad
máxima agregaba overhead de proceso (plan + verify + commit + push +
PR + cleanup) sin valor de revisión.

**Consolidación aplicada**: 53 → 34 PRs totales (29 pendientes tras
Fase 0). Reducción ~36%, principalmente combinando PRs XS afines
dentro de cada fase. PRs de alto riesgo (transacción cross-port,
middleware en hot path, idempotency, migración legacy) se mantienen
aislados.

Cambios concretos por fase:

| Fase | Rev. 1 | Rev. 2 | Consolidación |
|---|---|---|---|
| 1 | 4 | 2 | Dominio+tabla en uno; endpoints REST en otro. |
| 2 | 8 | 4 | Líneas; comandos línea (estructural+notas); motivos+transmitir+autorizar; rechazar+eliminar+bandejas. |
| 3 | 4 | 2 | Puertos completos en uno; stubs+tests en otro. |
| 4 | 6 | 4 | Consultar+persistir; reservar+bifurcar (aislado); cancelar (aislado); tests+eventos. |
| 5 | 3 | 1 | Recepción+cierre+saldo en uno. |
| 6 | 4 | 4 | Sin cambio (infraestructura crítica). |
| 7 | 7 | 4 | Catálogos juntos; import+fresh; cleanup; legacy completo. |
| 8 | 6 | 4 | Idempotency middleware aislado; endpoints+observabilidad; perf+OpenAPI; runbook+UAT. |
| 9 | 5 | 3 | Aprobadores+naturaleza; evaluator+wirear; RBAC. |

PRs de Fase 0 ya mergeados (#33, #35, #36, #37, #38) se documentan
como histórico — no cambian.

### Rev. 1 — versión inicial (2026-05-07)

Primer corte del breakdown. Calibrado contra el plan Rev. 5 y el audit
del repo. 53 PRs totales.
