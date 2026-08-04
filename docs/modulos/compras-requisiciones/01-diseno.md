# Diseño — Submódulo Requisiciones (Módulo Compras)

> **Construido sobre:** [00-levantamiento-legacy-portalsap.md](00-levantamiento-legacy-portalsap.md)
> (levantamiento del legacy, validado con el cliente).
>
> **Estado:** propuesta de diseño para revisión con el owner. Las decisiones
> marcadas como `[Asunción]` requieren confirmación del cliente antes de
> implementar.
>
> **Fecha:** 2026-05-07.

---

## 0. Cómo leer este documento

- `[Decidido]` — fijado por ADR existente o por sesión con el cliente.
- `[Asunción]` — propuesta del diseñador. Razonable pero pendiente de
  confirmación. Está claramente listada en la sección 3.
- `[Diferido]` — fuera de alcance de v1; se anota para no perderlo.

Este documento describe **qué construir y por qué**, no el código. La
implementación seguirá las convenciones del repo (hexagonal, CQRS con
MediatR, EF Core, FluentValidation, Serilog, records, sealed por defecto).

---

## 1. Posicionamiento y alcance

### 1.1 Ubicación en el ERP

- **Módulo:** `Compras`
- **Submódulo:** `Requisiciones` (este documento)
- **Esquema PostgreSQL:** `compras` (compartido con el resto de Compras)
- **Bounded context:** `Compras`. La requisición y la orden de compra
  conviven en el mismo BC porque comparten lenguaje, reglas de
  autorización y proveedor.

### 1.2 Alcance funcional v1

**Dentro:**

1. CRUD de requisición (cabecera + líneas).
2. Workflow de autorización en dos niveles, **a nivel de cabecera**.
3. Consulta de disponibilidad de stock al autorizar (vía Almacén).
4. **Bifurcación stock-aware** por línea:
   - Lo cubierto con stock → se materializa como **movimiento de salida
     de almacén** (vía Almacén).
   - El saldo no cubierto → genera una **Solicitud de Compra (Orden
     de Compra borrador)** dentro del mismo módulo Compras.
5. Re-autorización de saldos cuando llega material parcial.
6. Soft delete (rechazo = eliminación lógica con motivo).
7. Notificaciones por evento (vía módulo Notificaciones).
8. Auditoría (vía framework de auditoría del ERP).

**Fuera (validado con cliente, ver levantamiento §0.bis.2):**

- Cotizaciones formales / módulo "Solicitudes de cotización".
- Control presupuestal y consumo presupuestal.
- Multi-moneda activa (default MXN; el value object soporta otras pero
  no se usan en v1).
- Importaciones (`CONTENEDOR`).
- Caja chica (es módulo separado del ERP).
- Microsip y cualquier integración legacy contable.
- Multi-empresa SAP (ver ADR 0011 — se evalúa cuando el cliente confirme
  cuántas sociedades).
- Autorización a nivel de línea (`DO_RQ_DET_AUT` vestigial en el legacy).
- DIOT separado por línea (vestigial).
- Replicación a SAP B1 (el ERP nuevo es destino, no SAP).

### 1.3 Volúmenes esperados (orientativos)

Del legacy: 226k requisiciones en ~9 años → ~25k/año → ~70/día. Picos
modestos. **Sin requerimientos especiales de escala**; un solo nodo
.NET sirve.

---

## 2. Decisiones de diseño y ADRs aplicados

| Tema | Decisión | Referencia |
|---|---|---|
| Identidad y autorización | Entra ID + RBAC granular por permisos | ADR 0003, ADR 0007 |
| Concurrencia | Optimista por `xmin`/`RowVersion` | ADR 0012 |
| Auditoría | Framework centralizado, no columnas duplicadas en cada tabla | ADR 0008 |
| Migraciones | EF Core, esquema por módulo | ADR 0005 |
| Validación | FluentValidation por comando | ADR 0018 |
| Eventos de integración | Outbox + Service Bus | ADR 0009 |
| Errores HTTP | Problem Details (RFC 7807) | ADR 0010 |
| Versionado API | URL versioned (`/api/v1/...`) | ADR 0021 |
| Idempotencia HTTP | Header `Idempotency-Key` en POST que crean recursos | ADR 0020 |
| Notificaciones email | Vía módulo Notificaciones | ADR 0026 |
| Multi-empresa | `EmpresaId` en agregados con datos por sociedad | ADR 0011 |
| Tiempo / zonas | UTC en BD, conversión en presentación | ADR 0013 |
| Multimoneda | Value object Money. v1 fija MXN. | ADR 0014 |
| Adjuntos | Blob storage (no `image` en BD) | ADR 0024 |

> Los ADRs son la fuente de verdad. Si este documento contradice un ADR,
> gana el ADR.

---

## 3. Asunciones que deben confirmarse con el cliente

Estas asunciones son las del diseño v1. Si el cliente las descarta,
algunas secciones cambian.

| # | Asunción | Default propuesto | Si el cliente dice "no" |
|---|---|---|---|
| A1 | **Matriz de aprobación** | **CERRADA — cliente define modelo multidimensional de 3 capas:** (1) **departamental** — el jefe del departamento del solicitante aprueba primero, cada departamento tiene su propia estructura; (2) **por monto, condicional** — si el monto rebasa el umbral del departamento, requiere N2; el umbral es **por departamento** (no por sucursal); (3) **por naturaleza del artículo** — `Crítico` → siempre N2; `Riesgo` (químicos, TI, etc.) → aprobación adicional; `Estándar` → N1 = jefe de almacén basta; `Servicio` → N1 = jefe de departamento. La naturaleza puede elevar el nivel requerido. **El motor de reglas combina las 3 capas y resuelve (a) si requiere N2, (b) qué rol cubre cada nivel.** Detalles en §3.bis. | — |
| A2 | **Reserva de stock** | **CERRADA — cliente confirma: lock al reservar.** Modelo: **tabla de reservas en Almacén**, no decremento físico. `disponible = on_hand - SUM(reservas_activas)`. La reserva tiene `ReservaId` y **TTL configurable** (default 14 días). Saneamiento de reservas vencidas vía background job (ADR 0022). Liberación explícita en `Cancelar`/`Eliminar`/`Rechazar`. | — |
| A3 | **Generación de OC y movimiento de almacén** | **REVISADA 2026-05-13 — ahora configurable por empresa, ver ADR-0033.** Movimiento de almacén: siempre automático y síncrono dentro del handler de `AutorizarRequisicionCommand` (reservar + generar movimiento de salida en la misma TX). Generación de OC borrador: gobernada por el setting `compras.settings.auto_generar_oc_al_autorizar` (default **false** — manual). En modo `true`, el handler llama al puerto OC en la misma TX (Narrativa A original). En modo `false`, la RQ queda en `EnSurtido` con `ComprometidaEnOcId=null` y el comprador la convierte manualmente vía el botón "Convertir a OC" del detalle o desde el Sheet "Nueva OC" en modo Consolidación (Narrativa B del diseño OC §3.bis.1). El setting se cambia con `PATCH /api/v1/compras/configuracion`. | — |
| A4 | **Disparador del movimiento de almacén** | **CERRADA — cliente confirma: depende de la naturaleza del artículo.** Coherente con A1: cuando la matriz se satisface (sea con solo N1 o con N1+N2), se ejecuta la bifurcación completa (movimiento + OC). Para artículos `Estándar`, basta N1 (jefe almacén) → movimiento se ejecuta tras N1. Para artículos que requieren N2 (por naturaleza o monto), el movimiento espera a N2. **El "disparador" siempre es la satisfacción de la matriz**, no un nivel fijo. | — |
| A5 | **Rechazo con motivo** | **CERRADA — cliente confirma: catálogo + opción OTRO con texto libre.** Tabla `compras.motivos_rechazo` (motivo_id, clave, descripcion, permite_texto_libre, activo). Opción "OTRO" con `permite_texto_libre=true` permite captura manual. El comando `RechazarRequisicionCommand` recibe `motivoId` (obligatorio) + `motivoTexto` (opcional, requerido si el motivo permite texto libre). Aplica también a `EliminarRequisicionCommand` y `CancelarRequisicionCommand`. | — |
| A6 | **Concurrencia** | **CERRADA — se aplica ADR 0012 tal cual.** El ADR define modelo híbrido en 3 capas y nombra explícitamente a "requisiciones" como caso de **Capa 2** (soft lock). Para Requisiciones del ERP nuevo: **Capa 1** (optimismo `Version`/`IsConcurrencyToken`) + **Capa 2** (awareness colaborativo vía SignalR). No se necesita Capa 3 (hard lock); el ADR la reserva para CFDI, pólizas y cierre contable. La UI muestra "Pedro está editando" cuando otro usuario tiene el documento abierto; ambos pueden trabajar y el segundo en guardar recibe 409 con resolución de conflicto. Detalles en §3.bis.4. | — |
| A7 | **Folio** | **CERRADA — cliente confirma:** formato `{prefijo}{año}-{secuencial:6}` (ej. `MID2026-000001`). Generación atómica vía secuencia PostgreSQL. | — |
| A8 | **Migración de RQs en vuelo** | **CERRADA — cliente confirma: migración selectiva.** Las RQs **con OC generada y material pendiente de recibir** SÍ se migran (preservando estado y cubrimiento). Las RQs **sin OC** (Borrador, EnAutorizacion, Autorizada que aún no bifurcó) se cancelan en el legacy y se rehacen en el ERP nuevo. Esto requiere script de migración con mapeo de estados — ver §11. | — |
| A9 | **Catálogos legacy a importar** | **CERRADA — cliente confirma:** **departamentos, sucursales, almacenes, clasificaciones se diseñan fresh en el ERP nuevo** (no se importan del legacy). **Prioridades** y **derechos** sí se importan. **Proveedores y artículos** se exportan one-shot desde SAP. | — |
| A10 | **Sucursales lógicas vs físicas** | **CERRADA — cliente confirma:** el catálogo de Sucursales del ERP solo modela sucursales físicas (oficinas reales). Lo lógico ("Consejo de administración", "Pintura") se modela como Áreas/Departamentos. | — |
| A11 | **Adjuntos en la RQ** | **CERRADA — cliente confirma: v1.1.** v1 sin adjuntos. v1.1 los modela como `Adjunto` (value object con referencia a blob, tipo MIME, nombre, autor, fecha) usando ADR 0024 (blob storage). Migración aditiva, sin impacto en datos de v1. | — |

---

## 3.bis Conceptos derivados de las respuestas del cliente

Estas son piezas nuevas del modelo que surgen de RA1, RA4, RA5, RA6.

### 3.bis.1 Naturaleza del artículo (RA1, RA4)

Atributo **del artículo** (vive en `compras.articulos`, no en la línea
de RQ — la línea lo hereda al referir el artículo). Enum o catálogo:

| Valor | Implicación en autorización |
|---|---|
| `Estandar` | N1 = jefe de almacén basta. No requiere N2 salvo por monto. |
| `Servicio` | N1 = jefe de departamento (no almacén). |
| `Critico` | Siempre requiere N2 (independiente del monto). |
| `Riesgo` | Requiere aprobación adicional — *pendiente afinar quién: ¿N2 especializado por categoría (químicos, TI)? Ver preguntas abiertas.* |

**Regla agregada para una RQ con múltiples líneas:** se aplica la
naturaleza **más restrictiva** entre todas las líneas
(`Critico > Riesgo > Servicio > Estandar`).

### 3.bis.2 Matriz de aprobación — modelo de evaluación (RA1)

Pseudocódigo del motor:

```
NivelRequerido evaluar(Requisicion rq, MatrizConfig matriz):
    naturaleza = naturaleza_mas_restrictiva(rq.lineas)
    monto_total = sum(linea.cantidad * linea.precio_estimado for linea in rq.lineas)

    # Capa 3: naturaleza fuerza N2
    if naturaleza in (Critico, Riesgo):
        return N1_y_N2

    # Capa 2: monto vs umbral del departamento
    umbral = matriz.umbral_por_departamento(rq.departamento_id)
    if monto_total > umbral:
        return N1_y_N2

    # Capa 1: solo N1
    return solo_N1

ResolverAutorizadorN1(rq, naturaleza):
    if naturaleza == Estandar:
        return jefe_de_almacen(rq.almacen_destino_id)
    else:
        return jefe_de_departamento(rq.departamento_id)

ResolverAutorizadorN2(rq, naturaleza):
    # Pendiente afinar: ¿gerencia general?
    # ¿Gerencia especializada para Riesgo?
    return autorizador_n2_de_sucursal(rq.sucursal_id)
```

Tablas de configuración (en `compras`):

```sql
compras.umbrales_aprobacion_departamento (
    empresa_id              uuid          NOT NULL,
    departamento_id         uuid          NOT NULL,
    umbral_monto            numeric(15,2) NOT NULL,
    moneda                  char(3)       NOT NULL DEFAULT 'MXN',
    vigente_desde           date          NOT NULL,
    vigente_hasta           date,                          -- NULL = vigente
    PRIMARY KEY (empresa_id, departamento_id, vigente_desde)
);

compras.aprobadores_departamento (
    empresa_id              uuid          NOT NULL,
    departamento_id         uuid          NOT NULL,
    rol                     smallint      NOT NULL,        -- JefeDpto, JefeAlmacen, AutorizadorN2
    usuario_id              uuid          NOT NULL,
    vigente_desde           date          NOT NULL,
    vigente_hasta           date,
    PRIMARY KEY (empresa_id, departamento_id, rol, vigente_desde)
);
```

**Preguntas abiertas que surgen de RA1** (no bloquean diseño, sí
implementación de Fase 9):

- ¿Cuántas naturalezas en total? Las 4 listadas arriba ¿son exhaustivas?
- Para `Riesgo`: ¿el "aprobador adicional" es un **3er nivel** o es un
  N2 especializado (gerente de seguridad química para químicos, CTO
  para TI, etc.)?
- ¿Hay un solo `JefeAlmacen` por almacén o varios (suplencia)?
- ¿Qué pasa si el solicitante es el propio jefe del departamento? ¿se
  auto-aprueba el N1 o se escala?
- ¿El umbral por departamento es por mes/año (acumulado) o por RQ
  individual? Asumimos por RQ.

### 3.bis.3 Catálogo de motivos de rechazo (RA5)

```sql
compras.motivos_rechazo (
    motivo_id               uuid          PK,
    clave                   varchar(20)   UNIQUE NOT NULL,
    descripcion             varchar(200)  NOT NULL,
    permite_texto_libre     boolean       NOT NULL DEFAULT false,
    aplica_a                smallint      NOT NULL,    -- bitmask: rechazo|eliminacion|cancelacion
    activo                  boolean       NOT NULL DEFAULT true
);
```

Datos seed razonables (a confirmar con cliente):

- `RECH-DUP` "Duplicada"
- `RECH-INSUF` "Información insuficiente"
- `RECH-INCOR` "Datos incorrectos"
- `RECH-PROV` "Proveedor no aprobado"
- `RECH-PRESUP` "Sin presupuesto disponible"
- `RECH-OTRO` "Otro" (`permite_texto_libre = true`)

Los comandos `RechazarRequisicionCommand`, `EliminarRequisicionCommand`,
`CancelarRequisicionCommand` reciben:

```csharp
RechazarRequisicionCommand(
    RequisicionId id,
    MotivoRechazoId motivoId,
    string? motivoTexto,    // requerido si motivoId permite texto libre
    Guid usuarioId
)
```

### 3.bis.4 Concurrencia (RA6) — alineado con ADR 0012

Aplicamos directamente el modelo híbrido del ADR 0012, sin extensiones
ni excepciones:

**Capa 1 — Optimismo en BD (siempre):**
- `Requisicion` hereda de `BaseEntity` con propiedad `Version` (int)
  configurada como `IsConcurrencyToken()` por el `BaseDbContext`
  estándar.
- Conflictos de versión se traducen a HTTP 409 con Problem Details
  (ADR 0010) y resolución vía `<ConflictResolutionDialog />` del
  framework.

**Capa 2 — Soft locks vía SignalR:**
- `Requisicion` está declarada en la lista de entidades con awareness
  colaborativo del módulo (mecánica del ADR 0012).
- El hook `useCollaboration("requisicion", id)` del frontend muestra
  "Pedro García también tiene este documento abierto" o "Pedro García
  está editando".
- Vive en memoria del `CollaborationHub`; heartbeat 30s, expira a 90s
  sin actividad. Si la app reinicia, se pierde y los clientes
  reconectan.
- **No se persiste en BD; no genera comandos ni endpoints en este
  módulo.** Es infraestructura compartida del ERP.

**No aplica Capa 3 (hard lock):** la whitelist del ADR contiene CFDI
en borrador, pólizas en captura y cierre contable mensual.
Requisiciones no entra ahí porque un conflicto no es catastrófico
(la Capa 1 ya garantiza no perder datos). El ADR explícitamente
nombra "requisiciones" como caso de Capa 2.

**Implicación para este diseño:** ningún cambio en agregado, comandos
ni endpoints más allá de heredar `BaseEntity` y declarar Requisicion
en la lista de entidades con soft lock del módulo.
| A12 | **Saldos no surtidos** | **CERRADA — cliente confirma: solo informativa, no hay re-autorización.** Cuando el proveedor entrega parcial y queda saldo, el sistema **avisa** (notificación + evento de integración) pero **no requiere aprobación humana** del saldo. La autorización inicial cubre todo el monto. El saldo se gestiona desde la OC (cancelar, completar con otra OC, etc.) sin volver a Requisiciones. **Esto elimina** del modelo: `Autorizacion.Tipo`, el loop EnSurtido → EnAutorizacion, y los estados ASP/ASC del legacy. | — |
| A13 | **Bulk operations** | v1 individual. **v1.1** agrega aprobación masiva en bandeja del autorizador (pedida #1 de UX casi seguro). | Si el cliente la pide para v1, se incluye. |
| A14 | **Read models** | v1: queries EF directas contra `compras.requisiciones*`. Si la bandeja del autorizador degrada con volumen, se evalúa vista materializada. No hay event sourcing ni proyecciones separadas en v1. | Si exigen separación física desde v1, se evalúa CQRS con proyecciones. |
| A15 | **Permisos de autorización de saldo** | **Mismos roles** que la autorización inicial. `compras:requisiciones:autorizar:nivel1` y `:nivel2` cubren ambas. Si el cliente requiere distinguirlos, se agregan permisos `:saldo:`. | Si los roles son distintos, se separan los permisos. |

---

## 4. Modelo de dominio

> **Workstream almacén-por-línea PR3 (RQ sin almacén user-facing).**
> `Requisicion.AlmacenDestinoId` pasó a **NULLABLE** (no se dropea). La RQ
> **manual** (capturada por el requisitante) nace con el campo en `null` — el
> usuario ya no captura almacén; desapareció del sheet, del detalle, del schema
> Zod, del endpoint `POST` y de `CrearRequisicionCommand`, y el handler dejó de
> validar existencia/pertenencia-a-sucursal del almacén. La RQ de **origen
> Sistema** (motor de reorden, ADR-0047) lo **sigue poblando** vía
> `ComprasCrearRqSistemaAdapter` — por eso el campo no se elimina: el reorden lo
> usa para deduplicar el "pedido vivo". El pedido-vivo
> (`ComprasPedidoVivoReadAdapter`) filtra `Origen == Sistema` en SQL para no
> materializar el `null` de una OC autorizada creada desde una RQ manual (fix
> del blocker de PR3). La matriz de aprobación (`ResolverAutorizadorService`,
> hoy dead code) tolera el `null` con `?? Guid.Empty` + PLATFORM-TODO. El
> diagrama de abajo conserva `AlmacenDestinoId` como historia; hoy es
> `AlmacenId?` (nullable).

### 4.1 Agregado raíz: `Requisicion`

```text
Requisicion (agregado raíz)
├── RequisicionId         : RequisicionId (Guid, identidad)
├── EmpresaId             : EmpresaId          ← ADR 0011
├── Folio                 : Folio (VO)
├── Clasificacion         : Clasificacion (enum)
├── SucursalId            : SucursalId
├── DepartamentoId        : DepartamentoId
├── AlmacenDestinoId      : AlmacenId
├── RequisitanteId        : UsuarioId
├── CreadorId             : UsuarioId          ← puede diferir del requisitante
├── Descripcion           : string?
├── Prioridad             : Prioridad (enum)
├── FechaSolicitud        : DateTime
├── FechaEntregaDeseada   : DateTime?
├── ProveedorSugeridoId   : ProveedorId?       ← opcional
├── Estado                : EstadoRequisicion (enum, state machine — §5)
├── MotivoEliminacion     : string?            ← solo si Estado == Eliminada
├── Lineas                : List<LineaRequisicion>   (entidad hija)
├── Autorizaciones        : List<Autorizacion>       (entidad hija)
└── Version               : uint               ← optimistic concurrency
```

**Invariantes del agregado:**

- `Lineas.Count >= 1` cuando `Estado != Borrador && Estado != Eliminada`.
- `Folio` único por (`EmpresaId`, año).
- Las transiciones de `Estado` solo se permiten vía métodos del agregado
  (`EnviarAAutorizacion()`, `Autorizar(nivel)`, `Eliminar(motivo)`,
  etc.) y respetan la state machine (§5).
- Una `Autorizacion` con `Nivel.Nivel2` solo se acepta si ya existe una
  `Nivel.Nivel1`.
- El monto total (`Lineas.Sum(l => l.Cantidad * l.PrecioEstimado)`)
  determina si se requiere N2 (asunción A1).
- No se pueden modificar `Lineas` cuando el estado pasa de
  `EnAutorizacion`.

**Comportamientos clave (métodos públicos):**

- `Crear(...)` → constructor estático. Estado inicial = `Borrador`.
- `AgregarLinea(...)`, `ActualizarLinea(...)`, `EliminarLinea(...)`.
- `EnviarAAutorizacion()` → valida `Lineas` no vacías; transiciona a
  `EnAutorizacion`; emite `RequisicionEnviadaAAutorizacionEvent`.
- `RegistrarAutorizacion(usuarioId, nivel, notas?)` → valida permiso y
  secuencia (N1 antes de N2), agrega `Autorizacion`. Si la matriz
  (asunción A1) queda satisfecha, emite
  `MatrizAprobacionSatisfechaEvent` que el handler usa para invocar la
  bifurcación síncrona.
- `RegistrarCubrimiento(IReadOnlyList<Cubrimiento>)` → llamado por el
  handler tras la bifurcación. Persiste cantidades por línea y avanza
  el estado a `Autorizada` o `EnSurtido` (según haya saldo de compra).
- `RegistrarRecepcion(lineaId, cantidadRecibida)` → llamado cuando la
  OC recibe material; actualiza `Cubrimiento`. Si todo se cubrió,
  transiciona a `Cerrada`. Si queda saldo no recibido (la OC entregó
  parcial y se cierra), emite `SaldoNoSurtidoEvent` (informativo, ver
  A12) — no abre re-autorización.
- `Rechazar(usuarioId, motivo)` → solo desde `EnAutorizacion`. Persiste
  motivo y autor. Estado → `Rechazada`.
- `Eliminar(usuarioId, motivo)` → soft delete pre-autorización. Solo
  desde `Borrador` o `EnAutorizacion`. Estado → `Eliminada`.
- `Cancelar(usuarioId, motivo)` → solo desde `Autorizada` o `EnSurtido`.
  Emite `RequisicionCanceladaEvent` que el handler usa para liberar
  reservas y abortar OC borrador. Estado → `Cancelada`.

### 4.2 Entidad: `LineaRequisicion`

```text
LineaRequisicion
├── LineaId               : LineaRequisicionId
├── RequisicionId         : RequisicionId  ← FK al agregado
├── Posicion              : int
├── ArticuloId            : ArticuloId     ← FK a catálogo de Compras
├── Cantidad              : decimal
├── UnidadMedida          : string         ← snapshot del catálogo
├── PrecioEstimado        : Money          ← MXN en v1
├── CuentaContableId      : CuentaContableId?  ← informativo, sin validación bloqueante
├── CentroCostoId         : CentroCostoId?     ← informativo
├── Proyecto              : string?
├── FechaRequerida        : DateTime?
├── Notas                 : string?
└── Cubrimiento           : Cubrimiento (VO)
```

**Invariantes:**
- `Cantidad > 0`.
- `Posicion` única dentro de la requisición.
- Una vez que `Cubrimiento.CantidadDeAlmacen + CantidadDeCompra > 0`,
  los campos estructurales (`ArticuloId`, `Cantidad`, `PrecioEstimado`,
  `UnidadMedida`, `CuentaContableId`, `CentroCostoId`, `Proyecto`,
  `FechaRequerida`) **no se pueden modificar**.
- **Excepción**: `Notas` es editable en cualquier estado no terminal.
  Sirve para anotaciones operativas (recepciones parciales, comentarios
  del autorizador, observaciones del comprador).

### 4.3 Value object: `Cubrimiento`

Rastrea cómo se está cubriendo cada línea — es la pieza central del
modelo stock-aware.

```text
Cubrimiento (inmutable, value object)
├── CantidadOriginal       : decimal
├── CantidadDeAlmacen      : decimal       ← consumido vía mov. de salida
├── CantidadDeCompra       : decimal       ← solicitada vía OC
├── CantidadRecibida       : decimal       ← ya recibida de la OC
└── CantidadPendiente      : decimal       ← computed
```

**Reglas:**
- `CantidadDeAlmacen + CantidadDeCompra <= CantidadOriginal`.
- `CantidadRecibida <= CantidadDeCompra`.
- `CantidadPendiente = CantidadOriginal - CantidadDeAlmacen - CantidadRecibida`.
- Cuando `CantidadPendiente == 0` → línea cerrada.
- Si `CantidadDeCompra > 0 && CantidadRecibida < CantidadDeCompra` y
  llega una recepción parcial → se abre **re-autorización de saldo**
  (estados 7 ASP / 8 ASC del legacy).

### 4.4 Entidad: `Autorizacion`

```text
Autorizacion
├── AutorizacionId        : AutorizacionId
├── RequisicionId         : RequisicionId
├── Nivel                 : NivelAutorizacion (enum: Nivel1, Nivel2)
├── UsuarioId             : UsuarioId
├── FechaHora             : DateTime (UTC)
└── Notas                 : string?
```

**Invariantes:**
- Una `(Requisicion, Nivel)` solo puede tener una autorización.
- `Nivel2` requiere que exista `Nivel1` previo.

> Nota (A12): no hay `Tipo` (Inicial/Saldo). La autorización inicial
> cubre todo el monto. Saldos no surtidos son informativos.

### 4.5 Value object: `Folio`

```text
Folio
└── Valor : string  ← formato configurable por sucursal
```

Reglas (asunción A7):
- Formato default: `{prefijoSucursal}{año}-{secuencial:6}`
  (ej. `MID2026-000001` para sucursal "Mérida").
- Únicidad por `(EmpresaId, año)`.
- Generación atómica vía secuencia PostgreSQL por sucursal+año.

---

## 5. Ciclo de vida (state machine)

> **Decisión de diseño**: 8 estados en lugar de los 11 del legacy.
> Información que el legacy modelaba como estado distinto y que aquí
> se **deriva** de los datos:
>
> - "AutorizadaParcial" vs "AutorizadaCompleta" → se deriva de
>   `Autorizaciones` y de la matriz de aprobación (¿requiere N2?). Mientras
>   no se cumplan **todos** los niveles requeridos, el estado es
>   `EnAutorizacion`. Cuando se cumplen, transiciona a `Autorizada`.
> - "EnSurtido" / "SurtidaParcial" / "Surtida" → se derivan del
>   `Cubrimiento` por línea. Mientras alguna línea tenga
>   `CantidadPendiente > 0`, está `EnSurtido`. Cuando todas cierran,
>   transiciona a `Cerrada`.
> - "EnAutSaldo" / "AutSaldoCompleta" del legacy → **no tienen
>   equivalente** en el ERP nuevo. El cliente confirmó (A12) que los
>   saldos no surtidos son informativos, sin re-aprobación.
>
> Esto colapsa 11 estados a 8 sin perder información. Las queries de
> bandeja explotan estas derivaciones en proyecciones (read models).

### 5.1 Estados

| Estado | Significado | Terminal | Equivalentes legacy |
|---|---|---|---|
| `Borrador` | Recién creada, editable | no | 1 INICIADO |
| `EnAutorizacion` | Esperando autorizador(es) | no | 2 CREADO, 4 AUTPAR |
| `Autorizada` | Todas las autorizaciones requeridas registradas; la bifurcación se ejecutó síncrona | no | 5 AUTCOM, 9 OCC |
| `EnSurtido` | Bifurcada: hay líneas con `CantidadPendiente > 0` (esperando recepción de OC) | no | 9 OCC, 10 SP |
| `Cerrada` | Todas las líneas cubiertas | **sí** | 11 SC, 6 DOFIN |
| `Cancelada` | Cancelada después de autorizada (libera reservas, aborta OC borrador) | **sí** | (no existía en legacy) |
| `Rechazada` | Autorizador rechaza desde `EnAutorizacion`. **No** vuelve a Borrador. | **sí** | 3 ELIMINADO (parcial) |
| `Eliminada` | Solicitante o admin descarta antes de autorizar | **sí** | 3 ELIMINADO |

### 5.2 Transiciones permitidas

```text
   ┌──────────────┐
   │   Borrador   │── Eliminar() ─────────────────────────┐
   └──────┬───────┘                                       │
          │ EnviarAAutorizacion()                         │
          ▼                                               │
   ┌─────────────────┐── Eliminar() ─────────────────────▶│
   │ EnAutorizacion  │── Rechazar(motivo) ─────────────┐  │
   └──────┬──────────┘                                 │  │
          │ Autorizar(usuario, nivel)                  │  │
          │ ◀─── ciclo: si faltan niveles, queda aquí  │  │
          │                                            │  │
          │ cuando cumple matriz de aprobación:        │  │
          │ [ejecuta bifurcación síncrona              │  │
          │   reservar+movimiento Almacén,             │  │
          │   crear OC borrador para saldo]            │  │
          ▼                                            │  │
   ┌──────────────┐                                    │  │
   │  Autorizada  │── Cancelar(motivo) ─────────────┐  │  │
   └──────┬───────┘                                 │  │  │
          │                                         │  │  │
          │ si todo cubierto con stock              │  │  │
          │ (CantidadDeCompra=0 en todas)           │  │  │
          ├─────────────────► ┌──────────┐          │  │  │
          │                   │  Cerrada │          │  │  │
          │                   └──────────┘          │  │  │
          │ si hay líneas con OC pendiente          │  │  │
          ▼                                         │  │  │
   ┌────────────┐                                   │  │  │
   │ EnSurtido  │── Cancelar(motivo) ───────────────┤  │  │
   └─────┬──────┘                                   │  │  │
         │ recepción de material                    │  │  │
         │                                          │  │  │
         │ todas las líneas con CantidadPendiente=0 │  │  │
         │ ──► Cerrada                              │  │  │
         │                                          │  │  │
         │ saldo no surtido (OC cerrada con menos)  │  │  │
         │ ──► evento informativo, sin transición   │  │  │
         ▼                                          ▼  ▼  ▼
                                  ┌──────────┬──────────┬──────────┐
                                  │ Cancelada│ Rechazada│ Eliminada│
                                  └──────────┴──────────┴──────────┘
                                          (terminales)
```

**Reglas de transición:**

- `Eliminar` permitida solo desde `Borrador` o `EnAutorizacion`. Sin
  reservas ni OCs que liberar.
- `Rechazar` solo desde `EnAutorizacion`. Acción del autorizador,
  preserva motivo y autor. Terminal — el solicitante crea otra RQ si
  insiste; mantiene métricas limpias ("tasa de rechazo").
- `Cancelar` desde `Autorizada` o `EnSurtido`. Libera reservas en
  Almacén, aborta OC borrador, propaga eventos.
- **No hay loop de re-autorización** (A12). Cuando la OC entrega menos
  de lo solicitado y se cierra con saldo, se emite
  `SaldoNoSurtidoEvent` (informativo) — el comprador decide qué hacer
  desde el módulo OC (cancelar, generar otra OC, etc.) sin volver a
  Requisiciones.
- `Cerrada` ≠ `Finalizada` del legacy: en este modelo cierra
  automáticamente cuando todas las líneas están cubiertas. No hay paso
  manual extra (era ruido del legacy).

---

## 6. Casos de uso (CQRS)

### 6.1 Comandos

> Convención: `XxxCommand` retorna un `Result<TResponse>` (Either-style).
> Cada comando tiene su `XxxValidator` (FluentValidation) y su
> `XxxHandler` (MediatR `IRequestHandler`).

| Comando | Trigger | Estado origen | Estado destino |
|---|---|---|---|
| `CrearRequisicionCommand` | UI captura | — | `Borrador` |
| `AgregarLineaCommand` | UI agrega línea | `Borrador` | `Borrador` |
| `ActualizarLineaCommand` | UI edita línea (estructural) | `Borrador` | `Borrador` |
| `EliminarLineaCommand` | UI quita línea | `Borrador` | `Borrador` |
| `ActualizarNotasLineaCommand` | UI edita solo el campo `Notas` de una línea | cualquier estado **no terminal** | sin cambio de estado |
| `EnviarAAutorizacionCommand` | UI "Enviar" | `Borrador` | `EnAutorizacion` |
| `AutorizarRequisicionCommand` | Autorizador aprueba | `EnAutorizacion` | `EnAutorizacion` (si faltan niveles) o `Autorizada` o `EnSurtido` (síncrono tras bifurcación) |
| `RechazarRequisicionCommand` | Autorizador rechaza con motivo | `EnAutorizacion` | `Rechazada` |
| `EliminarRequisicionCommand` | Solicitante/admin descarta pre-autorización | `Borrador` o `EnAutorizacion` | `Eliminada` |
| `CancelarRequisicionCommand` | Cualquier rol con permiso descarta post-autorización (libera reservas, aborta OC borrador) | `Autorizada` o `EnSurtido` | `Cancelada` |
| `RegistrarRecepcionCommand` | Evento del módulo OC al recibir material | `EnSurtido` | `EnSurtido` o `Cerrada` (si todas las líneas se cubrieron) |

> El handler de `AutorizarRequisicionCommand` evalúa la matriz de
> aprobación (asunción A1) tras agregar la `Autorizacion`. Si se cumplen
> todos los niveles requeridos, llama síncrono a `IReservarStockPort` +
> `IGenerarMovimientoSalidaPort` + `IGenerarSolicitudCompraPort` y
> transiciona el agregado a `Autorizada` o `EnSurtido` (según haya
> saldo de compra o no). Todo en la misma transacción de aplicación;
> si algo falla, falla la autorización.
>
> No hay `RegistrarCubrimientoCommand` separado — el cubrimiento se
> calcula y persiste **dentro** del handler de `AutorizarRequisicionCommand`
> (al hacer bifurcación) y de `RegistrarRecepcionCommand` (al recibir
> material).

### 6.2 Queries

| Query | Uso |
|---|---|
| `ObtenerRequisicionPorIdQuery` | Detalle |
| `ListarRequisicionesQuery` | Bandeja con filtros (estado, sucursal, departamento, fecha, requisitante) y paginación |
| `ListarPendientesAutorizacionQuery` | Bandeja del autorizador (filtra por `EnAutorizacion` y nivel pendiente) |
| `ListarSaldosPendientesAutorizacionQuery` | Bandeja del autorizador para `EnAutSaldo` |
| `PreviewCubrimientoQuery` | Preview read-only del cubrimiento estimado por línea en `EnAutorizacion` (PR-C; ver §8.1) — no reserva ni persiste |
| `ConsultarHistoricoQuery` | Auditoría / línea de tiempo de eventos |

Las queries devuelven `Read Models` (proyecciones planas), no agregados.

> **Nivel pendiente (PR-A).** `ListarPendientesAutorizacionQuery` expone, por
> RQ, el `NivelPendiente` (N1/N2) que falta firmar: lo deriva una función pura
> (`NivelPendienteDerivacion`) a partir de dos `EXISTS` sobre `Autorizaciones`
> resueltos en SQL (sin materializar la colección) — mismo patrón read-time que
> la situación de surtido (ADR-0043). El query admite además filtro server-side
> `nivelPendiente=1|2`, aplicado con `EXISTS`/`NOT EXISTS` **antes** del conteo y
> la paginación (total y página reflejan el filtro). No es un estado de la
> máquina: el `Estado` sigue siendo `EnAutorizacion`; solo se reusa el enum
> `NivelAutorizacion` (sin enum nuevo ni cambio de contrato).

---

## 7. Eventos

### 7.1 Eventos de dominio (in-process)

Emitidos por el agregado, manejados por `INotificationHandler<T>`.

- `RequisicionCreadaEvent`
- `LineaAgregadaEvent`, `LineaActualizadaEvent`, `LineaEliminadaEvent`
- `RequisicionEnviadaAAutorizacionEvent`
- `AutorizacionRegistradaEvent` (incluye `Nivel`)
- `MatrizAprobacionSatisfechaEvent` ← **dispara la bifurcación síncrona**
- `CubrimientoRegistradoEvent`
- `MaterialRecibidoEvent` (parcial o total)
- `SaldoNoSurtidoEvent` (informativo, A12 — emitido cuando la OC entrega menos de lo solicitado y queda saldo sin surtir)
- `RequisicionRechazadaEvent` (motivo, autor)
- `RequisicionEliminadaEvent` (motivo, autor)
- `RequisicionCanceladaEvent` (motivo, autor) — **dispara liberación de reservas y aborto de OC borrador**
- `RequisicionCerradaEvent`

### 7.2 Eventos de integración (Outbox → Service Bus, ADR 0009)

Convención de naming: `compras.requisicion.{accion}.v1` (versionado).

| Evento | Productor | Consumidores |
|---|---|---|
| `compras.requisicion.autorizada.v1` | Compras (al satisfacer matriz) | BI, Notificaciones |
| `compras.requisicion.cubrimientoRegistrado.v1` | Compras (post-bifurcación) | BI |
| `compras.requisicion.saldoNoSurtido.v1` | Compras (al recibir cierre de OC con saldo no entregado) | Notificaciones (al solicitante y comprador), BI |
| `compras.requisicion.rechazada.v1` | Compras | Notificaciones (al solicitante), BI |
| `compras.requisicion.eliminada.v1` | Compras | Notificaciones, BI |
| `compras.requisicion.cancelada.v1` | Compras | Almacén (liberar reserva), OC (abortar borrador), Notificaciones, BI |
| `compras.requisicion.cerrada.v1` | Compras | BI |

> **Nota**: Almacén y OC se invocan **síncronos vía puerto** dentro del
> handler de `AutorizarRequisicionCommand` (asunción A3). Los eventos
> de integración existen para consumidores **fuera del módulo Compras**
> (Notificaciones, BI), no para coordinar la bifurcación.

---

## 8. Contratos con otros módulos

### 8.1 Almacén de no-producción

> **Workstream almacén-por-línea PR1 (rollup por sucursal).** La clave del
> cubrimiento pasó de **almacén** a **sucursal**. El puerto
> `IConsultarStockPort` ahora expone `ConsultarPorSucursalAsync(SucursalId,
> ArticuloId)` y su adapter delega en
> `IAlmacenSaldoQueryPort.ConsultarDisponibilidadPorSucursalAsync` (suma las
> ubicaciones de **todos** los almacenes de la sucursal). El handler de
> Autorizar y el Preview consultan con `Requisicion.SucursalId` en lugar de
> `AlmacenDestinoId`. Hoy el resultado no cambia (MILLET opera 1 almacén
> activo por sucursal); queda correcto para sucursales con 2+ almacenes. Es la
> pieza que libera a la RQ de exigir un almacén destino (PR3). El bloque de
> abajo es el contrato original (F3-PR2, con reservas ya retiradas en ADR-0047
> PR4) y se conserva como historia de diseño.

**Compras → Almacén** (queries y comandos vía puertos):

```csharp
interface IConsultarStockPort {
    Task<DisponibilidadStock> ConsultarAsync(
        AlmacenId almacen, ArticuloId articulo, CancellationToken ct);
    // DisponibilidadStock { OnHand, ReservadoActivo, Disponible }
    // Disponible = OnHand - ReservadoActivo
}

interface IReservarStockPort {
    Task<ReservaResultado> ReservarAsync(
        AlmacenId almacen, ArticuloId articulo, decimal cantidad,
        RequisicionId origen, TimeSpan ttl,   // default 14 días
        CancellationToken ct);
    // ReservaResultado { ReservaId, CantidadReservada, ExpiraEn }
    // CantidadReservada puede ser < cantidad solicitada → bifurcación
    // marca el resto como CantidadDeCompra.
}

interface ILiberarReservaPort {
    Task LiberarAsync(ReservaId reserva, CancellationToken ct);
    // Idempotente. Se invoca en Cancelar/Eliminar y desde job de TTL.
}

interface IGenerarMovimientoSalidaPort {
    Task<MovimientoSalidaId> GenerarAsync(
        ReservaId reserva, CancellationToken ct);
    // Convierte la reserva en movimiento de salida real.
    // Decrementa OnHand y libera la reserva atómicamente.
}
```

**Reglas de la reserva:**

- La reserva NO decrementa el `OnHand` físico; va a una tabla de
  reservas activas en Almacén. `Disponible = OnHand - SUM(reservas_activas)`.
- TTL configurable, default **14 días**. Vencidas se liberan vía
  background job (ADR 0022).
- Si la RQ se elimina/cancela/rechaza, el handler invoca
  `ILiberarReservaPort` antes de cambiar el estado terminal.
- **No contradice ADR 0012** (concurrencia optimista del agregado): la
  concurrencia entre operaciones que tocan el mismo stock se maneja
  dentro del agregado Almacén, no aquí.

**Almacén → Compras** (eventos):
- `almacen.movimientoSalida.completado.v1` → Compras actualiza
  `Cubrimiento.CantidadDeAlmacen` (en este modelo síncrono A3, esto se
  hace en el mismo handler; el evento queda como auditoría).
- `almacen.reservaExpirada.v1` → si una reserva expiró sin haberse
  consumido (caso anómalo: bifurcación que nunca llegó a movimiento),
  Compras lo registra como incidencia.

> **Preview de cubrimiento (PR-C).** Antes de autorizar, las columnas de
> cubrimiento están en 0 (la bifurcación real corre al cumplirse la matriz),
> así que el autorizador no veía si había stock. El endpoint read-only
> `GET /requisiciones/{id}/cubrimiento-estimado` resuelve esto **reusando el
> `IConsultarStockPort` ya existente** (no introduce puerto ni dependencia
> nuevos): por línea consulta el disponible y reparte con la función pura
> `Cubrimiento.Repartir` (la **misma** que usa la bifurcación al autorizar →
> sin drift). **Invariantes:** NO invoca `IReservarStockPort` ni escribe las
> columnas — es una **estimación, no una garantía** (el stock es móvil: otras
> RQs reservan; la cantidad puede diferir al autorizar). Gated en
> `compras.requisiciones.leer`; solo aplica en `EnAutorizacion` (`aplica=false`
> en otros estados). El FE lo pinta con tratamiento visual **distinto** del
> cubrimiento real para que no se confunda estimado con reservado. No es una
> nueva frontera: la lectura Compras→Almacén vía `IConsultarStockPort` ya
> estaba sancionada por la bifurcación; el preview solo la expone por HTTP
> (por eso no requiere ADR).

### 8.2 Compras / Órdenes de Compra (mismo módulo)

Direct call (in-proc, mismo BC):

```csharp
interface IGenerarSolicitudCompraPort {
    Task<OrdenCompraId> GenerarBorradorAsync(
        RequisicionId origen,
        IReadOnlyList<LineaSaldo> saldoNoCubierto,
        CancellationToken ct);
}
```

OC notifica recepción de material via evento de dominio (mismo proceso).

### 8.3 Identidad

Lectura de claims y verificación de permisos vía middleware estándar
del ERP. Permisos relevantes (RBAC, ADR 0007):

- `compras:requisiciones:crear`
- `compras:requisiciones:editar`
- `compras:requisiciones:eliminar` (pre-autorización)
- `compras:requisiciones:cancelar` (post-autorización)
- `compras:requisiciones:autorizar:nivel1` (cubre inicial y saldo — asunción A15)
- `compras:requisiciones:autorizar:nivel2` (cubre inicial y saldo — asunción A15)
- `compras:requisiciones:rechazar`
- `compras:requisiciones:editar-de-otros-usuarios`
- `compras:requisiciones:seleccionar-requisitante` (delegación)
- `compras:requisiciones:ver-todos-departamentos` (sin restricción)

### 8.4 Notificaciones

Suscripción a eventos de integración. Plantillas declaradas en el
módulo Notificaciones (ADR 0026), no en Compras.

### 8.5 Contabilidad

**Lookup informativo**, sin validación bloqueante. Compras consume:
- Catálogo de cuentas contables (read-only).
- Catálogo de centros de costo (read-only).

No hay control presupuestal en v1.

### 8.6 Dependencias de plataforma pendientes

> Convención del proyecto en **ADR-0031**.

| Pieza | Ticket | NoOp en uso | Cómo se wirea cuando llegue |
|---|---|---|---|
| `CollaborationHub` SignalR (ADR-0012 Capa 2) | `<CollaborationHub>` | **Estado: backend completo + observable + UAT-ready** (Rev. 19, 2026-05-09). Sprint 1 (PR #74): bootstrap + skeleton + auth. Sprint 2 (PR #75): soft-lock manager + worker de expiración + `userPresence` por grupo de empresa + `GetPresence` + 5 behavior tests. Sprint 3a (PR #76): OTel meter custom + Bicep metric alerts + Action Group → email del owner + runbook §8 + dashboards §6. Sprint Buffer (PR #77): `SignalRHealthCheck` en `/health/ready` + UAT plan Bloque H (8 casos) + spot-check post-deploy con hub negotiate. `Requisicion` emite presence vía el hub. Capa 1 (`Version`/`IsConcurrencyToken`) sigue siendo la protección final. **FE NoOp en uso (UF0-PR2, 2026-05-09)**: stubs en `frontend/src/components/erp/collaboration/` — `useCollaboration(entidad, id)` retorna `{ viendo: [], editando: [] }`; `<CollaborationIndicator/>` renderiza `null`. Pantallas Compras (UF1-UF6) los invocan desde día 1; UF8-PR1 los reescribe con cliente SignalR real, sin tocar features. Comentarios `PLATFORM-TODO(<CollaborationHub>)` en ambos archivos. | **Pendiente sprint 3b (deferred)**: wireup real de `useCollaboration` + `<CollaborationIndicator/>` contra `ComprasHub` (heartbeat 30s, reconexión, expiración 90s, banner "está editando"). Sprint 3b ahora rebautizado como **UF8-PR1** del breakdown FE (Camino A confirmado por owner). **No es bloqueante** del UAT — los CP-70..CP-73 del Bloque H son ejecutables vía script manual hasta que el FE wireup llegue. El ticket se cierra cuando UF8-PR1 mergee y haya validación end-to-end con el FE. |
| Stubs cross-module Almacén + OC (F3-PR2) | `<StubsTeardown>` | 5 implementaciones `InMemory*` de los puertos en §8.1/§8.2 + tabla provisional `compras.oc_borrador_stub` + flag `Compras:UseStubs`. Activos en `Development`/`Test` (con guardia que falla el bootstrap si se activan en `Production`). Ejercitan F4-PR1+ sin requerir submódulo Almacén ni submódulo OC reales. **Estado: deferred sin plazo fijo** (Rev. 14): F7 no cierra este ticket; el cierre depende de que existan los submódulos consumidores. | Cuando exista el submódulo Almacén implementado y el submódulo OC implementado: registrar los adapters reales como impl de los puertos, borrar `backend/src/Compras/Infrastructure/Stubs/`, dropear la tabla `oc_borrador_stub` (migración), borrar la opción `Compras:UseStubs` y `ComprasStubsOptions`, borrar comentarios `PLATFORM-TODO(<StubsTeardown>)`. |

> `<CollaborationHub>` es habilitador transversal del ERP, fuera del
> scope del módulo Compras. Compras opera con `NoOp` mientras tanto y
> se conecta sin refactor cuando el ticket se cierra.
>
> `<StubsTeardown>` es interno a Compras: los stubs existen para
> desbloquear la bifurcación stock-aware (Fase 4) sin bloquearse en
> submódulos hermanos que aún no se implementaron. Originalmente se
> previó cerrar este ticket en Fase 7, pero el cierre quedó **diferido
> sin plazo fijo** (Rev. 14): no por falta de seguimiento, sino porque
> los submódulos Almacén y OC reales —únicos que pueden reemplazar los
> stubs con adapters productivos— no existen todavía. El cierre se
> ejecutará en el módulo y PR donde lleguen esos adapters.
>
> El ticket `<Outbox>` (Outbox + Service Bus, ADR-0009) se **cerró en
> F6-PR4**: la infra real de outbox transaccional + worker Service Bus
> reemplazó al `NoOpIntegrationEventPublisher` (borrado). Compras
> publica 6 integration events versionados v1; el contrato vive en
> [docs/integraciones/compras-eventos.md](../../integraciones/compras-eventos.md).

---

## 9. API REST

Versionado URL (`/api/v1`, ADR 0021). Errores en Problem Details
(ADR 0010). Idempotencia con header `Idempotency-Key` en POST que crean
recursos (ADR 0020).

| Verbo | Ruta | Acción |
|---|---|---|
| `POST` | `/api/v1/compras/requisiciones` | Crear (Borrador) |
| `GET` | `/api/v1/compras/requisiciones/{id}` | Detalle |
| `GET` | `/api/v1/compras/requisiciones` | Listar (filtros, paginación) |
| `PATCH` | `/api/v1/compras/requisiciones/{id}` | Editar cabecera (solo `Borrador`) |
| `POST` | `/api/v1/compras/requisiciones/{id}/lineas` | Agregar línea |
| `PATCH` | `/api/v1/compras/requisiciones/{id}/lineas/{lineaId}` | Editar línea (estructural, solo en `Borrador`) |
| `PATCH` | `/api/v1/compras/requisiciones/{id}/lineas/{lineaId}/notas` | Editar solo `notas` (cualquier estado no terminal) |
| `DELETE` | `/api/v1/compras/requisiciones/{id}/lineas/{lineaId}` | Quitar línea |
| `POST` | `/api/v1/compras/requisiciones/{id}/transmitir` | Enviar a autorización |
| `POST` | `/api/v1/compras/requisiciones/{id}/autorizaciones` | Registrar autorización (`{nivel, tipo, notas?}`) |
| `POST` | `/api/v1/compras/requisiciones/{id}/rechazar` | Autorizador rechaza (`{motivo}`) |
| `DELETE` | `/api/v1/compras/requisiciones/{id}` | Eliminar pre-autorización (`{motivo}`) |
| `POST` | `/api/v1/compras/requisiciones/{id}/cancelar` | Cancelar post-autorización (`{motivo}`) |
| `GET` | `/api/v1/compras/requisiciones/{id}/historico` | Línea de tiempo |
| `GET` | `/api/v1/compras/requisiciones/pendientes-autorizacion` | Bandeja del autorizador (incluye saldos) |

Todas las respuestas exponen `ETag` derivado de `Version` para
concurrencia optimista (ADR 0012). Las mutaciones requieren
`If-Match: <etag>`. **Conflicto de versión retorna 412 Precondition
Failed con Problem Details** (no 500). El frontend debe recargar la RQ
y reintentar; UX clara con mensaje "alguien más actualizó esta RQ,
recarga".

> **v1.1 — bulk operations** (asunción A13): se anticipa endpoint
> `POST /api/v1/compras/requisiciones/autorizaciones-batch` para
> aprobar varias desde la bandeja del autorizador. No en v1, pero el
> diseño no lo bloquea.

---

## 10. Esquema de persistencia (PostgreSQL)

Esquema: `compras`. Migraciones EF Core (ADR 0005).

```sql
-- Cabecera
compras.requisiciones (
    requisicion_id          uuid          PK,
    empresa_id              uuid          NOT NULL,
    folio                   varchar(20)   NOT NULL,
    folio_anio              smallint      NOT NULL,
    clasificacion           smallint      NOT NULL,
    sucursal_id             uuid          NOT NULL,
    departamento_id         uuid          NOT NULL,
    almacen_destino_id      uuid          NOT NULL,
    requisitante_id         uuid          NOT NULL,
    creador_id              uuid          NOT NULL,    -- quien capturó la fila
                                                        -- (puede ≠ requisitante_id si
                                                        -- usó "seleccionar requisitante";
                                                        -- semántico, no redundante con
                                                        -- created_by del framework de
                                                        -- auditoría — ese es el HTTP user)
    descripcion             varchar(500),
    prioridad               smallint      NOT NULL,
    fecha_solicitud         timestamptz   NOT NULL,
    fecha_entrega_deseada   date,
    proveedor_sugerido_id   uuid,
    estado                  smallint      NOT NULL,
    motivo_terminacion_id   uuid,                            -- FK a compras.motivos_rechazo (RA5)
    motivo_terminacion_texto varchar(500),                    -- requerido si motivo permite texto libre
    actor_terminacion_id    uuid,                            -- quien la terminó
    fecha_terminacion       timestamptz,
    version                 int           NOT NULL DEFAULT 0  -- IsConcurrencyToken (ADR 0012 Capa 1)
    -- auditoría vía framework, no columnas duplicadas
)
UNIQUE (empresa_id, folio_anio, folio);
INDEX (empresa_id, estado, fecha_solicitud DESC);
INDEX (empresa_id, departamento_id, estado);
INDEX (empresa_id, requisitante_id);

-- Detalle
compras.requisicion_lineas (
    linea_id                uuid          PK,
    requisicion_id          uuid          NOT NULL FK → requisiciones,
    posicion                smallint      NOT NULL,
    articulo_id             uuid          NOT NULL,
    cantidad                numeric(18,5) NOT NULL,
    unidad_medida           varchar(20)   NOT NULL,
    precio_estimado         numeric(18,6) NOT NULL,
    moneda                  char(3)       NOT NULL DEFAULT 'MXN',
    cuenta_contable_id      uuid,
    centro_costo_id         uuid,
    proyecto                varchar(200),
    fecha_requerida         date,
    notas                   varchar(500),
    cant_de_almacen         numeric(18,5) NOT NULL DEFAULT 0,
    cant_de_compra          numeric(18,5) NOT NULL DEFAULT 0,
    cant_recibida           numeric(18,5) NOT NULL DEFAULT 0
)
UNIQUE (requisicion_id, posicion);

-- Autorizaciones
compras.requisicion_autorizaciones (
    autorizacion_id         uuid          PK,
    requisicion_id          uuid          NOT NULL FK → requisiciones,
    nivel                   smallint      NOT NULL CHECK (nivel IN (1, 2)),
    usuario_id              uuid          NOT NULL,
    fecha_hora              timestamptz   NOT NULL,
    notas                   varchar(500)
)
UNIQUE (requisicion_id, nivel);
INDEX (usuario_id, fecha_hora DESC);

-- Secuencia de folios por sucursal+año
compras.folio_secuencias (
    empresa_id              uuid,
    sucursal_id             uuid,
    anio                    smallint,
    siguiente               int           NOT NULL,
    PRIMARY KEY (empresa_id, sucursal_id, anio)
);
```

Concurrencia: columna `version` (int) configurada como
`IsConcurrencyToken()` en el `BaseDbContext` del ERP, alineada con
ADR 0012 Capa 1. EF Core la incrementa automáticamente en cada
`SaveChanges` y lanza `DbUpdateConcurrencyException` cuando dos
transacciones conflictúan; el middleware de errores la traduce a
HTTP 409 (ADR 0010). **No usar columnas `xmin` específicas de
PostgreSQL** — rompe la abstracción del framework.

### 10.2 Decisiones de normalización

El esquema de §10.1 toma cinco decisiones que conviene hacer explícitas
porque cada una tiene trade-offs.

#### 10.2.1 Enums en código vs tablas de lookup

**Decisión: enum en código C#**, persistido como `smallint`, validado con
`CHECK` constraint en la columna. **Sin tablas de lookup en BD.**

Aplica a:

| Columna | Valores | Domicilio del enum |
|---|---|---|
| `estado` | 8 valores (Borrador, EnAutorizacion, Autorizada, EnSurtido, Cerrada, Cancelada, Rechazada, Eliminada) | `EstadoRequisicion` (state machine) |
| `clasificacion` | 4 valores (Servicio, OrdenCompra, MateriaPrima, Pinturas) | `Clasificacion` |
| `prioridad` | 3 valores (Normal, Alta, Baja) | `Prioridad` |
| `nivel` | 2 valores (Nivel1, Nivel2) | `NivelAutorizacion` |
| `tipo` (autorización) | 2 valores (Inicial, Saldo) | `TipoAutorizacion` |

**Razón:**
- Son valores fijos del dominio, no editables por usuarios desde UI.
- Una tabla lookup añade joins y migraciones por cada nuevo valor.
- El enum vive en el agregado, donde aplican las reglas (state machine).

**Ejemplo:**
```sql
estado smallint NOT NULL CHECK (estado BETWEEN 0 AND 7),
```

**No aplica** a catálogos editables desde UI (proveedores, artículos,
sucursales, departamentos, almacenes, cuentas contables, centros de
costo). Esos sí son tablas (en su módulo dueño).

#### 10.2.2 Sin snapshots de catálogo

**Decisión: solo FK lógica, sin columnas de snapshot.** Las descripciones
se obtienen por join contra el catálogo vigente.

```sql
-- en compras.requisicion_lineas
articulo_id              uuid          NOT NULL,
proveedor_sugerido_id    uuid,
cuenta_contable_id       uuid,
centro_costo_id          uuid,
-- sin columnas *_snapshot
```

**Razón:**

- **No se migra histórico legacy** (decisión cerrada en A8) — la RQ que
  el legacy storeaba con snapshots se queda en el portal viejo.
- **Los catálogos son propiedad del ERP nuevo** (proveedores y artículos
  vienen de SAP one-shot y luego los administra Compras). Los renombres
  serán raros y trazables vía la auditoría del catálogo (ADR 0008).
- **CLAUDE.md**: "Don't design for hypothetical future requirements."
  Si dentro de dos años un nombre cambia y alguien quiere ver la RQ tal
  cual fue, agregar snapshots es una migración aditiva sin pérdida de
  datos. No vale la pena pagar el costo de columnas extra hoy.
- Excepción razonable: `precio_estimado` permanece en la línea porque
  **no es snapshot de catálogo** — es la estimación del solicitante,
  diferente del precio del catálogo (ver §10.2.5).

**Decisión del cliente:** los artículos se seleccionan **solo desde el
catálogo** (`articulo_id` siempre requerido, sin texto libre alterno).
Si el solicitante necesita aclarar algo del artículo, usa el campo
`notas` (que sí es editable durante el ciclo, ver §4.2).

#### 10.2.3 FKs físicas vs Guids lógicos cross-schema

**Decisión: solo Guids lógicos cross-schema**, sin FK física en BD.

Aplica a todas las referencias a otros módulos:
- `empresa_id`, `requisitante_id`, `creador_id`, `usuario_id` → módulo
  Identidad.
- `sucursal_id`, `departamento_id` → módulo Administración (catálogo
  cross-empresa en schema `compartido`: `sucursales`, `departamentos`,
  `sucursal_departamentos`). **PR-A2 (Rev. 21):** `CrearRequisicionHandler`
  valida via `ISucursalDepartamentoReadPort.OperaAsync(sucursal_id,
  departamento_id)` que la asignación N:M esté Activa en
  `compartido.sucursal_departamentos`. Si no, levanta 422
  `RQ_DEPTO_NO_OPERA_EN_SUCURSAL`.
- `almacen_destino_id` → módulo Almacén. **PR-A2 (Rev. 21):** el handler
  resuelve via `IAlmacenReadPort.ObtenerAsync(almacen_id)` y valida que
  el almacén exista (404 `ALMACEN_NO_ENCONTRADO`) y que su `SucursalId`
  coincida con `RQ.SucursalId` (422 `RQ_ALMACEN_NO_PERTENECE_A_SUCURSAL`).
- `articulo_id`, `proveedor_sugerido_id` → catálogo cross-empresa en
  schema `compartido` (F7-PR1: `compartido.articulos` /
  `compartido.proveedores`). FK **lógica** (Guid sin FK física) por
  la regla cross-schema. Validación cross-table (existe + estatus
  Activo) en Application layer: los handlers de Compras chequean
  via `CompartidoDbContext` antes de mutar el agregado.
- `cuenta_contable_id`, `centro_costo_id` → módulo Contabilidad.

**Razón:**
- Los módulos son fronteras. Una FK física entre esquemas ata el
  release y las migraciones de dos módulos.
- La integridad referencial cross-module se valida en la capa de
  aplicación (al ejecutar el comando), no en BD.
- PostgreSQL permite FK cross-schema, pero el costo arquitectónico
  supera el beneficio.

**Sí lleva FK física** cuando ambas tablas viven en el mismo esquema
(intra-módulo). Por ejemplo, `requisicion_lineas.requisicion_id →
requisiciones.requisicion_id` con `ON DELETE CASCADE`.

#### 10.2.4 Catálogos cross-empresa (en `compartido`)

`Proveedores` y `Articulos` son catálogos **cross-module** (los
consumen Compras, Cuentas por Pagar, Activos Fijos cuando lleguen).
Por eso viven en `compartido` (junto a `Empresas`, `Monedas`), no en
`compras`. Decidido en F7-PR1 (Rev. 13).

- `compartido.proveedores` — clave (UNIQUE), razón social, RFC,
  tipo persona (Moral/Física), condiciones de pago en días, moneda
  preferida (FK lógica `compartido.monedas`), email, teléfono,
  estatus (Activo/Inactivo/EnRevisión).
- `compartido.articulos` — clave (UNIQUE), nombre, unidad de medida
  default, **naturaleza** (Estandar/Servicio/Critico/Riesgo, default
  Estandar; alimenta la matriz de aprobación A1), categoría, precio
  referencia (Money opcional), estatus.

**Población:**
- En `Production` las tablas arrancan vacías. Se popularán cuando el
  cliente entregue el export real desde SAP (deferred a un PR
  posterior, post-MVP).
- En `Development`/`Staging` un `IHostedService`
  (`CatalogosTestSeedHostedService`) carga 5 proveedores y 10
  artículos de prueba al arranque (idempotente). Se autoexcluye en
  Production.

**Campos compra-specific futuros**: cuando se necesiten campos que
solo Compras usa (ej. matriz de aprobación con condiciones de pago
distintas a las de CxP), se agrega una tabla extension
`compras.proveedores_compra` con FK 1:1 al core. Por ahora todo lo
que Compras necesita está en `compartido`.

**Unidad de medida**: se persiste como **string** en la línea (no hay
tabla `compras.unidades_medida` separada). Es atributo del artículo en
el catálogo, pero se copia al string de la línea por si más adelante se
permite seleccionar UM alterna en algún caso. Si se requiere catálogo
formal (ej. para conversiones), se introduce en v2 sin romper datos.

#### 10.2.5 Datos denormalizados deliberadamente conservados

| Campo | Por qué se mantiene |
|---|---|
| `folio` (string formateado) en cabecera | Es el identificador legible para el usuario, calculado una vez y nunca recalculado |
| `precio_estimado` en línea | Es el precio que **el solicitante estimó al capturar**, no el del catálogo vigente. Los precios reales viven en la OC. |
| `motivo_terminacion`, `actor_terminacion_id`, `fecha_terminacion` | Snapshot del cierre — auditable sin necesidad de joinear con auditoría |

### 10.3 Esquema actualizado de líneas

Reemplaza `compras.requisicion_lineas` de §10.1 (sin snapshots):

```sql
compras.requisicion_lineas (
    linea_id                  uuid          PK,
    requisicion_id            uuid          NOT NULL FK → requisiciones,
    posicion                  smallint      NOT NULL,
    articulo_id               uuid          NOT NULL,    -- FK lógica a compras.articulos
    cantidad                  numeric(18,5) NOT NULL CHECK (cantidad > 0),
    unidad_medida             varchar(20)   NOT NULL,
    precio_estimado           numeric(18,6) NOT NULL,
    moneda                    char(3)       NOT NULL DEFAULT 'MXN'
                                            CHECK (moneda ~ '^[A-Z]{3}$'),
    cuenta_contable_id        uuid,
    centro_costo_id           uuid,
    proyecto                  varchar(200),               -- texto libre
    fecha_requerida           date,
    notas                     varchar(500),
    cant_de_almacen           numeric(18,5) NOT NULL DEFAULT 0,
    cant_de_compra            numeric(18,5) NOT NULL DEFAULT 0,
    cant_recibida             numeric(18,5) NOT NULL DEFAULT 0,
    CHECK (cant_de_almacen + cant_de_compra <= cantidad),
    CHECK (cant_recibida <= cant_de_compra)
)
UNIQUE (requisicion_id, posicion);
```

### 10.4 Convenciones generales

- **Snake_case** en columnas y tablas (convención PostgreSQL).
- **`uuid`** para todas las identidades (no `bigint identity`). Generación
  en aplicación con `Guid.CreateVersion7()` o equivalente para mejor
  ordenamiento por inserción.
- **`timestamptz`** siempre (UTC en BD, ADR 0013).
- **`numeric(18,5)`** para cantidades, **`numeric(18,6)`** para precios
  unitarios, **`numeric(15,2)`** para totales — consistente con el
  legacy y con tipo `Money` del framework (ADR 0014).
- **`varchar`** con tope explícito; nunca `text` salvo justificación.
- **`NOT NULL`** por defecto; `NULL` solo cuando el dominio explícitamente
  permite ausencia.
- **`CHECK` constraints** para invariantes simples que no involucran
  joins.

---

## 11. Migración de datos del legacy

### 11.1 Qué se importa (one-shot)

| Origen legacy | Destino ERP | Notas |
|---|---|---|
| `CA_PRIORIDADES` | enum `Prioridad` en código (3 valores) | datos seed |
| `DERECHOS` (filas relevantes a mod 3 del legacy) | mapeo a permisos RBAC | 26 acciones del módulo Requisiciones |
| `DERECHOS_USUARIOS` (filtrado) | asignaciones RBAC | filtrar a usuarios activos en Entra |
| Catálogo de proveedores (export desde SAP) | `compras.proveedores` | export inicial one-shot |
| Catálogo de artículos no-prod (export desde SAP) | `compras.articulos` | export inicial one-shot — incluye atributo `Naturaleza` (ver A1) |
| **`DO_RQ` filtrado** (ver §11.3.b) | `compras.requisiciones` | **solo las RQs con OC generada y material pendiente** |
| **`DO_RQ_DET` correspondiente** | `compras.requisicion_lineas` | con `Cubrimiento` reconstruido desde `UNIDADES_TRANSMITIDAS`, `UNIDADES_MOV_INVENTARIO` |
| **`DO_RQ_AUT` correspondiente** | `compras.requisicion_autorizaciones` | autorizaciones registradas, sin `Tipo` (saldos no se modelan, ver A12) |
| **`PEDIDOS` correspondientes** | módulo OC del ERP nuevo | OCs vivas asociadas |

> **Ojo**: departamentos, sucursales, almacenes y clasificaciones **NO
> se importan del legacy** (RA9). El ERP nuevo los diseña fresh; el
> mapeo `legacy → nuevo` durante la migración de RQs traduce los IDs.

### 11.2 Qué NO se importa

- RQs **sin OC generada** (estados Borrador / EnAutorizacion /
  Autorizada sin bifurcación legacy). Se cancelan en el legacy y se
  rehacen en el ERP nuevo si todavía interesan.
- RQs **ya cerradas** (`DO_RQ.ESTATUS_ID = 6/11/etc.`) — quedan en el
  legacy read-only para consulta histórica.
- `DO_RQ_LIGAS`, `DO_RQ_DET_LIGAS` — vestigiales.
- `PEDIDOS_V2`, `PEDIDOS_DET_V2` — V2 nunca se implementó.
- `RQ_PERIODOS`, `RQ_MOVIMIENTOS` — control presupuestal deprecado.
- `NOTIFICACIONES`, `BLOQUEOS`, `SESIONES` — estado operativo
  irrelevante.
- `USUARIOS` (incluye contraseñas en plano e imágenes en BD) —
  autenticación 100% Entra ID.
- `CA_DEPARTAMENTOS`, `CA_SUCURSALES`, `CA_ALMACENES`,
  `CA_CLASIFICACIONES` — el ERP nuevo los diseña fresh (RA9, RA10).

### 11.3 Estrategia

#### 11.3.a Catálogos

Scripts idempotentes (T-SQL `SELECT` → transformación → INSERT en
PostgreSQL) con `ON CONFLICT DO NOTHING`.

#### 11.3.b RQs con OC pendiente — script de migración con estado vivo

Criterio de selección en el legacy (a refinar con cliente):

```sql
-- Pseudocódigo de la query de selección
SELECT * FROM DO_RQ
WHERE ESTATUS_ID IN (9, 10, 7, 8)   -- OCC, SP, ASP, ASC
   OR (ESTATUS_ID = 5 AND COMPRA IS NOT NULL)  -- AUTCOM con pedido
ORDER BY FECHA_HORA_CREACION;
```

Lo que migra por cada RQ seleccionada:

1. La cabecera → `compras.requisiciones` con estado mapeado:
   - Legacy 9 OCC → nuevo `EnSurtido`
   - Legacy 10 SP → nuevo `EnSurtido`
   - Legacy 7 ASP → nuevo `EnSurtido` (sin re-aut, A12)
   - Legacy 8 ASC → nuevo `EnSurtido`
   - Legacy 5 AUTCOM con pedido → nuevo `EnSurtido` (la bifurcación ya
     ocurrió en legacy)
2. Las líneas → `compras.requisicion_lineas` con `Cubrimiento`:
   - `cant_de_almacen` ← `UNIDADES_MOV_INVENTARIO`
   - `cant_de_compra` ← `UNIDADES_TRANSMITIDAS`
   - `cant_recibida` ← *(a calcular: cant. recibidas registradas en SAP)*
3. Las autorizaciones → `compras.requisicion_autorizaciones`
   (descartando las de tipo Saldo del legacy, ya que ese concepto se
   eliminó).
4. Los `PEDIDOS` correspondientes → módulo OC del ERP nuevo (no es
   responsabilidad de este documento; coordinar con OC).

#### 11.3.c Mapeo temporal de IDs

Tabla puente `migracion.legacy_map` guarda
`(tipo_entidad, legacy_id, nuevo_id)` durante el proceso. Necesaria
para resolver:

- `USUARIO_ID` legacy → Entra ObjectId
- `DEPARTAMENTO_ID` legacy → `departamento_id` del ERP nuevo (el ERP
  los crea fresh; durante migración el cliente provee el mapping)
- `SUCURSAL_ID`, `ALMACEN_ID` → similar
- `CLAVE_ARTICULO` legacy → `articulo_id` del ERP nuevo (resuelto por
  match contra el catálogo importado de SAP)
- `CLAVE_PROV` legacy → `proveedor_id` del ERP nuevo (idem)

La tabla se descarta tras estabilizar.

#### 11.3.d Acceso al histórico legacy

Las RQs ya cerradas y las RQs sin OC que se cancelaron quedan en el
**portal viejo en modo read-only** durante el periodo de coexistencia.
No hay API ni vista en el ERP nuevo que lea esa data.

#### 11.3.e Ejecución

1. Pre-cutover: importar catálogos y precargar `legacy_map`.
2. Día del cutover (ventana de mantenimiento): correr el script de
   selección → migrar RQs vivas → activar el ERP nuevo → poner el
   legacy en read-only.
3. Validación post-cutover: spot-check de N RQs migradas (cabecera,
   líneas, cubrimiento, autorizaciones).

---

## 12. Pendientes para fase de implementación

### Pendientes con cliente (priorizados)

- [ ] **Refinar A1**: cuántas naturalezas en total, "aprobador
  adicional" para `Riesgo` (¿N2 especializado o 3er nivel?), suplencias
  de jefe almacén, auto-aprobación cuando el solicitante es jefe del
  depto, periodicidad del umbral por departamento (ver §3.bis.2).
- [ ] **Confirmar lista seed** de motivos de rechazo (§3.bis.3).
- [ ] **Confirmar mapeo legacy → ERP nuevo** de IDs de departamentos,
  sucursales, almacenes (RA9: el ERP los crea fresh, el cliente provee
  el mapping para la migración de RQs vivas).
- [ ] **Confirmar mapeo de estados** legacy → nuevo para la migración
  de RQs vivas (§11.3.b).
- [ ] Confirmar A13 (bulk en v1.1), A14 (read models EF directo). A15
  ya no aplica (el `Tipo` de autorización se eliminó).

### Decisiones técnicas pendientes con el equipo

- [ ] Definir el contrato exacto del módulo Almacén (puertos en §8.1)
  con quien diseñe Almacén — especialmente la semántica de `Reserva` y
  el job de saneamiento de TTL.
- [ ] Definir el contrato exacto con OC (`IGenerarSolicitudCompraPort`)
  con quien diseñe el agregado OC, incluyendo el flujo de migración
  de `PEDIDOS` legacy (§11.3.b).
- [ ] Acordar mapeo de los 26 derechos legacy de Requisiciones a la
  lista RBAC final (§8.3) — incluyendo los nuevos roles
  `JefeAlmacen`, `JefeDepartamento`, `AutorizadorN2` que surgen de A1.
- [ ] Preparar scripts de exportación de proveedores y artículos desde
  SAP (one-shot, ver §11.1) — **incluir el atributo `Naturaleza`** por
  artículo (Estandar/Servicio/Critico/Riesgo) — coordinar con el
  cliente cómo se determina esa clasificación.
- [ ] Definir plantillas de notificación (módulo Notificaciones).
- [ ] Definir UI/UX de la bandeja del autorizador con bulk en mente
  (asunción A13) — aunque el endpoint bulk se entregue en v1.1.

---

## 13. Cambios respecto a versiones previas

### Rev. 21 — Validación cross-table sucursal/depto/almacén en CrearRequisicion (PR-A2, 2026-06-02)

- **Cierre de la trilogía A1/A3/A2** (PR-A1 #333 modelo, PR-A3 #341
  frontend, PR-A2 backend enforcement). Hasta ahora el handler aceptaba
  cualquier `(SucursalId, DepartamentoId, AlmacenDestinoId)` opaco; el
  frontend filtraba pero un request manual / replay se colaba. PR-A2
  activa la validación cross-table en `CrearRequisicionHandler`.
- **Puertos nuevos** en `Compras.Domain.Ports.*`:
  - `ISucursalDepartamentoReadPort.OperaAsync(sucursalId, deptoId)` —
    bool true sólo si `compartido.sucursal_departamentos` tiene la fila
    en `Activo`.
  - `IAlmacenReadPort.ObtenerAsync(almacenId)` →
    `AlmacenLectura(Id, Clave, SucursalId, EsActivo)` desde
    `almacen.almacenes`.
- **Adapters** en `Compras.Infrastructure.PublicAdapters`
  (`SucursalDepartamentoReadAdapter` lee `CompartidoDbContext`;
  `AlmacenReadAdapter` lee `AlmacenDbContext`). Viven en Compras porque
  Almacen y Compartido no pueden referenciar Compras (regla anti-ciclo
  del csproj). Patrón ya establecido por `AlmacenEntregasReadAdapter`/
  `AlmacenStockReadAdapter`.
- **Códigos de error nuevos**:

  | Código | HTTP | Cuándo |
  |---|---|---|
  | `ALMACEN_NO_ENCONTRADO` | 404 | `AlmacenDestinoId` no existe |
  | `RQ_ALMACEN_NO_PERTENECE_A_SUCURSAL` | 422 | `Almacen.SucursalId` ≠ `RQ.SucursalId` |
  | `RQ_DEPTO_NO_OPERA_EN_SUCURSAL` | 422 | Combinación inexistente o Inactiva |

- **`EditarCabeceraRequisicionHandler` no se toca** — su command no
  acepta `SucursalId`/`DepartamentoId`/`AlmacenDestinoId` (inmutables
  tras crear). Si la decisión cambia, PR propio.
- **Performance**: ~2ms overhead por POST. Las dos lecturas se
  paralelizan con `Task.WhenAll`. **Sin cache** — decisión reversible si
  emerge throughput issue (ver comentario en
  `SucursalDepartamentoReadAdapter`).
- **Notas inline en §10.2.3** actualizadas: las columnas `sucursal_id`,
  `departamento_id` y `almacen_destino_id` de `compras.requisiciones`
  ahora declaran su validación cross-table.
- **Tests**: 14 fixtures migradas a `TestComprasFixtures` (constantes
  `SucursalMid`/`DeptoCompras`/`AlmacenMidGeneral` apuntando al seed
  canónico). `SucursalIdFija = 00000003-0001-*` (GUID legacy erróneo
  copiado de los permisos canónicos) eliminada. 4 tests nuevos de
  validación en `Compras.IntegrationTests/Validacion/`.
- **Mitigación de colisión de folios**: nuevo
  `ComprasTestSeedHostedService` (non-Production) adelanta
  `(empresa-bootstrap, MID-canónica, 2026, siguiente=10001)` para
  evitar choques con folios `MID2026-XXXX` legacy (143 RQs históricas
  en DBs locales de devs con la sucursal-fija deprecada).
- **ADR-0030 addenda 2026-06-02** registra la decisión arquitectónica.
- **Hallazgo lateral A — flakiness preexistente en `Crear_Genera_Folios_Consecutivos_Para_Misma_Empresa_Sucursal_Anio`** (`Api.IntegrationTests/Compras/RequisicionesEndpointsTests.cs`):
  el test asume que dos POST consecutivos en una misma class producen
  folios `(nA, nA+1)`. xUnit corre classes en paralelo por default, así
  que cuando este archivo se ejecuta junto a otro que también crea RQs
  con la misma sucursal (p. ej. `RechazarEliminarEndpointsTests`), una
  RQ del segundo archivo se cuela entre `first` y `second` y rompe el
  assert. Verificado empíricamente en main pre-PR-A2 (3 de 5 corridas
  fallan al ejecutar los 2 archivos juntos); cada uno aislado pasa
  13/13. PR-A2 NO introduce ni exacerba el flag (la sucursal pasa de
  `00000003-0001-*` a `MID` canónica, misma estructura compartida).
  Documentado inline con comentario `// FLAKY:` sobre el test. Arreglo
  proper (sucursal ad-hoc por test o `[CollectionDefinition]` que
  serialice los pares conflictivos) queda fuera de scope de PR-A2.
  El CI no se ve afectado porque sólo corre `*UnitTests`, no
  `IntegrationTests`.
- **Hallazgo lateral B — regresión pre-existente en `Autorizar_Nivel1_FailOpen_StockTotal_TransicionaA_Cerrada_Retorna_204`** (`Api.IntegrationTests/Compras/AutorizacionesEndpointsTests.cs`):
  el test asume que `IConsultarStockPort` responde
  `Disponible >= Cantidad` (lo que `TestAssemblyInit` intentaba forzar
  via `Compras__Stubs__Stock__DefaultRatio=1.0`), y bajo ese supuesto
  la RQ debería transicionar a `Cerrada`. Pero desde PR #293/#301 el
  stub config-driven se reemplazó por `AlmacenStockReadAdapter`
  (lectura real de `almacen.saldos_inventario`), que ignora el env
  var. Sin saldos sembrados para `(ArticuloSeedId, AlmacenMidGeneral)`,
  `Disponible=0` → la RQ pasa a `EnSurtido`, no `Cerrada`. **No es
  flaky — es determinísticamente rojo:** verificado en main pre-PR-A2
  con 3/3 corridas aisladas fallando. Causa raíz documentada; PR-A2
  NO la introduce (estado actual sólo cambia *qué* `AlmacenDestinoId`
  se usa, no si tiene saldos sembrados). Dos remediaciones posibles,
  ambas fuera de scope: (a) seedear `almacen.saldos_inventario` con
  stock para el par usado, o (b) eliminar el test (otras suites cubren
  el flujo "stock total"). Documentado aquí (no se arregla en PR-A2)
  para mantener disciplina de scope: el test rompe por bug previo, no
  por la validación de A2. Follow-up dedicado.
- **Hallazgo lateral C — patrón sistémico stub-vs-adapter en suites de `Compras.IntegrationTests/`** (descubierto durante C.3 al migrar fixtures, ampliado en C.4):
  comparte causa raíz técnica con el hallazgo B (regresión del PR
  #293/#301 reemplazó stub config-driven por `AlmacenStockReadAdapter`
  real, que ignora `Compras__Stubs__Stock__DefaultRatio`), pero
  conceptualmente es distinto: B es un test obsoleto aislado, C es un
  **patrón sistémico** que dejó 8 tests rotos en 4 archivos (3 de
  `Bifurcacion/` + 1 de `Integration/`). Detectado tras grep
  post-migración por
  `DefaultRatio`/`Compras__Stubs__Stock`/`IConsultarStockPort` y
  verificado empíricamente en main pre-PR-A2 (cada test aislado, 3/3
  corridas fallidas por test):
  `Bifurcacion/BifurcacionEndpointsTests` 3/5 rojos
  (`Autorizar_StockTotalCubreTodo_TransicionaA_Cerrada_SinOcBorrador`,
  `Autorizar_StockMixto_TransicionaA_EnSurtido_ConSaldoYOcBorrador`,
  `Autorizar_FallaEnReservar_RollbackTotal_Y_RqQuedaEnAutorizacion`);
  `Bifurcacion/CancelarEndpointsTests` 3/9 rojos
  (`Cancelar_DesdeCerrada_Retorna_422`,
  `Cancelar_DesdeEnSurtido_Retorna_204_LiberaReservas_Y_BorraOcBorrador`,
  `Cancelar_FallaEnLiberarReserva_Rollback_Y_RqQuedaEnSurtido`);
  `Bifurcacion/TwoLevelAuthEndpointsTests` 1/2 rojo
  (`DosNiveles_StockTotal_N1MantieneEnAutorizacion_N2TransicionaA_Cerrada`);
  `Integration/IntegrationEventsE2ETests` 1/7 rojo
  (`Autorizar_StockTotal_Persiste_AutorizadaEvent_Y_CerradaEvent`,
  asume que el flujo stock-total emite `compras.requisicion.cerrada.v1`
  en outbox; sin saldos sembrados sólo emite `autorizada.v1`).
  Cada uno marcado inline con `// REGRESIÓN PRE-EXISTENTE: stub→adapter
  real (PR #293/#301). Ver doc 01 §13 Rev. 21 hallazgo lateral C`
  (apunta a C porque es el alcance sistémico; la explicación técnica
  del bug subyacente está en B). Post-migración a
  `TestComprasFixtures` el conteo no cambia: mismos 8 tests rojos,
  ninguno nuevo introducido por PR-A2. **Por qué separar de B:** B se
  arregla con 2 líneas (seedear `saldos_inventario` para un par
  `(ArticuloSeedId, AlmacenMidGeneral)` o eliminar el test); C es un
  patrón que requiere auditar **todos** los tests que dependen de stock
  total post-autorización, decidir estrategia consistente (seedear
  stock real fixture-wide, refactorizar para inyectar
  `IConsultarStockPort` controlable por test, o eliminar el bloque de
  cobertura) y posiblemente eliminar también `StubsTests.cs` (que
  prueba directamente el stub que ya no se inyecta) y limpiar el
  comentario obsoleto en `RequisicionesEndpointsTests:283`. **Total de
  tests determinísticamente rojos atribuibles a esta familia de bugs:
  9** (1 hallazgo B + 8 hallazgo C). Follow-up dedicado debe auditar la
  estrategia entera, no parchear test por test.

### Rev. 20 — Setting `AutoGenerarOcAlAutorizar` por empresa (ADR-0033, 2026-05-13)

- **Asunción A3 reabierta y revisada** (§3). La narrativa original
  "automática síncrona" entra en conflicto con la del diseño OC §1.2 /
  §3.bis.1 ("comprador convierte manualmente vía 1:1 o N:1"). El owner
  decidió conservar AMBAS narrativas y resolverlas vía setting
  configurable por empresa. Detalle completo en
  [ADR-0033](../../decisiones/0033-setting-auto-generar-oc-al-autorizar.md).
- Default `AutoGenerarOcAlAutorizar = false` (modo manual, Narrativa B
  del diseño OC). El owner cambia el flag vía
  `PATCH /api/v1/compras/configuracion` sin redeploy.
- **Cambios en handlers cuando setting=false**:
  - `AutorizarRequisicionHandler` NO llama a `IGenerarSolicitudCompraPort`.
  - La RQ pasa a `EnSurtido` con `ComprometidaEnOcId=null`.
  - El comprador convierte vía botón "Convertir a OC" en RQ detalle
    (modo 1:1) o vía Sheet "Nueva OC" Consolidación (modo N:1).
- **Cambios en handlers OC**:
  - `ListarRequisicionesDisponiblesHandler` filtra ahora
    `Estado=EnSurtido AND ComprometidaEnOcId=null` (antes era
    `Autorizada`).
  - `CrearOrdenCompraDesdeRequisicionHandler` (1:1) y
    `AgregarLineaDesdeRequisicionHandler` (N:1) aceptan RQs en
    `EnSurtido` (no `Autorizada`) y heredan la línea con
    `CantidadDeCompra` (no `Cantidad` original — la parte de almacén ya
    fue cubierta al autorizar).
- **Tabla nueva**: `compras.settings (id, empresa_id, auto_generar_oc_al_autorizar, ...)`,
  una fila por empresa con UNIQUE en `empresa_id`. Migración seedea con
  default false para empresas existentes (idempotente).
- **Permisos nuevos**: `compras.configuracion.leer` y `.editar`.
- **Payload**: `comprasSettings` viaja en `LoginResponse` y `MeResponse`
  para que el FE consuma sin fetch extra.

### Rev. 19 — `<CollaborationHub>` Sprint Buffer: health check + UAT prep (2026-05-09)

- **Sprint Buffer del plan Camino A** (PR #77): cierra los 3 gaps de
  UAT readiness identificados en el inventario de planning.
- `SignalRHealthCheck` (`backend/src/Api/Hubs/SignalRHealthCheck.cs`):
  IHealthCheck custom registrado con tag `ready`. Verifica que el
  manager singleton es resoluble, que en Production la conn string de
  SignalR está set (Key Vault reference resuelve), y que la conn string
  parsea como `Endpoint=...;AccessKey=...`. Falla → instancia sale del
  pool de App Service. NO hace round-trip a Azure SignalR — eso lo
  cubren las metric alerts del sprint 3a.
- `plan-uat-compras.md` §3 pre-requisitos: Azure SignalR Service +
  Action Group + dashboards §6. §4 Bloque H nuevo (8 casos CP-70 a
  CP-77) cubriendo presence, transición Viewing→Editing, salidas
  explícita y silenciosa, aislamiento cross-empresa, lost-update vía
  Capa 1, métricas en App Insights, health check. §5 criterio 4
  incorpora bloque H. §8 cronograma agrega sesión D6 (tarde).
- `runbook-compras.md` §7 (spot-check): 2 curls al `/hubs/compras/negotiate`
  (sin token → 401, con token → 200 con `connectionId`). Criterio de
  aceptación cita el check `signalr_hub` del JSON de `/health`.
- 1 smoke test nuevo verifica que `/health/ready` retorna 200 con el
  check `signalr_hub` integrado. Suite Hubs/ ahora 11/11 verde.
- §8.6 fila `<CollaborationHub>` actualizada: backend completo +
  observable + UAT-ready. Pendiente sprint 3b (FE primitives) cuando
  exista pantalla Compras del FE.

### Rev. 18 — `<CollaborationHub>` Sprint 3a: observability + alertas (2026-05-09)

- **Sprint 3a del plan Camino A** (PR #76): observability backend
  completa antes del UAT.
- `ComprasHubMeter` (Meter `Millet.Compras.Hub`) registra 4 instruments:
  3 counters (`softlock.tracked`, `softlock.released`, `softlock.expired`)
  con tags `compras.hub.empresa.id` / `compras.hub.entidad` /
  `compras.hub.modo`, y 1 UpDownCounter (`hub.connections.active`).
  Wire en `Program.cs` vía `WithMetrics(m => m.AddMeter(...))` — el
  AzureMonitor OTel Distro los exporta a `customMetrics` de App Insights.
- Bicep nuevo: `infra/modules/monitoring-alerts.bicep` con Action Group
  (email a `ownerEmail`) + 2 metric alerts sobre el SignalR Service:
  `SystemErrors > 5 / 5 min` (severidad 1) y `ServerLoad > 80% avg / 15 min`
  (severidad 2). Wireado desde `main.bicep` después del módulo `budget`.
- `runbook-compras.md` §8 nuevo: triage del hub (Service status, Key Vault
  rotation, SKU saturation, expirations alto). KQL para visualizar las
  métricas custom del hub.
- `dashboards-compras.md` §6 nuevo: 4 queries para conexiones activas,
  tracked vs released vs expired, hot resources, modo Viewing/Editing.
- 3 unit tests del meter en `ComprasHubMeterTests` con `MeterListener`.
- Sprint 3b (FE primitives) **deferred sin plazo**: el módulo Compras
  del FE no existe todavía, así que no hay consumidor. Backend operativo
  + observable es suficiente para UAT.

### Rev. 17 — `<CollaborationHub>` Sprint 2: soft-lock manager operativo (2026-05-09)

- **Sprint 2 del plan Camino A** (PR #75): manager in-memory de soft locks
  + worker de expiración + 5 behavior tests integration.
- `ComprasHub` ahora cablea `ViewingResource`, `EditingResource`,
  `LeaveResource`, `Heartbeat` y `GetPresence` al `ISoftLockManager`
  singleton. `OnDisconnectedAsync` releasa la entry. El manager
  broadcastea `userPresence` al grupo `empresa:{id}` con la lista
  completa de presentes (FE no merge).
- `SoftLockExpirationWorker` (BackgroundService) barre cada 30s
  (configurable) y expira entries con `LastSeenUtc` > TTL (default 90s).
- Estado in-memory por instancia — Azure SignalR Service hace routing
  entre instancias del backplane y los heartbeats hacen converger el
  estado. Aceptable para 30-50 usuarios concurrentes; no requiere Redis.
- §8.6 fila `<CollaborationHub>`: actualizada a "backend operativo".
  PLATFORM-TODO de `RequisicionConfiguration.cs` removido — la entidad
  ya emite presence al hub.

### Rev. 16 — Reversión: `<CollaborationHub>` SÍ entra en v1 (Camino A) (2026-05-09)

- El owner decidió revertir la recomendación de Rev. 15: **`<CollaborationHub>`
  se implementa antes del UAT de v1**, no en v1.1.
- Razones: awareness colaborativo en tiempo real mejora UX para 30-50
  usuarios concurrentes en back-office; la infra Bicep de Azure SignalR
  Service ya está provisionada y wireada en Key Vault, reduciendo el
  trabajo a solo código backend.
- El polling ligero del FE sobre `Version` (Camino C de Rev. 15) **no
  se implementa** — los stubs `<CollaborationIndicator>` y
  `useCollaboration` del FE (UF0-PR2) se sustituirán por el cliente
  SignalR real en el sprint 3 del plan.
- Plan: 4 PRs secuenciales (~4-5 sprints, 20-25 días-persona). Detalle
  en [collaboration-hub-status.md](../../operacion/collaboration-hub-status.md) §8.
- §8.6 fila `<CollaborationHub>`: actualizada para reflejar "Estado:
  en implementación (Camino A, sprint 1+ en curso)" en lugar del
  "deferred a v1.1" de Rev. 15.

### Rev. 15 — Deferral oficial de `<CollaborationHub>` a v1.1 (2026-05-09)

- B.3 (brief de UI Compras) cerrado como doc-only: assessment formal
  del estado de la deuda `<CollaborationHub>` (SignalR + Azure SignalR
  Service, ADR-0001 + ADR-0012 Capa 2) confirma que **no hay
  scaffolding** en el repo (cero packages, cero hubs, cero connection
  string, cero provisión Bicep).
- Decisión: **Camino C** — diferir `<CollaborationHub>` a v1.1, operar
  v1 con Capa 1 (optimismo en BD via `Version`/`IsConcurrencyToken`,
  ya implementada) + polling ligero del FE sobre `Version` cada 30s
  como awareness colaborativo de bajo costo. El `412 Precondition
  Failed` sigue siendo el guardia final.
- Razones: (a) proteger el cutover de v1, (b) introducir dependencia
  Azure SignalR con un solo módulo wireado primero (aprendizaje
  barato), (c) los stubs FE (`<CollaborationIndicator>` /
  `useCollaboration`) ya escritos en UF0-PR2 garantizan migration path
  trivial cuando v1.1 implemente el hub.
- Detalle, criterios para reabrir el ticket en v1.1 y plan de FE en
  v1: ver
  [collaboration-hub-status.md](../../operacion/collaboration-hub-status.md).
- §8.6 sin cambios — `<CollaborationHub>` ya estaba marcado como
  pendiente; ahora la decisión es oficial.

### Rev. 14 — Cierre de Fase 7 y deferral de `<StubsTeardown>` (2026-05-08)

- **F7-PR3** ejecutado como doc-only: §8.6 actualizada para reflejar
  que el cierre de `<StubsTeardown>` queda **diferido sin plazo fijo**.
  El cierre real depende de que existan los submódulos Almacén y OC
  con adapters productivos; mientras tanto los stubs siguen activos
  en Development/Test (guardia bloquea Production).
- **Alcance final de Fase 7**:
  - F7-PR1 ✅ — catálogos cross-empresa + seed test data + validación.
  - F7-PR2 ⏸ — importer SAP **diferido post-MVP** hasta que el
    cliente entregue export real.
  - F7-PR3 ✅ — esta entrada (doc-only).
  - F7-PR4 ❌ — migración legacy **descartada por owner**; el portal
    viejo queda read-only.
- Funcionalmente Fase 7 cierra con F7-PR1; F7-PR3 es solo
  housekeeping documental.

### Rev. 13 — Catálogos cross-empresa + alcance MVP de Fase 7 (2026-05-08)

- **§10.2.3** corregida: `articulo_id` y `proveedor_sugerido_id` ya
  no apuntan a `compras.X` (mismo schema). Apuntan a `compartido.X`
  (cross-schema → FK lógica + validación cross-table en Application
  layer).
- **§10.2.4** reescrita: `compartido.proveedores` y
  `compartido.articulos` (no `compras.X`). Razón: son catálogos
  cross-module (Compras, CxP, Activos Fijos cuando lleguen). SAP
  tiene 1 catálogo, el ERP también.
- **F7-PR1 implementado en MVP** con seed test data
  (`CatalogosTestSeedHostedService`, non-Production only) — 5
  proveedores + 10 artículos cubriendo las 4 naturalezas. Endpoints
  GET read-only en `/api/v1/catalogos/...` con permiso nuevo
  `compartido.catalogos.leer`.
- **Validación cross-table** agregada a `AgregarLinea`,
  `ActualizarLinea` y `CrearRequisicion` (en
  `proveedor_sugerido_id`): el handler chequea via
  `CompartidoDbContext` que el ID exista y esté `Activo`. 404 si
  no existe; 422 si inactivo.
- **F7-PR2 (importer SAP)** y **F7-PR4 (migración legacy)**
  diferidos por decisión del owner — el cliente no quiere migrar
  RQs vivas del legacy; el portal viejo queda read-only.

### Rev. 12 — Cierre del ticket `<Outbox>` (2026-05-08)

- F6-PR4 cerró la deuda de plataforma `<Outbox>`. El
  `IIntegrationEventPublisher` real (basado en outbox transaccional +
  `OutboxPublisherWorker` con Service Bus) reemplazó al
  `NoOpIntegrationEventPublisher`, que se borró.
- §8.6 actualizada: la fila `<Outbox>` se removió. Quedan
  `<CollaborationHub>` (transversal del ERP) y `<StubsTeardown>`
  (interno a Compras, cierra en Fase 7).
- Nuevo documento de contrato:
  [docs/integraciones/compras-eventos.md](../../integraciones/compras-eventos.md)
  con shape de los 6 eventos publicados (`autorizada`, `rechazada`,
  `eliminada`, `cancelada`, `cerrada`, `saldoNoSurtido`) versión v1.

### Rev. 11 — Tracking de deuda de plataforma (2026-05-07)

- **Nuevo §8.6 "Dependencias de plataforma pendientes"**: tabla con
  `CollaborationHub` SignalR (ADR-0012 Capa 2) y Outbox + Service Bus
  (ADR-0009). Sigue la convención de ADR-0031.
- Identificadores descriptivos `<CollaborationHub>` y `<Outbox>` —
  el proyecto aún no usa sistema de tickets formal, así que se aplica
  la convención del ADR-0031 con nombres entre `<>`. Se reemplazarán
  por IDs reales cuando se adopte el sistema de tickets.
- Esta sección se vuelve estándar en todos los módulos del ERP a
  partir de aquí.

### Rev. 10 — A6 alineado con ADR 0012 (2026-05-07)

Corrección tras lectura del ADR 0012 (concurrencia híbrida). La
"bandera roja" de Rev. 9 era incorrecta: el ADR no fija optimismo
monolítico, fija un modelo de 3 capas y nombra explícitamente
"requisiciones" como caso de Capa 2 (soft lock).

- **A6 cerrada como aplicación directa del ADR**: Capa 1 (optimismo
  con `Version`/`IsConcurrencyToken`) + Capa 2 (awareness colaborativo
  vía SignalR). Sin Capa 3.
- **§3.bis.4 reescrita**: eliminado el discurso de "escalación a
  arquitectura" y las 3 rutas. Ahora solo describe la aplicación del
  ADR.
- **Eliminada** tabla propuesta `compras.bloqueos_edicion`.
- **Eliminados** comandos propuestos `TomarBloqueoEdicionCommand`,
  `RenovarBloqueoEdicionCommand`, `LiberarBloqueoEdicionCommand`,
  `ForzarTomaBloqueoCommand`. La Capa 2 vive en SignalR, no en
  comandos del módulo.
- **§10 esquema corregido**: columna `xmin_version xid` reemplazada
  por `version int NOT NULL DEFAULT 0` (alineado con `BaseEntity` del
  framework). Nota post-esquema actualizada.
- **§10 cabecera**: la columna `motivo_terminacion varchar(500)` se
  reemplaza por `motivo_terminacion_id uuid` (FK a
  `compras.motivos_rechazo` por RA5) + `motivo_terminacion_texto`
  opcional para el caso "OTRO".
- **§12 retirado** el bloque "⚠️ Pendientes con equipo arquitectura"
  (no aplica).

> Lección de proceso: antes de declarar contradicciones con un ADR,
> leer el ADR. Esto debió haberse hecho en Rev. 9.

### Rev. 9 — respuestas del cliente (2026-05-07)

El cliente respondió las 10 asunciones abiertas. Resumen del impacto:

**Cambios mayores:**

- **A1 (matriz de aprobación) cerrada con modelo multidimensional**:
  3 capas (departamental, monto por departamento, naturaleza del
  artículo). Reemplaza el placeholder simple. Se agrega §3.bis.1
  (naturaleza), §3.bis.2 (motor de evaluación + tablas
  `umbrales_aprobacion_departamento` y `aprobadores_departamento`).
  Surgen ~5 preguntas finas (cuántas naturalezas, "aprobador adicional"
  para Riesgo, etc.) que no bloquean diseño pero sí Fase 9 de
  implementación.
- **A6 (concurrencia) — bandera roja**: el cliente pide bloqueo
  pesimista. **Contradice ADR 0012**. §3.bis.4 documenta 3 rutas
  posibles + modelo propuesto si se aprueba híbrido. **Bloqueante para
  implementación; requiere escalación al equipo arquitectura.**
- **A8 (migración) cambia**: las RQs con OC pendiente **sí se migran**.
  §11 reescrita con script de migración con estado vivo,
  mapeo de estados legacy → nuevo, coordinación con módulo OC.

**Cambios moderados:**

- **A4 (disparador de movimiento) re-introducida**: depende de la
  naturaleza. Coherente con A1 — el "disparador" es la satisfacción de
  la matriz, no un nivel fijo. Aclaración en la tabla de A4.
- **A5 (rechazo)**: catálogo de motivos + opción "OTRO" con texto
  libre. §3.bis.3 con tabla `motivos_rechazo`, comandos actualizados.
- **A9 (catálogos)**: departamentos, sucursales, almacenes y
  clasificaciones se diseñan fresh en el ERP nuevo (no se importan).
  Solo se importan prioridades, derechos, proveedores y artículos.
  §11.1 actualizada.

**Confirmaciones (sin cambios):**

- A2 (lock al reservar), A3 (OC automática y síncrona), A7 (folio
  `MID2026-000001`), A10 (sucursales físicas vs lógicas).

**Quedó obsoleta:**

- A15 (permisos de autorización de saldo): ya no aplica desde A12 (los
  saldos son informativos, no hay re-aut).

### Rev. 8 — A12 cerrada: saldos solo informativos (2026-05-07)

**Cliente confirma**: los saldos no surtidos son informativos. No hay
re-autorización formal. Esto **simplifica significativamente** el
modelo:

- **Eliminado** `Tipo` (Inicial/Saldo) de la entidad `Autorizacion`
  (§4.4 y §10.1). Ahora `(requisicion_id, nivel)` es UNIQUE.
- **Eliminado** el loop `EnSurtido → EnAutorizacion` de la state
  machine (§5.2). Cuando llega material parcial, se registra el
  cubrimiento; si la OC cierra con saldo no entregado, se emite
  `SaldoNoSurtidoEvent` (informativo) y la RQ permanece en `EnSurtido`
  hasta que cierre (todas las líneas en `CantidadPendiente=0`) o se
  cancele.
- **Eliminados** los estados ASP (7) y ASC (8) del legacy del mapeo en
  §5.1. No tienen equivalente en el ERP nuevo.
- **Renombrado** evento `SaldoRequiereAutorizacionEvent` →
  `SaldoNoSurtidoEvent` (§7.1) e integración
  `compras.requisicion.saldoRequiereAutorizacion.v1` →
  `compras.requisicion.saldoNoSurtido.v1` (§7.2).
- **Simplificado** método del agregado: `RegistrarAutorizacion(usuarioId,
  nivel, notas?)` sin `tipo`. `RegistrarRecepcion` ya no abre re-aut.
- **Simplificado** comando `AutorizarRequisicionCommand` — sin lógica
  de "si es saldo".
- A12 retirada de pendientes en §12.

> El comprador gestiona los saldos no surtidos desde el módulo OC
> (cancelar OC parcial, generar otra OC para otro proveedor, etc.). Esa
> mecánica vive en OC, no en Requisiciones.

### Rev. 7 — A11 cerrada (2026-05-07)

- **Cliente confirma: adjuntos van en v1.1**, no v1.
- A11 marcada como cerrada en §3.
- §12 pendientes: A11 retirada.
- v1 sale sin modelo de `Adjunto`. v1.1 los agrega vía ADR 0024 como
  migración aditiva (sin impacto en datos de v1).

### Rev. 6 — pregunta abierta de §10.2.2 cerrada (2026-05-07)

- **Decisión del cliente**: artículos solo desde catálogo. `articulo_id`
  siempre requerido; sin `descripcion_libre` ni texto libre alterno.
- **Decisión del cliente**: el campo `notas` por línea es **editable en
  cualquier estado no terminal**, como excepción a la regla de
  inmutabilidad post-Borrador. Sirve para anotaciones operativas
  (recepciones parciales, comentarios del autorizador, observaciones
  del comprador).
- §10.3 actualizado: removida la columna `descripcion_libre`.
- §4.2 invariantes ajustadas: `Notas` listada como excepción explícita.
- §6.1 nuevo comando: `ActualizarNotasLineaCommand` (no cambia estado).
- §9 nuevo endpoint: `PATCH .../lineas/{lineaId}/notas`.

### Rev. 5 — snapshots eliminados (2026-05-07)

Consecuencia del cierre de A8 (no se migra histórico).

- §10.2.2 reescrita: **sin snapshots de catálogo**. Solo FK lógica
  (`articulo_id`, `proveedor_sugerido_id`, etc.). Las descripciones se
  resuelven por join contra el catálogo vigente.
- §10.3 actualizado: removidas columnas `articulo_clave_snapshot`,
  `articulo_nombre_snapshot`, `unidad_medida_snapshot`,
  `cuenta_clave_snapshot`, `centro_costo_clave_snapshot`. Agregado
  `descripcion_libre` opcional por si el cliente requiere texto libre
  (ver pregunta abierta en §10.2.2).
- §10.2.4 ajustado: `unidad_medida` se llama "string" (no "snapshot").
- Razón: catálogos son propiedad del ERP, renombres serán raros y
  trazables vía auditoría del catálogo. No diseñar para necesidades
  hipotéticas — agregar snapshots después es migración aditiva.
- `precio_estimado` se conserva (no es snapshot de catálogo, es la
  estimación del solicitante; ver §10.2.5).

### Rev. 4 — A8 cerrada (2026-05-07)

- **Cliente confirma: no se migra histórico de requisiciones.** A8
  pasa de "pregunta abierta" a "decidida".
- §11 simplificada:
  - §11.1 mantiene catálogos y permisos (lo único que sí migra).
  - §11.2 explícita la lista completa de tablas que **no** se importan
    (incluye `DO_RQ*`, `PEDIDOS*`, `RQ_PERIODOS`, `RQ_MOVIMIENTOS`,
    `NOTIFICACIONES`, `BLOQUEOS`, `SESIONES`, `USUARIOS`).
  - §11.3 ajusta la estrategia: RQs en vuelo se cierran/abandonan en
    el legacy; el portal viejo queda read-only durante el periodo de
    coexistencia. Sin API ni vista en el ERP nuevo que lea esa data.
- §12: pendientes A8 retirado. También se elimina el pendiente de
  "schema dump completo para descubrir tablas faltantes
  (`DO_INVENTARIO`)" — al no migrar histórico, esas tablas dejaron de
  ser relevantes.

### Rev. 3 — decisiones de normalización explicitadas (2026-05-07)

Se agrega §10.2 con cinco decisiones que estaban implícitas:

- **Enums en código vs lookup tables**: enum + `CHECK`, no tabla
  lookup, para `estado`, `clasificacion`, `prioridad`, `nivel`, `tipo`.
- **Snapshots de catálogo en líneas**: `articulo_id` + columnas
  `articulo_clave_snapshot` y `articulo_nombre_snapshot` (igual para
  proveedor, cuenta contable, centro de costo, unidad de medida).
  Preserva trazabilidad histórica si el catálogo cambia.
- **FKs físicas vs Guids lógicos**: solo Guids lógicos cross-schema.
  FKs físicas únicamente intra-módulo (ej. línea → cabecera).
- **Catálogos pendientes** (`compras.proveedores`, `compras.articulos`)
  se diseñan en otros documentos; este expone solo `*_id` lógicos.
- **Convenciones generales**: snake_case, `uuid` con Guid v7,
  `timestamptz`, `numeric` con escalas explícitas, `NOT NULL` por
  default, `CHECK` constraints para invariantes simples.

§10.3 reescribe `compras.requisicion_lineas` con las columnas snapshot
y los `CHECK` de cubrimiento.

### Rev. 2 — tras review técnica (2026-05-07)

Cambios derivados del review interno. Foco: resolver contradicciones
internas y reducir sobre-modelado del legacy.

- **Contradicciones resueltas:**
  - A4 (N1 dispara movimiento) eliminada por contradecir §5 y A3. La
    bifurcación pasa a N2 sin excepciones.
  - A3 ahora es explícita: **automática y síncrona** dentro del handler
    de `AutorizarRequisicionCommand`. No hay estado intermedio
    "autorizada sin OC".
- **State machine simplificada de 11 a 8 estados.** Lo que antes era
  estado distinto (`AutorizadaParcial`/`Completa`,
  `EnSurtido`/`SurtidaParcial`/`Surtida`, `EnAutSaldo`/`AutSaldoCompleta`)
  ahora se deriva de `Autorizaciones` y `Cubrimiento`. Ciclo de
  re-autorización múltiple representado explícitamente.
- **Scope ampliado:**
  - `Cancelar` post-autorización movido de v2 a **v1** (libera reservas,
    aborta OC). Sin esto, las RQs zombi serían el dolor #1.
  - `Rechazar`, `Eliminar` y `Cancelar` separados — antes estaban
    confundidos. Estados terminales distintos preservan métricas.
- **Reserva de stock cocida:**
  - Modelo de tabla de reservas (no decremento físico).
  - `ReservaId`, TTL configurable (default 14 días),
    `ILiberarReservaPort` explícito, job de saneamiento.
  - Aclaración de no contradicción con ADR 0012.
- **Errores HTTP**: conflicto de versión retorna 412 con Problem
  Details, no 500. Documentado.
- **Permisos de saldo fusionados** con los iniciales (asunción A15).
- **Nuevas asunciones explícitas:** A11 (adjuntos), A12 (razón de
  re-aut saldo), A13 (bulk), A14 (read models), A15 (permisos saldo).
- **Schema**: comentario aclarando que `creador_id` no es duplicación
  de la auditoría tecnológica del framework — captura semántica
  distinta. Consolidación de columnas de terminación
  (`motivo_terminacion`, `actor_terminacion_id`, `fecha_terminacion`)
  reemplaza `motivo_eliminacion`.

### Rev. 1 — versión inicial (2026-05-07)

Primera propuesta de diseño basada en el levantamiento validado con
el cliente. Pendiente de revisión técnica del equipo y de cierre de
asunciones.
