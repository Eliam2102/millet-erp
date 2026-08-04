# ADR-0008: Estrategia de auditoría

- **Estado**: Aceptada
- **Fecha**: 2026-05-01
- **Decisores**: Eduardo Paredes
- **Etiquetas**: cumplimiento, auditoría, base-de-datos, fundación

## Contexto y problema

En un ERP, la auditoría no es opcional. El SAT puede pedir trazabilidad de
quién emitió, modificó o canceló un CFDI. El control interno requiere saber
quién aplicó un pago, autorizó una nota de crédito, modificó una condición
comercial, o ajustó un asiento contable. La trazabilidad debe sobrevivir al
tiempo: cinco años para cumplimiento fiscal, idealmente para siempre en datos
financieros sensibles.

Necesitamos una estrategia de auditoría que:

- Se aplique automáticamente a todas las entidades relevantes sin pedirle al
  programador que escriba código en cada `Save`
- Capture qué cambió (campo a campo), quién lo cambió, cuándo, desde dónde
- Sobreviva a borrados accidentales (auditoría inmutable)
- Tenga impacto bajo en performance de operaciones normales

## Drivers de la decisión

- Cumplimiento fiscal y de control interno
- Facilidad para el programador (idealmente cero código por entidad)
- Performance: las consultas de auditoría no deben afectar OLTP normal
- Espacio de almacenamiento razonable (la auditoría puede crecer rápido)

## Opciones consideradas

1. Tabla `audit_log` central + EF Core `SaveChangesInterceptor`
2. Temporal Tables nativas de PostgreSQL (extensión `temporal_tables` o `pg_temporal`)
3. Event Sourcing parcial (solo para entidades críticas como CFDI, pagos, asientos)
4. Append-only por entidad (cada tabla tiene su `tabla_history`)
5. Triggers de PostgreSQL para registrar cambios

## Decisión

Se propone una **combinación**:

**Capa 1 — Auditoría general automática (todas las entidades)**

Tabla `audit_log` en esquema `core`:

```
audit_log
├── id (uuid v7, PK)
├── timestamp (timestamptz, not null)
├── usuario_id (uuid, FK identidad.usuarios)   -- nullable solo para eventos de sistema
├── empresa_id (uuid, FK compartido.empresas)  -- nullable para eventos de sistema o cross-empresa (ver ADR-0011)
├── modulo (text, not null)                     -- 'fiscal', 'cobranza', etc.
├── entidad (text, not null)                    -- 'cfdi', 'pago', etc.
├── entidad_id (uuid, not null)                 -- el ID del registro afectado
├── operacion (text, not null)                  -- 'crear' | 'actualizar' | 'borrar' | 'denegado'
├── cambios (jsonb, not null)                   -- formato según 'operacion' (ver abajo)
├── ip (inet, nullable)
├── correlation_id (uuid, not null)             -- mismo TraceId que App Insights (ADR-0006)
├── es_bulk (bool, default false)               -- true si fue un INSERT agregado por BulkAuditMode
└── metadatos (jsonb, nullable)                 -- contexto adicional libre
```

Particionado mensual por `timestamp` (declarative partitioning de PostgreSQL).
La FK `usuario_id` se mantiene como referencia lógica (no constraint físico
estricto) para tolerar particiones archivadas y permitir el archival a Blob.

**Formato de `cambios` según `operacion`** (estrategia híbrida):

- `crear`: snapshot completo de la entidad recién creada
  ```json
  {"snapshot": {"id": "...", "rfc": "XAXX010101000", "razonSocial": "...", ...}}
  ```
- `actualizar`: solo los campos que cambiaron, con valor anterior y nuevo
  ```json
  {"diff": {"estatus": {"antes": "Borrador", "despues": "Timbrado"}}}
  ```
- `borrar` (solo aplica a entidades NO `IFiscalmenteRelevante`): snapshot al momento del borrado
  ```json
  {"snapshot_pre_borrado": {...}}
  ```
- `denegado`: intento fallido por falta de permiso o regla de negocio
  ```json
  {"intento": "fiscal.cfdi.cancelar", "razon": "permission_denied"}
  ```

Razón del híbrido: el 99% de las consultas reales son "qué cambió y quién", lo
que se responde con `diff`. Para reconstruir el estado completo en cualquier
momento T se hace replay desde el snapshot inicial aplicando los diffs en orden.

Implementación: un `AuditSaveChangesInterceptor` de EF Core captura los cambios
de las entidades marcadas con `IAuditable` y escribe en `audit_log`. Cero código
por entidad, salvo declarar la interfaz.

**Capa 2 — Soft delete obligatorio en entidades fiscales**

Entidades con implicaciones fiscales (CFDIs, asientos contables, pagos, notas
de crédito) **nunca se borran físicamente**. Tienen columna `deleted_at`
nullable; queries filtran automáticamente `WHERE deleted_at IS NULL`. La
operación de "cancelar" un CFDI marca el campo correspondiente del SAT, no
borra nada.

Esto se implementa con `IFiscalmenteRelevante` y un query filter global en EF
Core.

**Capa 3 — Inmutabilidad operacional**

La tabla `audit_log` no permite UPDATE ni DELETE a la cuenta de aplicación. La
cuenta solo tiene `INSERT` y `SELECT` (este último restringido por permiso del
módulo). Solo el rol DBA puede manipular las particiones y queda registrado en
logs de PostgreSQL. NO se implementa hash chain criptográfico (overkill para
este alcance; la inmutabilidad por permisos es suficiente).

**Capa 4 — Alcance de IAuditable (opt-in con enforcement)**

`IAuditable` es **opt-in**: las entidades de dominio se auditan solo si
declaran explícitamente la interfaz. Esto evita ruido de tablas operativas
(outbox, eventos procesados, cache temporales).

Para evitar olvidos, hay un **test/linter en CI** que recorre todas las
clases de entidad de cada módulo y falla si una entidad NO declara
explícitamente `IAuditable` o `INotAudited`. El programador está obligado a
decidir conscientemente.

**Capa 5 — Auditoría de intentos denegados**

El `PermissionAuthorizationHandler` (ADR-0007) registra en `audit_log` con
`operacion = 'denegado'` cualquier intento fallido por falta de permiso.
Valor para seguridad: detectar usuarios probando acciones que no les
corresponden. Costo bajo: son operaciones poco frecuentes.

NO se audita el acceso de lectura (consultas/listados); explotaría el volumen
sin valor proporcional.

**Capa 6 — Bulk operations**

Para importaciones masivas (ej. carga inicial desde SAP, importación de
CFDIs históricos), el `DbContext` expone un flag `BulkAuditMode`. Mientras
está activo, el interceptor no genera un registro de auditoría por entidad
sino **uno solo agregado** al final, con:

- `operacion = 'crear'`
- `entidad_id = NULL` (no aplica a una entidad específica)
- `es_bulk = true`
- `metadatos`: `{"total": 5420, "tipo": "importacion_sap_cfdis", "criterios": "..."}`

El detalle granular vive en los registros importados mismos (cada uno tiene su
`createdBy`, `createdAt`).

**Capa 7 — PII en el audit log**

El `cambios` jsonb puede contener PII (CURP, nombres de personas físicas,
CLABE, etc.) cuando se modifica un cliente o proveedor persona física.

**Decisión: se guarda en crudo**, sin enmascaramiento. La auditoría debe ser
autoritativa para cumplimiento; enmascararla la invalida para esa función.

La protección NO es a nivel de contenido sino de **acceso**:

- Permiso `auditoria.log.leer` (definido en ADR-0007)
- Asignado solo a roles `Auditor` y `SuperAdmin`
- Cualquier consulta a `audit_log` desde la UI o la API requiere ese permiso
- Las consultas a `audit_log` también se loguean (meta-auditoría) para detectar
  abuso de privilegio

## Consecuencias

**Positivas**
- Cero código de auditoría por entidad: declaras la interfaz y listo
- Una sola tabla central facilita queries cross-módulo ("dame todo lo que hizo el usuario X ayer")
- `jsonb` permite buscar cambios específicos sin esquema rígido (índices GIN sobre el campo)
- Cumple requisitos fiscales y de control interno
- Audit log es autoritativo (PII en crudo) sin comprometer privacidad (acceso restringido)
- Imposible olvidar marcar una entidad nueva: el test/linter falla
- Bulk operations no inflan el log con miles de registros redundantes
- Intentos denegados quedan trazados para análisis de seguridad
- `correlation_id` une cada cambio con su request en App Insights y con sus eventos publicados a Service Bus

**Negativas**
- `audit_log` puede crecer mucho (decenas de miles de registros por día en operación normal). Mitigado por particionado mensual + archival
- Reconstruir el estado completo en un momento T arbitrario requiere replay (snapshot de creación + diffs ordenados por timestamp). En la práctica casi nunca se necesita, pero hay que implementar el helper cuando surja el caso
- Performance del INSERT extra en `audit_log` por cada cambio. En benchmarks típicos es despreciable, pero hay que monitorear con métricas (latencia de `SaveChanges` antes vs. después del interceptor)
- El enforcement por test agrega fricción a quien crea entidades nuevas (debe declarar la interfaz). Asumida como costo aceptable a cambio de garantizar cobertura

## Descartadas

**Temporal Tables de PostgreSQL**. Hermosa solución conceptual, pero requiere
extensión que no siempre está disponible en Azure Flexible Server, y maneja
cada entidad por separado (no permite query unificado de "qué hizo el usuario X").

**Event Sourcing parcial**. Excelente para entidades muy críticas, pero
incrementa complejidad del modelo de dominio y de las queries. Lo dejamos como
posibilidad futura para CFDIs y pagos si la auditoría con `audit_log` resulta
insuficiente.

**Append-only por entidad** (`cfdis_history`, `pagos_history`, etc.).
Multiplica el número de tablas y dificulta queries cross-entidad. Mejor un
log central.

**Triggers de PostgreSQL**. Funcionan pero la lógica queda fuera del código
.NET, dispersa entre BD y aplicación. Más difícil de mantener y de hacer
versionar bien con migraciones.

## Notas de implementación

**Estructura y modelo**
- Crear interfaces marker: `IAuditable`, `INotAudited`, `IFiscalmenteRelevante`
- Test/linter en CI: recorre todas las clases que extienden de la base `Entity` y falla si NO declaran explícitamente `IAuditable` o `INotAudited`
- Implementar `AuditSaveChangesInterceptor` registrado en todos los `DbContext`
- Configurar query filter global para soft delete en entidades `IFiscalmenteRelevante`
- Crear esquema `core` con tabla `audit_log` particionada mensualmente por `timestamp`
- Índices: `(empresa_id, timestamp)`, `(usuario_id, timestamp)`, `(modulo, entidad, entidad_id)`, GIN sobre `cambios` y `metadatos`
- El audit_log está protegido por permiso `auditoria.log.leer` y aplica el query filter de empresa por defecto (excepto cuando se invoca con permiso `compartido.cross_empresa.leer`, ver ADR-0011)

**Permisos y acceso**
- Usuario de aplicación de PostgreSQL: solo `INSERT, SELECT` sobre `audit_log` (no `UPDATE`, no `DELETE`)
- Usuario DBA: full access, pero con logging activado en PostgreSQL para visibilidad
- Permiso `auditoria.log.leer` requerido para cualquier consulta vía API/UI
- Las consultas a `audit_log` se registran en `audit_log` mismo (meta-auditoría) con `entidad = 'audit_log'`

**Bulk operations**
- Flag `BulkAuditMode` en `DbContext` activable temporalmente
- Cuando está activo, el interceptor agrega un solo registro agregado al final
- Util para imports desde SAP, regeneraciones masivas, recálculos batch

**Política de retención (a implementar en job mensual)**
- 0 a 24 meses: vive en particiones live de PostgreSQL (`audit_log_y2026m05`, etc.)
- 25 meses en adelante: la partición se exporta a Blob Storage tier `Cool` (formato Parquet con metadata para búsqueda) y se elimina la partición de PostgreSQL
- En Blob `Cool`: se conserva por 5 años (cumplimiento SAT)
- Después de 5 años:
  - Operaciones fiscales (entidades `IFiscalmenteRelevante`): pasan a tier `Archive` indefinidamente
  - Operaciones no fiscales: se eliminan
- Job mensual `AuditArchiveJob` implementado como hosted service. Como es un job singleton (solo una instancia debe correr la migración a Blob por mes), aplica el patrón de **advisory lock** descrito en la sección "Despliegue" de ADR-0009: la instancia que obtiene `pg_try_advisory_lock` ejecuta el archival, las demás duermen. Si en el futuro el job crece a procesar volúmenes grandes (GB), se extrae a Container Apps Jobs o Azure Functions con Timer Trigger como contempla esa misma ADR

**Reconstrucción de estado en momento T (helper)**
- Cuando se necesite "dame el estado de la entidad X al día Y":
  1. Localizar el snapshot inicial en `cambios.snapshot` (operación `crear`)
  2. Recuperar todos los diffs posteriores hasta la fecha Y
  3. Aplicar diffs en orden cronológico
- Implementar como utility `IAuditReplayService.ReconstructAsync(entidadId, fechaCorte)`

**Performance**
- Métricas custom: `SaveChanges` latency con/sin interceptor, INSERT/seg en `audit_log`
- Alerta si latencia mediana sube más de 20% tras la habilitación

**ADRs hijo posibles** (cuando surjan)
- Estrategia de búsqueda eficiente sobre el archive (cuando se necesite consultar datos de >2 años atrás regularmente)
- Event Sourcing parcial para CFDIs y pagos (si la auditoría diff/snapshot resulta insuficiente para alguna necesidad de cumplimiento)
