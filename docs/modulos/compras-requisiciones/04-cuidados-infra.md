# Cuidados de infraestructura — Submódulo Requisiciones (Compras)

> **Construido sobre:** [01-diseno.md](01-diseno.md) (Rev. 11),
> [02-plan-implementacion.md](02-plan-implementacion.md) (Rev. 5),
> [03-pr-breakdown.md](03-pr-breakdown.md) (Rev. 1).
>
> **Estado:** propuesta para revisión con el equipo.
> **Fecha:** 2026-05-07.

---

## 0. Cómo leer

Cada cuidado tiene cuatro líneas:

- **Qué**: la regla en una frase.
- **Por qué importa**: una o dos frases que justifican el costo.
- **Cómo lo verificamos**: test, gate de CI, checklist de PR, o
  revisión manual concreta.
- **Qué pasa si lo ignoramos**: el modo de falla.

Los cuidados se agrupan por tema y se priorizan dentro de cada bloque
(P0 = bloqueante para release; P1 = importante; P2 = nice-to-have
trazable).

---

## 1. Migraciones EF Core

> Ref: ADR-0005, plan §2 audit, F1-PR2, F2-PR1, F2-PR4, F2-PR6,
> F4-PR4 (cancelar), F7-*.

### 1.1 [P0] Cada migración debe revisarse como SQL, no como C#

- **Qué**: antes de mergear cualquier PR con migración, ejecutar
  `dotnet ef migrations script <prev> <new> --project <proj>` y pegar
  el SQL en el cuerpo del PR.
- **Por qué importa**: EF Core a veces genera SQL sorprendente
  (drop+recreate de columnas con datos, índices implícitos, cambios de
  tipo destructivos). Revisar en C# no es suficiente.
- **Cómo lo verificamos**: checklist de PR. Reviewer rechaza el PR si
  el script no está adjunto. CI puede correr `--idempotent` y publicar
  el script como artifact.
- **Qué pasa si lo ignoramos**: pérdida de datos en producción al
  aplicar una migración que en C# parecía aditiva pero en SQL implicaba
  drop. Históricamente, el modo de falla más caro de un ORM.

### 1.2 [P0] Reversibilidad y orden seguro de pasos destructivos

- **Qué**: cualquier cambio destructivo (DROP COLUMN, NOT NULL en
  columna nueva, cambio de tipo) debe descomponerse en al menos dos
  migraciones en releases separados: (1) aditiva con default → (2)
  destructiva una vez que el código ya no usa la columna vieja.
- **Por qué importa**: durante un rollback, una migración que ya borró
  datos no se puede revertir. Y en producción puede haber otra réplica
  todavía leyendo la columna vieja.
- **Cómo lo verificamos**: revisión manual del PR; convención
  documentada aquí. Caso típico en Fase 9 (cambios de schema de
  autorización).
- **Qué pasa si lo ignoramos**: imposibilidad de rollback ante un
  incidente. Downtime forzado.

### 1.3 [P0] NOT NULL en columnas con datos: pasos atómicos con default

- **Qué**: agregar `NOT NULL` a una tabla no vacía requiere `ADD COLUMN
  ... DEFAULT 'X' NOT NULL` en una sola migración (o `ADD COLUMN`
  nullable + `UPDATE` + `ALTER ... NOT NULL` en migraciones
  consecutivas con verificación entre pasos).
- **Por qué importa**: PostgreSQL en versiones recientes optimiza el
  caso del default constante; fallar este caso bloquea la migración.
- **Cómo lo verificamos**: test de integración que carga el DB,
  inserta filas con datos del estado anterior, y aplica la migración.
  Para tablas grandes (>100k filas), benchmark de tiempo de aplicación.
- **Qué pasa si lo ignoramos**: migración que en dev pasa en 50ms y en
  prod toma 30 minutos con lock de tabla.

### 1.4 [P0] Esquemas separados — no FK física cross-schema

- **Qué**: dentro del esquema `compras` sí se permiten FK físicas
  (línea→cabecera). **Cross-schema** (a `identidad`, `almacen`,
  `contabilidad`, `compartido`) **solo Guid lógico**, sin FK física.
- **Por qué importa**: una FK física entre esquemas ata el release y
  las migraciones de dos módulos. Diseño §10.2.3 lo exige.
- **Cómo lo verificamos**: test que escanea el modelo EF de
  `ComprasDbContext` y falla si encuentra FK con `principal` en otra
  tabla cuyo `ToTable` apunta a otro esquema.
- **Qué pasa si lo ignoramos**: deploy de Compras bloqueado porque
  Identidad cambió una columna referenciada.

### 1.5 [P1] El módulo Compras NO escribe en otros esquemas

- **Qué**: `ComprasDbContext` solo gestiona el esquema `compras`. Para
  leer entidades de otros módulos (ej. `Empresa`, `Usuario`), se mapea
  con `ExcludeFromMigrations()` (mismo patrón que `IdentidadDbContext`
  hace con `Empresa`).
- **Por qué importa**: cada módulo es dueño de su esquema; CLAUDE.md
  lo exige. Cruzarse rompe el invariante.
- **Cómo lo verificamos**: revisión de PR. Buscar `ToTable("...", "X")`
  en `ComprasDbContext` donde `X != "compras"` debe ir acompañado de
  `ExcludeFromMigrations()`.
- **Qué pasa si lo ignoramos**: Compras intenta crear/modificar tablas
  de Identidad. Conflicto de migraciones, datos corrompidos.

### 1.6 [P1] Migraciones grandes (Fase 7) corren con transacción manual

- **Qué**: el script de migración de RQs vivas (F7-PR7) NO debe correr
  en una sola transacción gigante. Trabajar por lotes (1000 RQs por
  vez) con commits intermedios y tabla de progreso (`migracion.estado`)
  para reanudar tras una falla.
- **Por qué importa**: una transacción de horas eleva el riesgo de
  bloqueos largos, vacuum atascado, WAL crece sin liberar.
- **Cómo lo verificamos**: dry-run del script sobre dump de prueba con
  tamaño realista; medir uso de WAL y locks. Documentar en runbook.
- **Qué pasa si lo ignoramos**: ventana de mantenimiento que se
  alarga; rollback parcial difícil; cliente bajando producción más
  tiempo del comprometido.

---

## 2. Concurrencia optimista (ADR-0012)

> Ref: ADR-0012, diseño §3.bis.4, §10.1, F1-PR1, F2-PR6.

### 2.1 [P0] `Version` se incrementa SOLO vía `BaseDbContext`

- **Qué**: nunca asignar `Requisicion.Version` manualmente, nunca
  bypasear el `MetadataSaveChangesInterceptor`. EF Core es la única
  fuente de incremento.
- **Por qué importa**: el `IsConcurrencyToken` que aplica
  `BaseDbContext.OnModelCreating` solo funciona si el flujo pasa por
  el interceptor. Bulk operations (raw SQL, `ExecuteUpdate`) saltan el
  interceptor.
- **Cómo lo verificamos**: test parametrizado que toma una
  `Requisicion`, modifica un campo, llama `SaveChanges` dos veces
  desde dos `DbContext`s distintos con el mismo `Version` cargado, y
  verifica que el segundo lance `DbUpdateConcurrencyException`. Repetir
  por cada agregado: `Requisicion`, `LineaRequisicion`, `Autorizacion`.
- **Qué pasa si lo ignoramos**: lost updates silenciosos en
  producción. Imposibles de reproducir, peor que un bug ruidoso.

### 2.2 [P0] `DbUpdateConcurrencyException` traduce a HTTP 409

- **Qué**: `GlobalExceptionHandler` ya mapea `ConcurrencyException` a
  HTTP 409 con Problem Details. Compras debe lanzar
  `ConcurrencyException` (envoltorio de `DbUpdateConcurrencyException`)
  en el lugar correcto, no dejar que se escape como 500.
- **Por qué importa**: ADR-0012 fija 409 con cuerpo Problem Details
  para que el frontend pueda renderizar `<ConflictResolutionDialog />`.
  Un 500 bloquea esa UX. (Nota: el diseño §9 dice 412; ver "Hallazgos
  para revisión" — el repo y ADR mandan 409.)
- **Cómo lo verificamos**: test de integración HTTP: dos `PATCH`
  concurrentes con el mismo ETag → uno responde 200, el otro 409 con
  `application/problem+json` y `code: CONCURRENCY_CONFLICT`.
- **Qué pasa si lo ignoramos**: usuarios ven errores genéricos sin
  poder reintentar. Soporte recibe tickets que no deberían existir.

### 2.3 [P1] Tests de conflicto por agregado, no solo por entidad

- **Qué**: hay que probar concurrencia en operaciones que mutan dos
  entidades del mismo agregado en una transacción (ej. `Autorizar`
  agrega `Autorizacion` y modifica `Estado` de la `Requisicion`). El
  conflicto puede venir de cualquiera de las dos.
- **Por qué importa**: si dos autorizadores aprueban a la vez, ambos
  cargan `Version=5`, ambos agregan su `Autorizacion`, ambos intentan
  guardar con `Version=5` en `requisiciones`. Solo uno gana. El otro
  no puede simplemente reintentar — su autorización ya está creada.
- **Cómo lo verificamos**: test de integración con dos handlers
  concurrentes invocando `AutorizarRequisicionCommand`. Verificar que
  exactamente una de las dos autorizaciones queda persistida y la
  otra recibe 409.
- **Qué pasa si lo ignoramos**: autorizaciones duplicadas o estado
  inconsistente entre el agregado y sus hijos.

### 2.4 [P1] Frontend recibe `ETag` y envía `If-Match`

- **Qué**: cada `GET .../{id}` devuelve `ETag: "<version>"` derivado
  del campo `Version`. Cada `PATCH/POST` de mutación requiere
  `If-Match` matching.
- **Por qué importa**: sin `If-Match`, el servidor no puede detectar
  el conflicto antes del SaveChanges. Se confía en `IsConcurrencyToken`
  pero el cliente puede haber refrescado y tener un Version más nuevo
  que el suyo.
- **Cómo lo verificamos**: test de integración que verifica `ETag`
  presente en GET y que `PATCH` sin `If-Match` retorna 428
  (Precondition Required) o el equivalente que el equipo defina.
- **Qué pasa si lo ignoramos**: conflictos que se descubren tarde, UX
  pobre.

---

## 3. Outbox + Service Bus (ADR-0009)

> Ref: ADR-0009, diseño §7.2, §8.6, F6-PR1/PR2/PR3.

### 3.1 [P0] Atomicidad: outbox + entidades en la misma transacción

- **Qué**: `IIntegrationEventPublisher` agrega filas a la tabla
  `compras.integration_events_outbox` **antes** del `SaveChanges`,
  usando el mismo `DbContext` de la operación de dominio. Una sola
  transacción cubre ambos.
- **Por qué importa**: si el outbox y los datos no son atómicos, hay
  ventanas donde el dato cambió pero el evento no se publicó (o al
  revés). Inconsistencia entre módulos.
- **Cómo lo verificamos**: test de integración que kill el proceso
  entre el insert del outbox y el commit (o simulado vía falla
  inyectada): la BD debe quedar sin la fila del outbox y sin los
  cambios de dominio (todo o nada).
- **Qué pasa si lo ignoramos**: el bug clásico "facturación dice
  cobrado y cobranza no se enteró", aplicado a "Compras dice cancelado
  y Almacén no liberó la reserva".

### 3.2 [P0] Worker publisher: SELECT FOR UPDATE SKIP LOCKED

- **Qué**: `OutboxPublisherWorker` lee batches con `SELECT ... FOR
  UPDATE SKIP LOCKED LIMIT 100` ordenados por `created_at`. Cuando se
  escale a N réplicas, esto garantiza que dos workers no publican el
  mismo evento.
- **Por qué importa**: sin `SKIP LOCKED`, dos workers compiten por la
  misma fila y uno falla; o peor, ambos publican y se duplica.
- **Cómo lo verificamos**: test multi-instancia (lanzar dos workers en
  el test) y verificar que cada evento se publica exactamente una vez.
- **Qué pasa si lo ignoramos**: eventos duplicados en Service Bus.
  Consumers idempotentes los descartan, pero el log se llena de ruido
  y aumentan errores transitorios.

### 3.3 [P1] Retry exponencial + dead-letter en `intentos > 10`

- **Qué**: si la publicación a Service Bus falla, incrementar
  `intentos` y hacer backoff. A partir de 10 intentos, marcar el
  evento como tóxico y NO publicarlo automáticamente; alertar.
- **Por qué importa**: un evento corrupto que falla siempre detiene
  la cola si no hay dead-letter. Los demás eventos se atascan.
- **Cómo lo verificamos**: test que inyecta excepción permanente en
  el publisher → tras 10 intentos, fila queda `published_at IS NULL,
  intentos = 10` y la siguiente fila se publica normalmente.
- **Qué pasa si lo ignoramos**: cola de outbox crece sin parar. P95
  de publishing lag explota. Los consumers reciben eventos viejos
  cuando el hot evento ya no es vigente.

### 3.4 [P1] Recuperación tras crash: idempotencia del consumer

- **Qué**: si el publisher cae después de publicar a Service Bus
  pero antes de marcar `published_at`, al reiniciar republica el
  evento. El consumer debe tolerarlo vía `integration_events_processed`
  (ADR-0009).
- **Por qué importa**: at-least-once es inherente al modelo. El
  consumer debe ser idempotente.
- **Cómo lo verificamos**: en cada consumer (cuando existan
  Notificaciones, BI), test que entrega el mismo evento dos veces y
  verifica que el side-effect se aplica una sola vez.
- **Qué pasa si lo ignoramos**: notificaciones duplicadas al
  solicitante; doble suma en BI; etc.

### 3.5 [P2] Métricas de outbox como gates blandos

- **Qué**: emitir custom metrics `outbox.publishing_lag_seconds`,
  `outbox.pending_count`, `outbox.toxic_count`. Alerta P1 si toxic > 0.
- **Por qué importa**: detectar el problema antes que el cliente.
- **Cómo lo verificamos**: dashboard en App Insights + alertas
  configuradas (en F8-PR3).
- **Qué pasa si lo ignoramos**: sabes que algo se rompió cuando un
  consumer reporta. Tarde.

---

## 4. Idempotencia HTTP (ADR-0020)

> Ref: ADR-0020, F8-PR1, F8-PR2.

### 4.1 [P0] `[RequireIdempotencyKey]` en POST que crean recursos

- **Qué**: marcar con `[RequireIdempotencyKey]` todos los POST que:
  crean RQ (`POST .../requisiciones`), agregan línea, transmiten,
  registran autorización, rechazan, cancelan, registran recepción.
- **Por qué importa**: doble-click del usuario o retry en red flaky
  generan duplicados. En Compras, una RQ duplicada cuesta una OC
  duplicada y una compra duplicada al proveedor.
- **Cómo lo verificamos**: test de integración por endpoint: replay
  con misma `Idempotency-Key` y mismo body → mismo response, sin
  efecto duplicado en BD. Replay con body distinto → 422
  `IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_BODY`.
- **Qué pasa si lo ignoramos**: RQs duplicadas con el mismo folio
  intentado (rechazadas por UNIQUE pero ya hay basura), OCs
  duplicadas, autorizaciones dobles.

### 4.2 [P0] TTL del cache de respuesta y limpieza

- **Qué**: 24h para `completed`/`failed`, 1h para `processing`. Job
  `IdempotencyKeysCleanupJob` corre cada hora. Verificar que el job
  está registrado y corriendo en producción.
- **Por qué importa**: la tabla crece linealmente con el tráfico; sin
  limpieza, en meses está en GB.
- **Cómo lo verificamos**: job tiene health check; test que inserta N
  registros con `created_at` antiguo y verifica que el job los borra.
- **Qué pasa si lo ignoramos**: degradación de queries por tabla
  hinchada. Costo de storage.

### 4.3 [P1] Storage del header: PK `(empresa_id, usuario_id, key)`

- **Qué**: la PK incluye `empresa_id` y `usuario_id` para evitar
  colisiones de keys entre usuarios distintos (ADR-0020).
- **Por qué importa**: si solo usas `key`, un atacante puede generar
  un UUID v4 que coincida con uno legítimo y robar la respuesta.
- **Cómo lo verificamos**: revisión de PR del diseño de la tabla;
  test que verifica que la misma key con dos usuarios distintos genera
  dos filas y dos respuestas independientes.
- **Qué pasa si lo ignoramos**: vulnerabilidad de fuga de información.

### 4.4 [P1] Replay tests por cada endpoint idempotente

- **Qué**: por cada endpoint con `[RequireIdempotencyKey]`, un test
  que: (1) hace el request, (2) lo repite con la misma key, (3)
  verifica que el body de la 2ª respuesta coincide con la 1ª y el
  side-effect ocurrió una sola vez.
- **Por qué importa**: el middleware puede no estar wireado en un
  endpoint específico por error.
- **Cómo lo verificamos**: convención: cada endpoint en
  `Compras.IntegrationTests` tiene una variante `*_Idempotent`.
- **Qué pasa si lo ignoramos**: bug que solo se manifiesta cuando un
  endpoint específico recibe doble request.

---

## 5. Stubs y NoOp (ADR-0031)

> Ref: ADR-0031, diseño §8.6, F3-*, F6-PR4, F7-PR5.

### 5.1 [P0] Stubs NUNCA son default en producción

- **Qué**: el flag `Compras:UseStubs` debe ser `false` por default.
  En `Production`, si el flag está en `true`, la app **debe fallar al
  arrancar** con un mensaje claro. Igual para `IIntegrationEventPublisher`
  registrado como `NoOp`.
- **Por qué importa**: un stub silencioso en producción es lo peor
  posible — operaciones aparentemente exitosas que en realidad no
  hicieron nada.
- **Cómo lo verificamos**: test de arranque que carga la
  configuración `Production` con `UseStubs=true` y verifica que
  `WebApplication.CreateBuilder` lanza una `InvalidOperationException`
  con mensaje específico.
- **Qué pasa si lo ignoramos**: RQs que parecen autorizadas pero
  Almacén nunca reservó. Cliente entera el día siguiente cuando llega
  un cliente sin material reservado.

### 5.2 [P0] `PLATFORM-TODO(<id>)` en cada NoOp

- **Qué**: cada implementación temporal lleva el comentario
  `PLATFORM-TODO(<identificador>): ...` siguiendo ADR-0031. Para Compras
  v1: `<CollaborationHub>` y `<Outbox>` mientras esos tickets estén
  abiertos; `<Almacen>` y `<OcBorrador>` si los stubs siguen vivos al
  llegar a Fase 7.
- **Por qué importa**: `rg "PLATFORM-TODO" backend/src` debe listar
  todo el debt. Sin el comentario, queda invisible.
- **Cómo lo verificamos**: revisión de PR; mensualmente, `rg
  "PLATFORM-TODO" backend/src --no-heading | sort | uniq -c` cruzado
  contra los tickets de plataforma vivos.
- **Qué pasa si lo ignoramos**: el debt se vuelve invisible. Cuando
  el ticket de plataforma cierra, nadie wirea Compras y se cree que
  está conectado.

### 5.3 [P1] Tabla §8.6 del diseño se mantiene actualizada

- **Qué**: cada vez que se agrega/quita un `PLATFORM-TODO` en código,
  se actualiza la tabla "Dependencias de plataforma pendientes" del
  diseño en el mismo PR.
- **Por qué importa**: la tabla es la fuente que se lee en revisión.
  Si está desactualizada, las decisiones se toman con info falsa.
- **Cómo lo verificamos**: checklist de PR. Reviewer pregunta
  explícitamente si hubo cambio en `NoOp`s.
- **Qué pasa si lo ignoramos**: el doc miente sobre el estado real.

---

## 6. Auditoría (ADR-0008)

> Ref: ADR-0008, diseño §10.1, `AuditSaveChangesInterceptor`.

### 6.1 [P0] No bypass de interceptors con SQL crudo o bulk

- **Qué**: prohibido `ExecuteUpdate`, `ExecuteDelete`, `ExecuteSqlRaw`
  con `INSERT/UPDATE/DELETE` sobre entidades `IAuditable`. El script
  de migración de RQs vivas (F7-PR7) usa la API de `DbContext` o
  marca explícitamente la inserción como `BulkAuditMode = es_bulk
  true` y registra una entrada agregada en `audit_log`.
- **Por qué importa**: SQL crudo y `ExecuteUpdate` saltan el
  `AuditSaveChangesInterceptor`. Cambios sin auditar = SAT y control
  interno enojados.
- **Cómo lo verificamos**: gate de CI: `BannedSymbols` agrega
  `M:Microsoft.EntityFrameworkCore.RelationalQueryableExtensions.ExecuteUpdate`
  y similares con la misma severidad que `DateTime.UtcNow`. Excepción
  documentada solo para el script de migración.
- **Qué pasa si lo ignoramos**: hueco de auditoría. En la primera
  revisión SAT, no hay forma de demostrar quién canceló qué.

### 6.2 [P0] `Requisicion`, `LineaRequisicion`, `Autorizacion` declaran `IAuditable`

- **Qué**: las tres entidades del agregado deben implementar
  `IAuditable`. `MotivoRechazo` (catálogo) también, aunque cambie
  raro. `compras.integration_events_outbox` se marca `INotAudited`.
- **Por qué importa**: si una entidad no declara `IAuditable`, el
  interceptor la ignora silenciosamente. ADR-0008 §"opt-in con
  enforcement" lo exige.
- **Cómo lo verificamos**: test que escanea el modelo EF de
  `ComprasDbContext` y falla si encuentra una `BaseEntity` que no
  implementa ni `IAuditable` ni `INotAudited`.
- **Qué pasa si lo ignoramos**: cambios sin pista en producción.
  Indemostrable.

### 6.3 [P1] `audit_log` en `core` particionado mensual

- **Qué**: `BaseDbContext` ya configura el mapeo a `core.audit_log`.
  La PK compuesta `(id, timestamp)` y el particionado mensual son
  responsabilidad del módulo `Core`, no de Compras. Compras solo
  consume.
- **Por qué importa**: si `Core` no provisiona la partición del mes
  siguiente, los inserts fallan a fin de mes.
- **Cómo lo verificamos**: health check de `Core` que verifica
  particiones futuras existen. (Fuera de scope de Compras pero
  bloquea Compras si falla.)
- **Qué pasa si lo ignoramos**: `INSERT INTO core.audit_log` falla;
  como el interceptor escribe en la misma transacción, falla todo
  el comando.

---

## 7. Soft delete y query filters (ADR-0008, ADR-0011)

> Ref: ADR-0008, ADR-0011, `BaseDbContext.ApplyQueryFilters`.

### 7.1 [P0] `IFiscalmenteRelevante` aplicado a `Requisicion`

- **Qué**: `Requisicion` implementa `IFiscalmenteRelevante` →
  `DeletedAt` se setea al "eliminar" y el query filter global
  (`e.DeletedAt == null`) excluye las eliminadas. La eliminación
  física está prohibida.
- **Por qué importa**: una RQ eliminada puede ser evidencia de
  control interno; no se borra.
- **Cómo lo verificamos**: test que: (1) crea RQ, (2) ejecuta
  `EliminarRequisicionCommand`, (3) verifica que `GET .../{id}` retorna
  404 con filtro normal pero la fila sigue en BD con `deleted_at IS
  NOT NULL`. Gate de CI: ningún `RemoveRange` o `DELETE FROM` sobre
  `Requisicion`.
- **Qué pasa si lo ignoramos**: RQs físicamente borradas;
  imposibilidad de auditar.

### 7.2 [P1] Queries de auditoría que ven eliminadas

- **Qué**: para reportes históricos / auditoría / migración, hay
  queries que necesitan ver RQs eliminadas. Estas queries usan
  `IgnoreQueryFilters()` y están explícitamente en un proyecto/módulo
  separado (admin/auditoría) o detrás de un permiso
  `compras.requisiciones.leer-eliminadas`.
- **Por qué importa**: si todo el código usa `IgnoreQueryFilters`
  "por si acaso", el filter pierde sentido.
- **Cómo lo verificamos**: gate de CI: `rg "IgnoreQueryFilters"
  backend/src/Compras` debe estar acotado a archivos en
  `Compras/Application/Auditoria/` o `Compras/Application/Migracion/`.
  Cualquier otra ocurrencia falla el build (gateado por convención).
- **Qué pasa si lo ignoramos**: leak de eliminadas a usuarios sin
  permiso; auditoría no puede confiar en lo que ve.

### 7.3 [P1] Multi-empresa filter no se evade

- **Qué**: `IPerteneceAEmpresa` aplicado a `Requisicion`. El query
  filter es `bypass || EmpresaId == current`. Solo el módulo
  `Identidad` (admin cross-empresa) puede activar `bypass`.
- **Por qué importa**: leak cross-empresa es violación de tenancy
  serio.
- **Cómo lo verificamos**: test que setea contexto de empresa A,
  intenta `GET` de RQ creada en empresa B, debe retornar 404.
- **Qué pasa si lo ignoramos**: usuario de la empresa A ve datos de
  la empresa B.

### 7.4 [Pendiente] Segmentación por sucursal (ADR-0051) — aún no implementada aquí

- **Qué**: hoy `Requisicion` solo aplica el filtro de §7.3
  (`EmpresaId`, hoy trivial con una sola empresa). No pasa por el
  patrón `SucursalScopeGuard` que sí protege Departamentos/Puestos/
  Usuarios de sucursal en Administración (F1-ADM-01). Un usuario
  operativo de la sucursal CDMX puede hoy ver/operar RQs de Mérida.
- **Por qué importa**: el eje real de aislamiento de negocio en
  Millet es la sucursal (ver [ADR-0051](../../decisiones/0051-segmentacion-de-datos-por-sucursal.md) y
  CLAUDE.md), no la empresa. Este es el gap de negocio real, no el
  de §7.3.
- **Qué falta**: cuando se priorice, declarar el permiso de bypass
  `compras.requisiciones.gestionar-todas-sucursales` (o el nombre que
  corresponda) y llamar a `SucursalScopeGuard.VerificarAsync` en los
  handlers de listado/consulta de RQ, reusando `IUsuarioSucursalReadPort`
  — mismo mecanismo ya construido, no uno nuevo.

---

## 8. Permisos (ADR-0007)

> Ref: ADR-0007, diseño §8.3, F0b-PR1, F9-PR5.

### 8.1 [P0] No filtrar existencia vía 404 vs 403

- **Qué**: si un usuario sin permiso `compras.requisiciones.leer`
  pide `GET .../{id}`, retornar **403** sin importar si el id existe.
  Si el usuario tiene permiso pero el id no existe, retornar 404.
  Para usuarios cross-empresa, retornar 404 (no 403) para no revelar
  existencia.
- **Por qué importa**: con un endpoint que devuelve 404 para
  inexistente y 403 para existente sin permiso, un atacante sin
  permiso puede enumerar IDs.
- **Cómo lo verificamos**: tests negativos por endpoint:
  (a) usuario sin permiso, id existente → 403;
  (b) usuario sin permiso, id inexistente → 403;
  (c) usuario con permiso, id de otra empresa → 404;
  (d) usuario con permiso, id inexistente → 404.
- **Qué pasa si lo ignoramos**: enumeración de IDs por usuarios
  hostiles. Vulnerabilidad reportable.

### 8.2 [P0] Tests negativos de permisos por endpoint

- **Qué**: cada endpoint que requiere permiso tiene un test de
  integración que verifica 403 sin el permiso (token con rol que no
  lo incluye).
- **Por qué importa**: olvidar el `[RequirePermission]` en un endpoint
  nuevo es un bug silencioso (HTTP 200 pasa los tests positivos).
- **Cómo lo verificamos**: convención de naming
  `*_Returns403_When_PermissionMissing` en `Compras.IntegrationTests`.
  Idealmente, un test parametrizado que recorre el manifiesto de
  endpoints.
- **Qué pasa si lo ignoramos**: privilege escalation.

### 8.3 [P1] Cache de permisos: invalidación al cambiar roles

- **Qué**: el cache de permisos `(userId, empresaId)` con TTL 5
  minutos (ADR-0007) se invalida explícitamente cuando un admin
  modifica roles del usuario. Compras no toca ese flujo (vive en
  Identidad), pero tests E2E deben asumirlo.
- **Por qué importa**: si quitan un permiso a un autorizador y
  sigue pudiendo aprobar 5 minutos, hay tiempo para abusar.
- **Cómo lo verificamos**: test E2E (en Identidad, no Compras): quitar
  permiso → token sigue válido → siguiente request → 403. Compras
  asume el contrato y no necesita test propio.
- **Qué pasa si lo ignoramos**: ventana de 5 min para abuso de
  permisos revocados.

---

## 9. Folio: secuencia atómica PostgreSQL

> Ref: diseño §4.5, §10.1 (`compras.folio_secuencias`), F1-PR2.

### 9.1 [P0] `nextval` en transacción del comando

- **Qué**: la generación del folio usa `nextval('seq_folio_<sucursal>_<anio>')`
  o equivalente vía la tabla `folio_secuencias` con `UPDATE ... RETURNING
  siguiente`. Se hace dentro de la transacción del comando para que un
  rollback no consuma folio.
- **Por qué importa**: dos `Crear` concurrentes deben recibir folios
  consecutivos sin colisión. PostgreSQL secuencias garantizan eso.
- **Cómo lo verificamos**: test concurrente: 100 `CrearRequisicionCommand`
  en paralelo (`Parallel.ForEach` con `WaitAll`) → 100 folios distintos
  consecutivos sin saltos (o con saltos solo si hubo rollback).
- **Qué pasa si lo ignoramos**: dos RQs con el mismo folio. Cliente
  reporta "veo el folio MID2026-000042 dos veces".

### 9.2 [P1] Folio único por (empresa, año, sucursal)

- **Qué**: la columna `folio` en `compras.requisiciones` tiene
  `UNIQUE (empresa_id, folio_anio, folio)`. Si un import legacy
  intenta colisión, falla ruidosamente.
- **Por qué importa**: doble protección además de la secuencia.
- **Cómo lo verificamos**: revisión del DDL en F1-PR2 + test que
  inserta un duplicado intencional y verifica `DbUpdateException`.
- **Qué pasa si lo ignoramos**: silenciosamente la lógica de
  generación se rompe sin que la BD se entere.

---

## 10. IClock, UTC en BD, TZ en presentación (ADR-0013)

> Ref: ADR-0013, `BannedSymbols.txt`, `IClock`, `SystemClock`.

### 10.1 [P0] Banned symbols ya gateados

- **Qué**: `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now`
  están baneados a nivel build vía `BannedSymbols.txt`. Compras debe
  usar `IClock.UtcNow` siempre.
- **Por qué importa**: tests determinísticos, tiempo único en todos
  los handlers.
- **Cómo lo verificamos**: ya hay gate de CI. Reviewer verifica que
  no hay supresiones (`#pragma warning disable`).
- **Qué pasa si lo ignoramos**: tests flaky por reloj real, errores
  de TZ por usar `Now` local.

### 10.2 [P1] Conversión a TZ del usuario solo en presentación

- **Qué**: BD almacena `timestamptz` (UTC). DTOs de respuesta
  exponen `DateTimeOffset` UTC. La conversión a la zona del usuario
  vive en el frontend (con `dayjs` o `Intl.DateTimeFormat`).
- **Por qué importa**: una conversión inadvertida en backend produce
  un dato de fecha con la TZ del servidor (Azure → UTC, server
  Europeo → CET). Inconsistente.
- **Cómo lo verificamos**: revisión de PR del shape de DTOs;
  prohibido `DateTime` desnudo (sin offset) en DTOs.
- **Qué pasa si lo ignoramos**: bug clásico "creé la RQ a las 23:00
  pero el sistema dice 17:00".

---

## 11. Money y `decimal`

> Ref: ADR-0014, `SharedKernel/Domain/Money.cs`, diseño §10.4.

### 11.1 [P0] `Money` cruza la capa de dominio, NO `decimal`

- **Qué**: el value object `Money` (cantidad + `Moneda`) es el que
  viaja por dominio y aplicación. `decimal` desnudo solo en DB layer
  (mapeo EF) y en DTOs de API (con la moneda como campo separado).
- **Por qué importa**: un `decimal` sin moneda es ambiguo. Sumar
  USD + MXN sin darse cuenta es un bug financiero.
- **Cómo lo verificamos**: gate de CI: `BannedSymbols` agrega
  parámetros `decimal precio` en handlers (regla custom o convención
  enforced en review). En lugar, recibir `Money`.
- **Qué pasa si lo ignoramos**: agregaciones cruzadas de monedas en
  reportes, totales mal sumados.

### 11.2 [P1] `decimal(18,5)` para cantidades, `decimal(18,6)` para precios

- **Qué**: el schema fija precisiones del diseño §10.4. EF Core con
  `HasPrecision(18, 5)` o `HasPrecision(18, 6)`.
- **Por qué importa**: precisión insuficiente trunca centavos en
  totales grandes.
- **Cómo lo verificamos**: revisión del DDL al mergear F2-PR1 y F1-PR2.
- **Qué pasa si lo ignoramos**: trunca al 4º decimal por default
  de PostgreSQL `numeric`. Errores de redondeo.

---

## 12. Cross-module ports: dónde vive cada cosa

> Ref: diseño §8.1, §8.2, F3-PR1/PR2, F7-PR5.

### 12.1 [P0] Las interfaces viven en `Compras.Domain.Ports`

- **Qué**: `IConsultarStockPort`, `IReservarStockPort`,
  `ILiberarReservaPort`, `IGenerarMovimientoSalidaPort`,
  `IGenerarSolicitudCompraPort` son **propiedad de Compras** y viven
  en `Compras.Domain.Ports.{Almacen|OrdenCompra}`. Almacén / OC las
  implementan, no las definen.
- **Por qué importa**: en hexagonal, el dominio expone los puertos.
  Cambiar el shape de un puerto es decisión del dueño (Compras), no
  del implementador.
- **Cómo lo verificamos**: revisión de PR. Si Almacén propone cambiar
  un puerto, lo hace vía PR a Compras.
- **Qué pasa si lo ignoramos**: arquitectura inversa: Compras
  acoplado a la implementación de Almacén.

### 12.2 [P0] Stubs viven en `Compras.Infrastructure.Stubs`

- **Qué**: `InMemory*Port` son código de Compras, no de Almacén.
  Cuando Almacén llegue, sus implementaciones reales viven en
  `Almacen.Infrastructure.AdaptersDeCompras`. Compras solo registra
  el puerto en DI; no le importa quién lo implementa.
- **Por qué importa**: un stub que vive en Almacén implica que Compras
  depende del módulo Almacén (incluso si solo es para tests). Romper
  esa dependencia desde el día 1 evita refactors costosos.
- **Cómo lo verificamos**: revisión del proyecto en F3-PR3.
- **Qué pasa si lo ignoramos**: Compras no compila sin Almacén.

### 12.3 [P1] Reemplazo de stubs requiere PR de Compras

- **Qué**: cuando Almacén entrega su adapter real, Compras hace un
  PR que (1) registra el adapter real en DI con flag, (2) borra el
  stub de Compras, (3) actualiza la tabla §8.6, (4) borra el
  `PLATFORM-TODO`.
- **Por qué importa**: el dueño de los puertos coordina el cutover.
- **Cómo lo verificamos**: F7-PR5 explícitamente.
- **Qué pasa si lo ignoramos**: stub queda en producción.

---

## 13. State machine: transiciones inválidas son `Result.Failure`

> Ref: diseño §5.2, §6.1, F1-PR1, F2-PR5/PR6/PR7, F4-PR4.

### 13.1 [P0] Transiciones inválidas devuelven `Result.Failure` (o `BusinessRuleException`)

- **Qué**: intentar `Eliminar` desde `Cerrada`, o `Autorizar` desde
  `Borrador`, no debe lanzar excepción genérica ni `InvalidOperationException`.
  El agregado lanza `BusinessRuleException` (ya disponible en
  `SharedKernel`) con código que el `GlobalExceptionHandler` traduce a
  HTTP 422. Si se adopta `Result<T>`, la decisión se documenta y se
  aplica uniformemente (ver Hallazgos).
- **Por qué importa**: 422 con `code` específico permite al frontend
  mostrar UX clara ("esta RQ ya está cerrada"). 500 es ruido.
- **Cómo lo verificamos**: test parametrizado que recorre cada
  arista del diagrama §5.2: por cada estado origen × comando,
  verifica si la transición es válida; si no, espera 422 con `code`
  esperado.
- **Qué pasa si lo ignoramos**: errores 500 al usuario por
  transiciones que el dominio no permite. Soporte recibe tickets que
  son la propia lógica.

### 13.2 [P0] Cobertura por arista del diagrama

- **Qué**: el test parametrizado del 13.1 cubre las **8 transiciones
  válidas** del §5.2 + las inválidas que se prueban explícitamente.
  Idealmente, un `[Theory]` con datos generados desde la matriz.
- **Por qué importa**: una arista olvidada se descubre en producción.
- **Cómo lo verificamos**: contar las aristas del diagrama (8 válidas
  + N inválidas seleccionadas) y asegurar que el test las recorre
  todas.
- **Qué pasa si lo ignoramos**: bug del tipo "RQ atascada en estado X
  porque el Y nunca se implementó".

### 13.3 [P1] Solo el agregado decide transiciones

- **Qué**: ningún handler debe asignar `Estado` directamente.
  Llamar a métodos del agregado (`EnviarAAutorizacion`, `Autorizar`,
  `Cancelar`, etc.) que validan internamente.
- **Por qué importa**: encapsulación. Si un handler cambia `Estado`
  saltándose el método, las invariantes se rompen.
- **Cómo lo verificamos**: revisión de PR. `Requisicion.Estado` debe
  tener setter `private` (o `protected internal` solo para EF). Tests
  unitarios verifican que cada método del agregado valida la
  transición.
- **Qué pasa si lo ignoramos**: estado inválido en BD. Imposibles de
  reproducir en dev.

---

## 14. Otros

### 14.1 [P1] Versionado de eventos de integración

- **Qué**: cada evento publicado al Service Bus es `compras.requisicion.<accion>.v1`.
  Cuando cambie el shape, **NO** modificar v1; agregar v2 y mantener
  ambos durante un periodo.
- **Por qué importa**: consumers desplegados con el v1 esperando ese
  shape. Cambiarlo silenciosamente rompe a Notificaciones / BI.
- **Cómo lo verificamos**: convención de naming explícita; revisión
  de PR.
- **Qué pasa si lo ignoramos**: deploys de Compras rompen consumers.

### 14.2 [P1] Liberación de reservas en el camino terminal

- **Qué**: `Cancelar`, `Eliminar` (post-autorización si aplica),
  `Rechazar` (si llegó a haber reserva, aunque por flujo no debería)
  invocan `ILiberarReservaPort` antes del estado terminal. Idempotente.
- **Por qué importa**: una reserva huérfana bloquea stock para otros.
- **Cómo lo verificamos**: tests por cada path terminal con stub
  contando llamadas a `LiberarAsync`.
- **Qué pasa si lo ignoramos**: stock fantasmal "reservado" para una
  RQ cancelada. Operaciones humanas para limpiar.

### 14.3 [P1] Cobertura de integración en CI antes de merge a main

- **Qué**: el pipeline `validate-app.yml` actualmente solo corre
  unit tests (filter `~UnitTests`). Antes de Fase 5, configurar
  Testcontainers en CI y correr `Compras.IntegrationTests` también.
- **Por qué importa**: bugs de wiring (DI, EF config, migraciones) no
  se detectan con unit tests.
- **Cómo lo verificamos**: PR a `validate-app.yml` cuando exista la
  primera suite de integración.
- **Qué pasa si lo ignoramos**: bugs que llegan a `main` y bloquean
  al equipo.

---

## Hallazgos para revisión

Discrepancias detectadas entre diseño, plan y estado real del repo:

1. **Conflicto de versión: 412 vs 409.**
   El diseño §9 dice "Conflicto de versión retorna 412 Precondition
   Failed". El ADR-0012 §"Manejo de conflictos en UI" y el repo
   (`GlobalExceptionHandler.cs:72`) usan **409 Conflict**. ADR manda;
   el diseño debe alinearse al ADR. Sugerencia: corregir §9 del
   diseño en una Rev. 12 menor.

2. **`Result<T>` vs excepciones.**
   El diseño §6.1 menciona `Result<TResponse>` (Either-style). El
   repo (`SharedKernel/Application/Exceptions/*`) usa el patrón de
   excepciones (`BusinessRuleException`, `ValidationException`,
   `EntityNotFoundException`, `ConcurrencyException`) traducidas en
   `GlobalExceptionHandler`. **Acción**: confirmar con el equipo cuál
   es la convención. Recomendación: seguir lo que ya funciona
   (excepciones tipadas) y borrar la mención a `Result<T>` del
   diseño. Si se prefiere `Result`, es trabajo de plataforma de
   Fase 0.

3. **Paquetes ausentes de `Directory.Packages.props`.**
   El plan §2 marca MediatR / FluentValidation / Mapster como
   "**[Verificar]**". Verificado: **no están**. El audit asumía que
   sí. F0-PR1 los introduce. Sin esto, Fase 1 no compila.

4. **OpenAPI ausente.**
   El plan §2 marca "**[Verificar]**". Verificado: el repo no tiene
   `Microsoft.AspNetCore.OpenApi` ni Scalar todavía. El ADR-0017 lo
   exige. F0-PR2 lo introduce.

5. **CI corre solo unit tests.**
   `.github/workflows/validate-app.yml` filtra
   `FullyQualifiedName~UnitTests`, excluyendo `Api.IntegrationTests`.
   Cualquier integration test que se escriba no corre en CI hasta
   que se configure Testcontainers (mencionado como TODO en el
   workflow). **Acción**: agendar PR de plataforma para habilitar
   docker-in-docker en el runner y eliminar el filtro. Bloquea el
   valor de los integration tests planeados desde Fase 1.

6. **Path de los docs.**
   El brief habla de `docs/compras/requisiciones/...`, pero la
   estructura real del repo es `docs/modulos/compras-requisiciones/...`
   (consistente con el ADR-0031 y el plan). Estos dos documentos
   se crearon en la ruta correcta del repo.

7. **`compras.requisiciones.leer` no aparece en la lista canónica del diseño.**
   El diseño §8.3 lista 10 permisos pero **omite el de leer**. F1-PR4
   necesita un permiso para `GET .../{id}`. Propuesta: agregar
   `compras.requisiciones.leer` a Fase 0.b. **[Pendiente confirmar]
   con el equipo de Identidad**.

8. **GUIDs deterministas para los permisos de Compras.**
   El namespace `00000002-...` se usa para Identidad (visible en
   `PermisosCanonicos.cs`). Para Compras, el patrón natural sería
   `00000003-...` o un namespace dedicado. **[Pendiente confirmar]**
   convención exacta antes de F0b-PR1.

9. **`Empresa` mapeada cross-schema con `ExcludeFromMigrations`.**
   `IdentidadDbContext` mapea `Empresa` con `ExcludeFromMigrations()`
   para que las FK cross-schema resuelvan sin que Identidad gestione
   la migración (vive en `CompartidoDbContext`). Compras hará lo
   mismo si necesita exponer `EmpresaId` en sus configuraciones EF.
   No es bloqueante, pero **es un patrón a replicar consistentemente**
   en F0-PR4 / F1-PR2.

10. **F4-PR3 implica transacción cross-port.**
    El diseño §6.1 dice "Todo en la misma transacción de aplicación;
    si algo falla, falla la autorización." Si `IReservarStockPort` y
    `IGenerarMovimientoSalidaPort` viven en otro proceso (Almacén) y
    `IGenerarSolicitudCompraPort` en otro (OC), una transacción única
    cross-process es imposible (no hay 2PC). En v1 todos son in-proc
    (mismo monolito modular), entonces basta una transacción local
    de EF. **[Pendiente confirmar]** que esto sigue siendo el caso
    cuando Almacén / OC se construyan; si se separan, hay que rediseñar.

---

## Cambios respecto a versiones previas

### Rev. 1 — versión inicial (2026-05-07)

Primer corte de cuidados de infra. 14 bloques. Calibrado contra el
diseño Rev. 11, plan Rev. 5, y audit del repo. Pendiente de revisión
con el equipo.
