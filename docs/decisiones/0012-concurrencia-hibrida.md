# ADR-0012: Estrategia de concurrencia (optimista en BD + soft locks UI + hard locks selectivos)

- **Estado**: Aceptada
- **Fecha**: 2026-05-02
- **Decisores**: Eduardo Paredes
- **Etiquetas**: arquitectura, concurrencia, base-de-datos, ux, fundación

## Contexto y problema

En un ERP multiusuario, dos personas pueden editar el mismo registro al mismo
tiempo. Sin protección, el último en guardar gana silenciosamente y los
cambios del primero se pierden ("lost update"). Esto es inaceptable en datos
financieros donde cada cambio puede tener impacto contable o fiscal.

Pero el problema no se resuelve con una sola estrategia. Las opciones tienen
tradeoffs distintos:

- **Optimista** (versionado de filas, validar al guardar): barata, protege
  contra cualquier camino de actualización (UI, jobs, integraciones), pero el
  usuario solo descubre el conflicto al final cuando ya invirtió tiempo en su
  edición
- **Pesimista hard lock** (bloqueo mientras un usuario edita): el usuario sabe
  desde el inicio que alguien más está trabajando ahí, pero introduce
  complejidad real (timeouts, heartbeats, manejo de desconexiones) que es
  particularmente costosa en una arquitectura HTTP/REST stateless
- **Sin protección**: lost updates silenciosos, inaceptable

Necesitamos una estrategia que combine lo mejor de cada enfoque según el costo
del conflicto en cada tipo de entidad.

## Drivers de la decisión

- Imposible perder cambios silenciosamente, sin importar el camino de actualización
- UX: en entidades de alto valor, el usuario debe saber que alguien más está editando antes de invertir tiempo
- No introducir complejidad operacional innecesaria en entidades donde los conflictos son raros
- Compatibilidad con jobs en background, integraciones, y correcciones manuales por DBA
- Aprovechar SignalR (ADR-0001) para awareness colaborativo sin infraestructura adicional

## Opciones consideradas

1. Híbrido: optimista en BD + soft locks UI vía SignalR + hard locks duros en whitelist mínima
2. Pesimista hard lock para todas las entidades editables
3. Solo optimista en BD, sin awareness en UI
4. Solo soft locks vía SignalR, sin protección en BD

## Decisión

Se adopta la **opción 1: híbrida en tres capas**, aplicada según el costo del
conflicto en cada tipo de entidad.

### Capa 1 — Optimismo en BD (siempre, obligatorio)

Toda entidad de dominio hereda de `BaseEntity`, que incluye:

```csharp
public abstract class BaseEntity
{
    public Guid Id { get; set; }
    public int Version { get; set; }   // configurada como IsConcurrencyToken
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    // ...
}
```

En el `BaseDbContext`, `Version` se configura como `IsConcurrencyToken()` y se
incrementa automáticamente en cada `SaveChanges`. Si dos transacciones intentan
actualizar la misma fila con el mismo `Version`, EF Core lanza
`DbUpdateConcurrencyException` en la segunda. Esto se traduce a HTTP 409
(Conflict) con Problem Details (ADR-0010).

**Esta capa es no-negociable**: protege contra TODOS los caminos de
actualización, incluyendo jobs en background, integraciones externas, y
ediciones desde la UI que escapen del soft lock.

### Capa 2 — Soft locks en UI (awareness colaborativo, no bloqueante)

Para entidades donde editar al mismo tiempo es probable y costoso pero no
catastrófico, la UI muestra al usuario quién más tiene el registro abierto.

**Mecánica**:

- Cuando un usuario abre la pantalla de detalle/edición de un registro, el cliente envía un mensaje al hub SignalR del módulo: `viewingResource(entidad, id)`
- El hub mantiene en memoria un mapa `(entidad, id) → [usuarios con sesión abierta]` por empresa
- Cuando otro usuario abre el mismo registro, ambos reciben una notificación: "María García también tiene este registro abierto (última actividad: hace 12s)"
- Si un usuario hace cambios sin guardar, su estado pasa a `editing`; los otros ven "María García está editando"
- El estado NO se persiste en BD; vive en memoria del hub. Si la app reinicia, los soft locks se pierden (aceptable: los usuarios reconectan y reportan su estado)
- Heartbeat: el cliente reporta actividad cada 30s. Si no hay heartbeat en 90s, se asume desconexión y el lock se libera

**Aplicabilidad**: clientes, proveedores, cotizaciones, pedidos, requisiciones,
recepciones, remisiones, contratos, plantillas, configuraciones de módulo no
críticas. La gran mayoría de las entidades transaccionales y maestras.

### Capa 3 — Hard locks en whitelist mínima

Para entidades donde un conflicto sería catastrófico (pérdida de datos críticos
con impacto fiscal o contable), se aplica un lock duro: solo un usuario a la
vez puede editar.

**Whitelist de entidades con hard lock**:

- **CFDI en estado `Borrador`**: antes de timbrar. Una vez timbrado, el CFDI se vuelve inmutable y deja de necesitar lock.
- **Póliza contable en captura**: antes de posteo. Una vez posteada, inmutable.
- **Cierre contable mensual o anual**: mientras el job de cierre corre, nadie puede editar el periodo afectado. Lock global por periodo.

**Mecánica**:

- Tabla `core.locks_duros` con: `id`, `entidad`, `entidad_id`, `usuario_id`, `empresa_id`, `acquired_at`, `last_heartbeat_at`, `expires_at`
- Al intentar editar una entidad en whitelist, el cliente llama a `POST /api/locks/acquire { entidad, entidadId }`
- El backend hace `INSERT ... ON CONFLICT DO NOTHING` con TTL de 15 min: si tiene éxito, el usuario obtiene el lock; si la fila ya existe y no expiró, recibe 409 con info de quién lo tiene
- El cliente envía heartbeat cada 60s para extender el TTL: `PATCH /api/locks/{id}/heartbeat`
- Si el cliente desconecta o no envía heartbeat, el lock expira a los ~90s tras el último heartbeat
- Al guardar, el cliente libera el lock: `DELETE /api/locks/{id}`
- Job en background limpia locks expirados cada minuto
- El holder del lock se publica también vía SignalR para que otros usuarios vean inmediatamente "Bloqueado por María García hasta hh:mm"

**Razón de la whitelist mínima**: cada hard lock añade complejidad operacional
real (timeouts, recuperación de bloqueos huérfanos, locks "perdidos" que un
admin tiene que liberar). Limitamos esta complejidad a casos donde realmente
se justifica.

### Manejo de conflictos en UI

Cuando ocurre un conflicto optimista (HTTP 409 al guardar):

- El frontend muestra un diálogo: "Tus cambios no se pueden guardar porque otro usuario modificó este registro mientras lo editabas"
- Opciones presentadas: ver los cambios del otro usuario, fusionar (si los campos cambiados no se solapan), descartar mis cambios, mantener mi versión sobreescribiendo (requiere permiso `core.conflicto.sobrescribir`, asignado solo a admins)
- En la mayoría de los casos (campos no solapados), el frontend puede ofrecer fusión automática presentando solo los campos en conflicto

## Consecuencias

**Positivas**
- Protección absoluta contra lost updates en BD: imposible perder cambios silenciosamente
- UX informada en entidades importantes: el usuario sabe quién más está editando ANTES de invertir tiempo
- Locks duros solo donde se justifica: complejidad operacional acotada
- SignalR ya está en el stack (ADR-0001), no añade infraestructura
- Heartbeats vía endpoint dedicado son cheap y robustos
- Las tres capas son ortogonales: cada una protege contra escenarios distintos

**Negativas**
- Tres mecanismos coexistiendo aumenta la superficie conceptual; los devs deben entender cuándo aplica cada uno
- Soft locks viven en memoria del hub: si el App Service reinicia, se pierden hasta que los clientes reconecten (aceptable, pero hay que documentarlo)
- Hard locks pueden quedar "huérfanos" si el cliente nunca libera y el TTL es largo. Mitigado por heartbeat corto y job de limpieza, pero ocasionalmente un admin tendrá que forzar liberación manual
- Conflict UX en frontend requiere componente reutilizable bien diseñado; sin eso, los 409 son frustrantes para el usuario

## Descartadas

**Pesimista hard lock para todas las entidades editables**. Multiplicaría la
complejidad operacional sin beneficio proporcional. Para una cotización de
cliente B-tier, un soft lock con awareness es suficiente; un hard lock con
heartbeat dedicado es overkill.

**Solo optimista, sin awareness UI**. Los usuarios del ERP descubrirían
conflictos solo al guardar, después de invertir tiempo en su edición.
Experiencia frustrante en flujos largos (capturar una factura compleja).

**Solo soft locks SignalR, sin protección en BD**. La protección desaparece en
cualquier camino que no pase por la UI: jobs, integraciones, scripts de
mantenimiento. En un ERP estos caminos existen y deben estar protegidos.

## Notas de implementación

**Capa 1 — Optimismo**
- `BaseEntity` con propiedad `Version` (int) y configuración `IsConcurrencyToken()` en `BaseDbContext.OnModelCreating`
- `BaseDbContext` incrementa `Version` automáticamente en cada `SaveChanges` para entidades modificadas
- `DbUpdateConcurrencyException` se mapea en el middleware de errores (ADR-0010) a HTTP 409 con `type = "concurrency_conflict"` y detalle del registro
- Tests parametrizados que verifican el comportamiento en una muestra representativa de entidades

**Capa 2 — Soft locks**
- Hub SignalR `CollaborationHub` por empresa con métodos `viewingResource`, `editingResource`, `releaseResource`
- Servicio in-memory `ICollaborationStateService` (singleton) con la estructura `(empresaId, entidad, entidadId) → List<UserPresence>`
- Heartbeat cada 30s desde el cliente; expiración a 90s sin actividad
- Hook frontend `useCollaboration(entidad, id)` retorna lista de otros usuarios presentes
- Componente `<CollaborationIndicator />` visualiza quién más está viendo/editando
- Convención de cuáles entidades usan soft lock: declaradas en una lista por módulo (no automático)

**Capa 3 — Hard locks**
- Tabla `core.locks_duros` con índice único `(entidad, entidad_id)` para que `INSERT ON CONFLICT` funcione
- Endpoints: `POST /api/locks/acquire`, `PATCH /api/locks/{id}/heartbeat`, `DELETE /api/locks/{id}`
- Hosted service `LockCleanupJob` corre cada 60s y borra locks con `expires_at < NOW()`
- Hook frontend `useHardLock(entidad, id)` que adquiere al montar, hace heartbeat, libera al desmontar o al guardar
- Whitelist explícita de entidades con hard lock declarada en un archivo de configuración (`HardLockedEntities.cs`); cualquier otra usa solo soft lock
- Permiso `core.lock.forzar_liberacion` para admins que necesiten liberar manualmente locks huérfanos en casos extremos
- Endpoint admin: `DELETE /api/admin/locks/{id}` con auditoría obligatoria del motivo

**Manejo de conflictos en UI**
- Componente reutilizable `<ConflictResolutionDialog />` que muestra:
  - Tus cambios pendientes
  - Los cambios remotos (cargando la versión actual del servidor)
  - Diff campo por campo con marcadores de conflicto
  - Acciones: descartar míos, sobrescribir (con permiso), fusionar (cuando es trivial), reintentar
- Hook `useEntityForm` envuelve `react-hook-form` y maneja 409 automáticamente

**Cambios en otras ADRs**
- ADR-0005: `BaseEntity` agrega columna `version int not null default 0`; aplica a TODAS las tablas de entidades de dominio
- ADR-0007: nuevos permisos `core.conflicto.sobrescribir` y `core.lock.forzar_liberacion`

**ADRs hijo posibles**
- Diseño detallado del componente `<ConflictResolutionDialog />` y patrones de fusión automática
- Estrategia de awareness colaborativo más rica (cursores en vivo, comentarios) si se necesita
