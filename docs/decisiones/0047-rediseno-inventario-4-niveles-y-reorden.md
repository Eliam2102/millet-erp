# ADR-0047: Rediseño de inventario a 4 niveles con asignación artículo-ubicación y reorden automático

- **Estado**: Aceptada (enmendada 2026-07-06 — el reorden sube a Nivel 1/2; ver Enmienda)
- **Fecha**: 2026-07-03 (refinado 2026-07-03: cut a 7 PRs — captura de bin real partida a PR7)
- **Decisores**: Eduardo Paredes (owner), Victor
- **Etiquetas**: almacen, inventario, compras, requisiciones, reorden, saldos

## Enmienda 2026-07-06 — El reorden sube a Nivel 1/2 (deja de operar por Nivel 4)

> **Alcance:** esta enmienda **revisa los puntos §3 (Política de reposición) y §4
> (Motor de reorden)** de la Decisión original y ajusta el cut de PR5. El resto del
> ADR —jerarquía de 4 niveles (§1), asignación artículo→ubicación (§2), eliminación
> de reservas (§5), ya materializados en PR1–PR4— **se conserva sin cambio**. Decidida
> con dirección (Eduardo).

### Qué cambió y por qué

**Antes (§3/§4 originales):** la política de reorden (min/máx/punto de reorden +
bandera de reabasto + objetivo) vivía en la **asignación artículo→ubicación (Nivel 4)**,
y el motor barría y deduplicaba **por ubicación**.

**Problema detectado** (exploración read-only de PR5, 2026-07-06): **desajuste de grano.**
La RQ y la OC solo conocen el **Almacén (Nivel 2)** — la línea de RQ no tiene destino
alguno (solo el header lleva `AlmacenDestinoId`), y la línea de OC llega a
`AlmacenDestinoId` (N2). "Lo pedido / en tránsito" es, por tanto, **indeterminado a nivel
ubicación** hasta la recepción (el aterrizaje a N4 ocurre al recibir, vía la ubicación
`es_default`). Poner el reorden en N4 obligaba a una de dos salidas malas: (a) **agregar
un destino Nivel 4 a la RQ/OC** —cambio invasivo de dominio + migración en Compras—, o
(b) **restar mal "lo pedido"** al calcular el faltante por ubicación. **Subir el reorden
a N1/N2 alinea el grano de la política con el grano de lo pedido y elimina el hueco**: la
RQ automática es una RQ normal a nivel almacén y **no** se toca su forma.

### Nuevo modelo de reorden (reemplaza §3 y §4)

- **La config de reorden vive en Nivel 1 (Sucursal) o Nivel 2 (Almacén)**, por artículo,
  **nunca en ambos** para el mismo artículo (exclusión — evita disparo doble):
  - **Caso normal = N2 (Almacén):** config por almacén real (p. ej. artículo en CRYSTAL).
  - **Caso especial = N1 (Sucursal):** cuando el N2 es genérico; config por sucursal.
- **Nivel 3 y Nivel 4 conservan min/máx/reorden pero SOLO INFORMATIVO** (consulta/
  reportes), **sin bandera que dispare**. La asignación artículo→ubicación (N4) de PR3
  **sigue existiendo** para "ver el 0" y el min/máx informativo.
- **Detección al nivel configurado (N1/N2)** sumando saldos hacia arriba por **rollup**
  (ya existe `ConsultarDisponibilidadPorAlmacenAsync` en `IAlmacenSaldoQueryPort`).
- **Faltante = objetivo − (existencia física + todo lo vivo)**, calculado **a nivel
  almacén/sucursal**. *"Todo lo vivo"* = borradores + RQ vivas (no terminales) + OC no
  surtidas ni canceladas, **incluyendo pedidos manuales de usuarios** (todo al mismo
  grano N2 ahora — el desajuste desaparece). Las **OC cuentan solo desde `Autorizada`**
  (con recepción pendiente); Borrador/EnAutorización aún no se comprometen y se excluyen
  (además el índice parcial de partidas abiertas ya cubre `estado = 3`).
- **Deduplicación por artículo+almacén** (o artículo+sucursal si la config es N1),
  **no por ubicación**.
- **El borrador es una RQ NORMAL a nivel almacén** (cabecera sucursal→almacén, como
  cualquier RQ). **El almacenista decide N3/N4 al recibir.** **NO se agrega ubicación a
  la RQ ni a la OC** — esto es lo que resuelve el desajuste de grano.

### Las 4 fuentes de campos de la RQ automática (decididas; verificadas en código 2026-07-06)

| Campo RQ | Fuente | Verificación en `main @ c2c88af` |
|---|---|---|
| `DepartamentoId` | Departamento **FIJO de sistema** ("Reabastecimiento Automático"), creado por **seed** (como el usuario de servicio) | nuevo (seed) |
| `PrecioEstimado` (línea) | `Articulo.PrecioReferenciaMonto` del maestro (**0 si NULL**) | existe (`ArticuloDetalleDto.PrecioReferenciaMonto`) |
| `UnidadMedida` (línea) | `Articulo.UnidadMedidaDefault` / `UnidadMedidaId` del maestro | existe (`ArticuloDetalleDto.UnidadMedida*`) |
| `SucursalCodigo` (folio) | `Sucursal.Clave` (módulo Administración) vía el `SucursalId` que el motor deriva de la config | existe (`ISucursalReadPort.Clave`) |

Otros (ya decididos): **requisitante = usuario de SERVICIO** (entidad `UsuarioServicio`,
infra existente: resolver + bootstrap + auth handler + migración); **job periódico**
molde `IdempotencyKeysCleanupJob` (advisory lock PG); **campo de ORIGEN sistema nuevo**
en la RQ (distingue automática de manual); **borrador editable** (quitar/reducir líneas),
**re-evaluación al cancelar**, **avisos** (stock recuperado; regeneración si no se quita
la bandera).

### Impacto en PR3 (ya mergeado, #442)

- La **asignación artículo→ubicación (N4) se conserva** para "ver el 0" y el min/máx
  **informativo**.
- La **bandera `AutoRequisicion` y el `Objetivo` de la asignación N4** —que hoy anticipan
  el disparo por ubicación— **se degradan a informativos** (se conservan las columnas; el
  motor las ignora, sin migración destructiva sobre PR3): la política que **dispara** se
  mueve a la nueva config N1/N2.

### Decisiones cerradas de la enmienda (2026-07-06)

1. **N4 bandera/objetivo → informativo** (se conservan las columnas; sin drop sobre PR3).
2. **Config de reorden = una sola tabla con discriminador de nivel** (`articulo_id`,
   nivel `{Sucursal|Almacén}`, `entidad_id`, min/máx/reorden/bandera/objetivo), con
   **`unique(articulo_id, nivel, entidad_id)`** — un artículo **puede tener varias
   configs** (una por sucursal/almacén donde se reabastece; p. ej. HER2031 en Cancún-N1
   + Conkal-Herrajes-N2 + Conkal-Crystal-N2). La **exclusión N1 ⊕ N2 es por sucursal y
   se valida en el handler** (contra configs **activas**), resolviendo la sucursal de un
   almacén N2 vía `Almacen.SucursalId`; **NO** sale del unique. Además, crear una config
   exige que **exista la asignación** (PR3) del artículo en esa entidad
   (`REORDEN_SIN_ASIGNACION`) — coherencia estructural, sin juzgar sentido de negocio.
3. **OC "viva" = solo desde `Autorizada`** con recepción pendiente (excluye
   Borrador/EnAutorización).
4. **FE:** la **config N1/N2 es un sub-PR FE propio** (5.F, tras 5.A); la revisión/edición
   del borrador automático **reusa la bandeja de requisiciones** (es una RQ normal en
   `Borrador`) con un badge/filtro de **origen sistema**.

> **Actualización 2026-07-07 (PR C — backend).** La decisión #1 se **completa**: en vez de
> conservar como informativas las columnas en N4, se **eliminan** de
> `AsignacionArticuloUbicacion` (`Minimo/Maximo/PuntoReorden/AutoRequisicion/Objetivo` + sus
> 3 CHECKs + el método `EditarPolitica` + el endpoint `PATCH /asignaciones/{id}`, que solo
> editaba esos campos). Motivo: el caso **informativo** ya vive íntegro en N1/N2
> (`ConfiguracionReorden` con `AutoRequisicion=false` guarda min/máx sin generar borradores);
> duplicarlo en N4 confundía reportes. Verificado por exploración read-only: **ningún
> consumidor de producción** lee esos 5 campos de N4 — el motor (`EvaluarReorden`/worker) lee
> solo `ConfiguracionReorden`, y `REORDEN_SIN_ASIGNACION` solo chequea **existencia** de la
> asignación. La entidad queda como pura relación **artículo ↔ ubicación**
> (`UbicacionId + ArticuloId + Estatus`); `Asignar` conserva todas sus validaciones + la
> fila-en-0, `Desasignar` su guardrail. N3 (SubAlmacén) no tenía estos campos. Además PR C
> agrega el endpoint **`GET /api/v1/almacen/ubicaciones`** (ubicaciones enriquecidas con clave/
> nombre del sub-almacén padre) que faltaba para el FE de asignación artículo→ubicación.

> **Actualización 2026-07-07 (PR C7.1 — backend, gestión de ubicaciones N4).** Materializa
> el N4 real que el rediseño previó (§1): el admin y el rol de almacén ya pueden crear/editar/
> desactivar ubicaciones físicas (racks/pasillos) una por una con clave alfanumérica (≤20,
> única por sub-almacén). Agrega el **par de permisos `almacen.ubicaciones.leer` /
> `almacen.ubicaciones.administrar`** (sub-namespace `00000008-000e-*`; el `GET` listar migra
> de `almacen.almacenes.leer` a `almacen.ubicaciones.leer` — coherencia de recurso) y monta
> **`POST /ubicaciones`**, **`PATCH /{id}`** (solo clave/nombre) y **`POST /{id}/desactivar`**
> + **`/reactivar`** sobre los handlers ya existentes, con Idempotency-Key. La baja tiene
> **dos guardrails**: la ubicación **default (ÚNICA) no es desactivable** (es el destino del
> trigger de saldos) y una ubicación **con saldo > 0** tampoco. **Lo que NO cambia (queda para
> PR C7.2):** la ÚNICA `es_default` sigue siendo el destino de enrutamiento del trigger de
> saldos y los movimientos siguen capturando solo el sub-almacén; por eso los bins reales
> recién creados **coexisten con la ÚNICA pero aún no reciben inventario** hasta que C7.2
> capture la ubicación explícita en el movimiento y reescriba el trigger. El PATCH se acotó a
> clave/nombre a propósito (quitando `Estatus` de `EditarUbicacionCommand`) para que la baja no
> pueda saltarse esos guardrails.

### Impacto en el cut

- **PR5 se subdivide en sub-PRs (5.A–5.E)** — ver "Notas de implementación → Cut de PR5"
  más abajo. **PR6 (consulta jerárquica) y PR7 (bin real) no cambian.**
- Se **revisan dos descartes originales**: la opción 3 ("min/máx/reorden en el Almacén")
  pasa a ser —para la **política que dispara**— la **elegida**, con el grano por ubicación
  degradado a **informativo**; y "una sola bandera activa por asignación" se sustituye por
  la **exclusión N1 ⊕ N2** (un solo nivel configurado por artículo).

## Contexto y problema

El modelo actual de Almacén/Inventario (levantado 2026-07-02 sobre `main @ 147cfbc`,
ver `docs/modulos/almacen/00-levantamiento.md` §3 y `01-diseno.md`) tiene tres
limitaciones que bloquean la operación tipo SAP que el área necesita:

1. **No se ve el 0.** La fila de `almacen.saldos_inventario` (PK
   `(sub_almacen_id, articulo_id)`) **nace solo cuando ocurre un movimiento de
   entrada** — el trigger PG `tg_movimientos_actualizar_saldo` la crea con
   `INSERT ... ON CONFLICT DO UPDATE` únicamente en entradas. Un artículo que
   "debería estar" en una ubicación pero nunca ha tenido movimiento **no
   aparece**: el área no puede ver "tengo 0 de esto aquí", que es justo la señal
   que dispara la reposición. Las salidas incluso truenan con
   `SALDO_INEXISTENTE` si no hay fila.

2. **No hay min/máx/punto de reorden.** Ni el Artículo (`compartido.articulos`),
   ni el Almacén, ni el Sub-almacén, ni el Saldo modelan niveles de reposición.
   El diseño previo lo dejó explícitamente condicional
   (`01-diseno.md:663` — `StockBajoMinimoQuery // si master de artículos tiene
   min/max`). Hoy no existe en código ni en BD.

3. **No hay reabasto asistido.** "Reabastecer" hoy significa que un almacenista
   **levanta manualmente** una requisición cuando nota que falta material
   (`00-levantamiento.md` §6.5/§6.7). No hay motor que detecte el mínimo y
   proponga la reposición; depende de la memoria y disponibilidad del operador.

Decisión de negocio (dirección): migrar a un **modelo de inventario tipo SAP**
con jerarquía física completa, política de reposición por ubicación y un motor
de reorden que proponga requisiciones automáticamente. Este ADR documenta el
**destino** y el **porqué** para revisión antes de escribir código.

## Drivers de la decisión

- **Visibilidad del cero** como precondición para cualquier política de stock.
- **Granularidad por ubicación**: el mismo artículo tiene políticas distintas
  según dónde vive (no una política única por almacén).
- **Automatizar la reposición** sin quitarle el control al humano (la propuesta
  es editable, no se dispara compra sola).
- **Alinear con SAP** para que la migración de datos y la operación sean
  reconocibles por el área.
- **Reducir acoplamiento**: la reserva de inventario (introducida en F3-PR2)
  agrega complejidad —columna generada, CHECK, puertos, campo en la RQ— que ya
  no aporta valor bajo el nuevo modelo (ADR-0043 dejó el material recibido como
  stock libre de todos modos).

## Opciones consideradas

1. **(Elegida)** Jerarquía de 4 niveles + asignación artículo-ubicación (OITW) +
   min/máx/reorden en la asignación + motor de reorden por job + eliminar
   reservas.
2. Mantener 3 niveles y resolver "ver el 0" con un left-join contra el catálogo
   de artículos en la consulta (sin tabla de asignación).
3. Poner min/máx/reorden en el Almacén (o Sub-almacén) en vez de en la
   asignación artículo-ubicación.
4. Reorden event-driven (evaluar en cada salida que cruza el mínimo) en vez de
   job periódico.

## Decisión

Se adopta la opción 1, con los siguientes puntos (cerrados con dirección):

### 1. Jerarquía física de 4 niveles

`Sucursal → Almacén → Nivel3 → Nivel4`, **todos obligatorios**.

- **Nivel3 = el Sub-almacén actual** (`almacen.sub_almacenes`), que **conserva su
  `Tipo`** (`TipoSubAlmacen`: Insumos / MaterialesDirectos / MaterialEnRevision /
  Transitorio), porque ese tipo gobierna variante A/B de recepción y el flujo
  8.A. No se re-mapea ni renombra.
- **Nivel4 = ubicación/bin** (entidad y tabla nuevas), que era exactamente el
  "cuarto nivel diferido a vNext" del levantamiento (`00-levantamiento.md:119`).
- **La existencia se cuenta en Nivel4.** Los niveles superiores (Nivel3, Almacén,
  Sucursal) se obtienen por **rollup** (suma de los Nivel4 hijos), no se
  almacenan de forma independiente.
- **Migración de datos**: al desplegar, se **auto-crea un Nivel4 default
  (`ÚNICA`)** por cada Sub-almacén existente y se **mueven ahí los saldos
  actuales**. El área arranca sin captura manual; puede refinar ubicaciones
  después.

### 2. Asignación artículo → ubicación (tipo OITW)

- **Tabla nueva** que relaciona un **Artículo** con una **ubicación Nivel4**.
- **Un artículo puede tener varias asignaciones** (varias ubicaciones).
- **Al asignar, nace la fila de `saldos_inventario` en 0** (cantidad 0, costo 0).
  Esto **resuelve "ver el 0"**: la existencia de la política de stock crea la
  fila, independientemente de que haya habido movimiento.

### 3. Política de reposición en la asignación

> ⚠️ **Revisado por la Enmienda 2026-07-06:** la política que **dispara** el reorden
> ya **no** vive en la asignación N4 sino en Nivel 1/2; el min/máx de la asignación N4
> queda **informativo**. Lo de abajo refleja el diseño original (superado).

- **min / máximo / punto de reorden + bandera de reabasto + objetivo de
  reposición** viven en la **ASIGNACIÓN (artículo+ubicación)**, **no** en el
  almacén.
- **Una sola bandera de reabasto activa por asignación**: un único nivel dispara
  la reposición (típicamente el punto de reorden); los demás niveles (min, máx)
  quedan como **referencia informativa**. No se permiten múltiples disparos
  simultáneos.

### 4. Motor de reorden

> ⚠️ **Revisado por la Enmienda 2026-07-06:** el motor opera a nivel **N1/N2** (no N4),
> el faltante y el dedup son **por artículo+almacén/sucursal**, y la RQ es una RQ normal
> a nivel almacén (sin destino Nivel4). Lo de abajo refleja el diseño original (superado);
> se conserva el job periódico, el origen "sistema" y el usuario de servicio.

- **Disparo por job programado periódico** (`IHostedService`, mismo patrón que
  `OutboxPublisherWorker`, `SoftLockExpirationWorker`, etc. — ADR-0022/D4), que
  **barre las asignaciones bajo el nivel disparador** en cada ciclo.
- Al detectar una asignación bajo el nivel, **genera una RQ en estado
  `Borrador`** (reusa el estado existente `EstadoRequisicion.Borrador = 0`, con
  un **nuevo origen "sistema"**). La RQ en Borrador es **editable** por el humano
  (quitar/reducir líneas antes de enviarla a autorización).
- **Identidad de la RQ automática**: el **creador es un usuario de servicio**
  (patrón Entra ID D3, ya previsto en Identidad); **requisitante, departamento y
  almacén se derivan de la ubicación** de la asignación.
- **Cálculo del faltante**:
  `faltante = objetivo − (existencia física + todo lo vivo)`, donde *todo lo
  vivo* = borradores + RQ autorizadas + OC no surtidas ni canceladas. Así no se
  re-pide lo que ya viene en camino.
- **Deduplicación por artículo+ubicación**: no se generan dos propuestas para la
  misma asignación en el mismo ciclo.
- **Cancelación en cualquier etapa** (borrador descartado, RQ cancelada, OC
  cancelada) **libera** el compromiso y el **siguiente ciclo re-evalúa** la
  asignación.

### 5. Se eliminan las reservas de inventario

- Se **desmonta el agregado `ReservaStock`** y toda su infraestructura: tabla
  `almacen.reservas_stock`, columna `saldos_inventario.cantidad_reservada`, la
  columna generada `cantidad_disponible` (= cantidad − reservada), el CHECK
  `reservada ≤ cantidad`, los puertos `IReservarStockPort` / `ILiberarReservaPort`
  y sus adapters, la columna `LineaRequisicion.ReservaId`, y los eventos
  `StockReservado`/`StockLiberado` (que hoy no tienen consumidores).
- ADR-0043 ya dejó el material recibido como **stock libre** sin reserva; el
  nuevo modelo generaliza eso: **no se reserva nunca**.

### 6. "Solo el faltante" se conserva, con nuevo input

- La lógica `Cubrimiento.Repartir` (que ya existe: al autorizar, reparte cada
  línea en `CantidadDeAlmacen = min(disponible, solicitado)` y
  `CantidadDeCompra = faltante`) **se mantiene**.
- **Cambia su input**: de `CantidadDisponible` (que restaba reservas) a
  **existencia física pura** (`cantidad`), coherente con la eliminación de
  reservas.

## Consecuencias

**Positivas**

- El área **ve el 0** y opera reposición por ubicación como en SAP.
- **min/máx/reorden por ubicación** habilita políticas finas por artículo.
- **Reabasto asistido** reduce faltantes por olvido, sin quitar el control humano
  (Borrador editable).
- **Menos superficie**: eliminar reservas quita una columna generada, un CHECK,
  un agregado, dos puertos y un campo en la RQ.
- Se **corrige una violación de la regla de oro**: hoy Compras lee
  `AlmacenDbContext.SaldosInventario` **directo**
  (`Compras/.../AlmacenStockReadAdapter.cs`); el rediseño lo reencauza por el
  puerto oficial `IAlmacenSaldoQueryPort` (hoy muerto, sin callers).

**Negativas / lo que se rompe o cambia**

- **PK de saldos baja a Nivel4**: `(sub_almacen_id, articulo_id) →
  (ubicacion_nivel4_id, articulo_id)`. Cambio de esquema mayor.
- **Trigger PG `tg_movimientos_actualizar_saldo` se reescribe**: nueva llave y se
  le quita la lógica de `cantidad_reservada`. Además, como la fila ahora puede
  existir en 0 por asignación, el `RAISE SALDO_INEXISTENTE` de las salidas cambia
  de semántica (la fila existirá para artículos asignados).
- **Cross-module read de Compras se reimplementa** vía `IAlmacenSaldoQueryPort`;
  el rollup a nivel almacén que Compras hacía sumando sub-almacenes ahora suma
  Nivel4.
- **`CantidadDisponible` desaparece** como columna generada (pasa a ser igual a
  `cantidad`); todo consumidor que lea "disponible" debe reapuntar a existencia
  física.
- **Los movimientos pasan a referir Nivel4**; el `ubicacion_referencia` texto
  libre de `LineaMovimiento` queda subsumido o coexiste como nota.
- **La línea de RQ probablemente gane un destino Nivel4** (hoy solo el header
  tiene `AlmacenDestinoId`), para que el reorden ubique la reposición.
- **Requisitante no-humano**: modelar que la RQ automática tenga como creador un
  usuario de servicio y derive requisitante de la ubicación — toca invariantes de
  la RQ y el catálogo de Empleados/Identidad.
- **Migración de saldos existentes** (mover a Nivel4 default) es un job de datos
  con riesgo operativo; requiere runbook.

## Relación con ADRs previos

- **ADR-0043 (entrega post-recepción de RQ)** — Se **refina**. Su riesgo aceptado
  R2 dejó el material recibido como stock libre y difirió "reservar al
  solicitante" como PLATFORM-TODO `<ReservaEntregaPostRecepcion>`. Al **eliminar
  reservas por completo**, ese TODO queda **resuelto por cancelación** (ya no
  aplica). Los estados terminales (`CerradaSinSurtir`/`CerradaSurtidaParcial`) y
  el cierre por entrega **se conservan**; el reorden los respeta al calcular "lo
  vivo".
- **ADR-0033 (auto-generar OC al autorizar)** — Se **conserva**. El setting
  `AutoGenerarOcAlAutorizar` y el "solo el faltante" siguen. La RQ automática
  entra en `Borrador` y sigue el mismo camino
  `EnviarAAutorizacion → Autorizar → bifurcación → OC`. No cambia el contrato
  RQ→OC; cambia el **input del faltante** (existencia física, punto 6).
- **ADR-0046 (catálogo de unidad de medida y conversión)** — **Dependencia**. Si
  el reorden razona en unidades, min/máx/objetivo se expresan en la **unidad de
  inventario**; la conversión a **unidad de compra** al generar la RQ queda
  sujeta a la **Etapa 4 de ADR-0046 (pendiente)**. Mientras esa etapa no exista,
  el reorden opera en unidad de inventario y la conversión de compra se maneja
  como hoy.

## Descartadas

- **Left-join contra el catálogo para "ver el 0" (opción 2)** — no da min/máx,
  ni reorden, ni ubicación física; solo maquilla la ausencia de fila. No resuelve
  el problema de fondo (política de stock).
- **min/máx/reorden en el Almacén o Sub-almacén (opción 3)** — el mismo artículo
  tiene políticas distintas por ubicación; un umbral por almacén no permite
  granularidad ni "ver el 0" por ubicación. La asignación (artículo+ubicación) es
  el grano correcto, igual que OITW en SAP.
- **Bandera múltiple (varios niveles disparando a la vez, o varias asignaciones
  del mismo artículo compitiendo por disparar)** — genera RQs duplicadas y
  ambigüedad sobre qué nivel dispara. Se fija **una sola bandera activa por
  asignación**.
- **Backfill manual de saldos a Nivel4** — bloquearía el go-live en captura de
  datos del área. Se elige backfill automático con Nivel4 default `ÚNICA`.
- **Reorden event-driven (opción 4)** — acopla el reorden al hot-path de salidas
  y complica la deduplicación. El job periódico es más simple, predecible y
  desacoplado; el retardo máximo (un ciclo) es aceptable para reposición.

## Notas de implementación

- **Cut de PRs (7)** — orden por dependencias, PR1 y PR2 **separados** (PR2 es el
  más riesgoso y se aísla). La **captura de bin real se partió a PR7** (2026-07-03):
  el destino es Camino A (movimiento con `ubicacion_id`), pero PR2 se mantiene
  acotado con una ubicación ÚNICA temporal y el movimiento sigue a nivel
  sub-almacén.
  1. **PR1** — 4º nivel: entidad + tabla `Ubicacion` + jerarquía obligatoria (sin
     tocar saldos). ✅ mergeado (#439).
  2. **PR2** — bajar saldos a Nivel4 (**acotado**): backfill de una ubicación
     **ÚNICA** por sub-almacén (marcada con bandera `es_default`), swap de PK a
     `(ubicacion_id, articulo_id)`, reescritura del trigger PG (enruta a la ÚNICA
     del `sub_almacen_id` del movimiento vía `es_default`, llavea por
     `ubicacion_id`, **sin tocar `cantidad_reservada`**), y revivir
     `IAlmacenSaldoQueryPort` para el read cross-módulo de Compras. **El
     movimiento sigue a nivel sub-almacén**; la captura de bin real se difiere a
     PR7. **El más riesgoso.**
  3. **PR3** — maestro de asignación artículo→ubicación (tabla OITW +
     min/máx/reorden/bandera/objetivo + crear fila de saldo en 0 al asignar).
     Resuelve "ver el 0". **Materializado (2026-07-03):** tabla
     `almacen.asignaciones_articulo_ubicacion`, clave única `(ubicacion_id,
     articulo_id)`, `articulo_id` lógico (compartido.articulos). Política =
     `Minimo`/`Maximo`/`PuntoReorden` (numeric 14,4) + `AutoRequisicion` (bool) +
     `Objetivo` (enum `ObjetivoReposicion` minimo/maximo/reorden). La regla "un
     nivel activo" quedó **estructural** (un bool + un enum por asignación; sin
     validación cross-fila). Auto-requisición **por ubicación** (un artículo puede
     tenerla activa en varias ubicaciones; el reorden dedup por artículo+ubicación).
     El **handler de asignar** crea la fila-en-0 idempotente (`ON CONFLICT
     (ubicacion_id, articulo_id) DO NOTHING`, mismo target que el trigger) en una
     transacción; **desasignar** bloquea si hay saldo>0 (guardrail EN_USO) y borra
     la fila-en-0 en la misma TX. El guard `CONTEO_SIN_SALDO` se mantiene (nota:
     los asignados-en-0 ahora entran legítimamente al conteo). FE diferido.
  4. **PR4** — quitar reservas (desmontar `ReservaStock`; **materializado
     2026-07-03:** cierra el estado intermedio cut-A. `cantidad_disponible`
     **redefinida `= cantidad`** (generada STORED, sin restar reservas) — todos
     los lectores del puerto/queries siguen sin cambio y el faltante de Compras
     reapunta a existencia física automáticamente. Se dropean `cantidad_reservada`,
     los 2 CHECKs de reservada, la tabla `reservas_stock`, `requisicion_lineas.reserva_id`,
     y de los DTOs del puerto `CantidadReservada`/`ReservadoActivo`. Se elimina
     también `IGenerarMovimientoSalidaPort` (stub del loop de reserva). **`sub_almacen_id`
     se CONSERVA** (load-bearing: trigger/puerto/queries/asignación). El surtido ya
     no consume reserva (el trigger decrementa físico). Migración con Down reversible.)
  5. **PR5** — motor de reorden + RQ en Borrador. **Subdividido (Enmienda 2026-07-06)**
     en 5 sub-PRs (el modelo cambió a N1/N2). Cada uno mergea por separado:
     - **5.A — Config de reorden N1/N2 + seed.** **Una tabla** de config con
       discriminador de nivel (`articulo_id` + nivel {Sucursal|Almacén} + `entidad_id` +
       min/máx/reorden + bandera + objetivo), `unique(articulo_id, nivel, entidad_id)`
       (multi-config). Validación en el handler: existencia de la entidad según nivel,
       **exclusión N1 ⊕ N2 por sucursal contra configs activas**, y **asignación-existe**
       (`REORDEN_SIN_ASIGNACION`). Degradar a **informativa** la bandera/objetivo de la
       asignación N4 (se conservan las columnas, el motor las ignora); seed del
       **departamento de sistema** ("Reabastecimiento Automático") y **usuario de
       servicio** del reorden (entrada en `ServicePrincipalsJson`). Sin motor todavía.
       **Riesgo bajo-medio** (toca PR3, sin migración destructiva).
     - **5.B — Cálculo del faltante ("stock bajo reorden" + "todo lo vivo").** Query
       que hace join config↔saldos con **rollup al nivel N1/N2** y resta *lo vivo*
       (RQ vivas + OC no surtidas, sumado por artículo+almacén). Puro cálculo, sin
       generar nada. **Riesgo medio** (define la semántica de "vivo"; cruza a Compras).
     - **5.C — Path de creación de RQ por el sistema.** Command/handler que acepta
       `empresaId`/`creadorId`/`requisitanteId` explícitos (usuario de servicio, sin
       JWT) + **campo de ORIGEN sistema** en la RQ + las 4 fuentes de campos. Reusa
       el estado `Borrador`. **Riesgo medio** (toca invariantes/creación de la RQ).
     - **5.D — Worker/job periódico** (molde `IdempotencyKeysCleanupJob` + advisory
       lock) que orquesta: barre config activa → 5.B faltante → **dedup por
       artículo+almacén** → 5.C genera Borrador. **Decisiones cerradas (2026-07-06):**
       (a) orquestación en un `GenerarBorradoresReordenCommand` (Almacen.Application.Reorden)
       que el worker invoca por `IMediator` (testeable sin timer); (b) **mapeo config→almacén**:
       N2 → `AlmacenDestinoId = EntidadId`, `SucursalId = Almacen.SucursalId`; **N1 → el
       ÚNICO almacén de la sucursal** (0 → `REORDEN_SUCURSAL_SIN_ALMACEN`; **2+ → falla
       ruidoso** `REORDEN_SUCURSAL_MULTI_ALMACEN`, reporta y sigue — una sucursal N1 tiene
       un solo almacén por diseño); (c) **agrupa 1 RQ por almacén con N líneas** (una por
       artículo bajo mínimo); (d) **dedup por faltante-neto** (5.B ya resta lo vivo y el
       Borrador creado cuenta como vivo el ciclo siguiente → no re-crea); se acepta un
       duplicado raro (doc vivo con cantidad 0 + faltante residual) que el humano cancela;
       **sin** chequeo booleano de existencia; (e) **try/catch por config** (un fallo no
       tumba el ciclo); (f) worker **Disabled por default** (opt-in por ambiente — mergear
       5.D no genera RQs hasta habilitarlo, cuando 5.E/5.F estén listos). Re-evaluación al
       cancelar / editar / avisos quedan en **5.E**.
     - **5.E — Comportamiento del borrador:** editable (quitar/reducir líneas),
       re-evaluación al cancelar, avisos (stock recuperado; regeneración si no se
       quita la bandera). **Riesgo bajo-medio** (UX/eventos).
     - **5.F — FE de reabasto + `Origen` en la RQ (absorbe 5.E).** La pantalla de config
       N1/N2 es lo único genuinamente nuevo; va como **sub-PR FE propio** tras 5.A. La
       revisión/edición del borrador automático **reusa la bandeja de requisiciones**
       existente (es una RQ normal en `Borrador`), con un badge/filtro de **origen
       sistema**. **Decisiones cerradas (2026-07-07):**
       - **Nomenclatura:** término de negocio **visible** = **"Reabasto"** (nav +
         títulos de pantalla); término **técnico** en código = **"reorden"** (archivos,
         hooks, tipos, ruta `/almacen/reorden`, endpoints `almacen.reorden.*`). No se
         renombran los internals.
       - **Hogar = pantalla dedicada** de reabasto en Almacén (`features/almacen/`, molde
         `AlmacenesPage`: tabla + Sheet lateral crear/editar), a nivel **N1/N2**. **NO**
         se integra en la asignación N4: desajuste de grano (una config N1/N2 abarca
         muchas asignaciones N4) y la asignación N4 tampoco tiene FE. Es justo el
         desajuste que esta enmienda resolvió al subir el disparo a N1/N2.
       - **Selector polimórfico de entidad:** `SucursalSelector` (N1) o `AlmacenSelector`
         (N2) según el Nivel; **reset del `EntidadId` al cambiar de nivel** (patrón §6.8);
         `initialLabel` para el cold value al editar; resolución `EntidadId`→nombre
         **polimórfica** en la grilla (según el nivel de cada fila).
       - **Tabla intermedia:** muestra mín/máx/punto-reorden y **resalta el activo según
         `Objetivo`**, sin columna Objetivo aparte. Filtros: **artículo / nivel /
         estatus** (la entidad no se filtra en la barra — es polimórfica).
       - **Form:** **llave arriba** (artículo/nivel/entidad, inmutable al editar) →
         divisor → **política** (mín/máx/punto-reorden/AutoRequisicion/Objetivo).
       - **Nav:** card **"Reabasto"** como entrada propia en la sección **Configuración**
         del módulo Almacén (ADR-0032), junto a "Almacenes y sub-almacenes" — no crea
         grupo nuevo.
       - **Backend chico:** expone `Origen` en los DTOs de RQ **lista** (para el badge) y
         **detalle** (para la nota); sin migración, serializa INT. Proyectado en **ambos**
         handlers de lista (`ListarRequisiciones` + `ListarPendientesAutorizacion`) — DTO
         compartido, un campo sin proyectar sería bug latente.
       - **Badge "Sistema"** (molde `NivelPendienteBadge`) en los **2** renders de la
         bandeja (tabla + lista compacta), solo para `origen=Sistema`; **nota al eliminar**
         (slot `aviso` de `ModalMotivo`) condicionada a `origen=Sistema`.
       - El worker **sigue Disabled por default** (5.D): esta PR da la cara para
         configurar y revisar, pero habilitarlo es un paso de ambiente aparte.
  6. **PR6** — consulta jerárquica (ver el 0 + rollup por niveles) — la cara del
     área de almacén. **✅ COMPLETO (2026-07-09, 4 commits C1–C4).** Endpoint lazy
     `GET /almacen/saldos/jerarquia` ("hijos del nodo X" con subtotal por nodo:
     cantidad + valor aditivos, el CPP no se promedia hacia arriba) + pantalla
     "Consulta jerárquica" (`/almacen/saldos-jerarquia`, junto a Saldos). Dos
     modos sobre el mismo árbol: por ARTÍCULO (su distribución por niveles; la
     ubicación es hoja) y por UBICACIÓN (browse desde la raíz, atajos de salto
     almacén/sub-almacén; el rack expande a sus artículos — 5º nivel). Carga
     PEREZOSA (una llamada acotada por expansión) + auto-expand de cadena única
     en el FE (un solo hijo no-hoja se abre solo; se infiere de la respuesta,
     sin hint del backend). Toggle "incluir vacíos" expone la fila-en-0 (racks
     asignados sin saldo — el "ver el 0" por bin, que ninguna otra consulta
     mostraba) sin alterar sumas. N1 vía `ISucursalReadPort` (por-id) con
     fallback al id; RBAC `almacen.almacenes.leer`; solo lectura, sin migración.
     **Con PR6 cierra el rediseño completo de ADR-0047** (PR1–PR5 motor +
     PR7 bin real + PR6 la cara): el almacén opera y SE CONSULTA por racks
     de punta a punta.
  7. **PR7** (nuevo) — bin real. **✅ COMPLETO (2026-07-08).** **Subdividido
     (2026-07-07)** en dos por tamaño/riesgo:
     - **C7.1 — gestión de ubicaciones N4** (CRUD + permiso `almacen.ubicaciones.*` +
       pantalla): el admin/rol de almacén crea/edita/desactiva racks manualmente. La
       ÚNICA `es_default` y el trigger **no cambian**; los bins reales coexisten sin
       recibir inventario todavía. Bajo riesgo, sin migración de saldos ni de trigger.
     - **C7.2 — captura de bin real (Camino A completo):** `ubicacion_id` en los
       movimientos + los ~8 handlers que crean `MovimientoInventario` + FE de selección
       de bin. Reemplaza el enrutamiento a la ÚNICA por la ubicación real elegida en cada
       movimiento.

       > **Decisión cerrada (2026-07-08) — modelo de negocio del bin:** el
       > `ubicacion_id` va **en la línea** (`lineas_movimiento`), no en el encabezado
       > (una recepción puede repartir N artículos en N racks). Se **obliga bin** (no
       > fallback pasivo), con **asimetría**: las ENTRADAS exigen ubicación real —
       > prohibido entrar a la ÚNICA (`ENTRADA_A_UBICACION_UNICA`) — y además exigen
       > **asignación activa** del artículo a esa ubicación
       > (`ENTRADA_SIN_ASIGNACION`, validación de handler, patrón
       > `REORDEN_SIN_ASIGNACION`); las SALIDAS aceptan ubicación real **o la ÚNICA**,
       > para agotar el stock histórico. Un artículo puede vivir en varias ubicaciones
       > (el almacenista elige entre las asignadas). La ÚNICA solo se drena, nunca
       > recibe → **muere sola**; no hay migración/redistribución de saldos.
       >
       > **Sub-cut de C7.2 (2026-07-08), mismo criterio que C7.1 — el riesgo aislado:**
       > - **C7.2a — esquema + trigger + lecturas (backend puro, sin cambio de
       >   comportamiento observable):** `ubicacion_id UUID NULL` + FK en
       >   `lineas_movimiento` (sin backfill: histórico NULL = "vino por la ÚNICA");
       >   trigger con resolución bin-explícito/NULL→ÚNICA y las validaciones
       >   (`UBICACION_NO_PERTENECE_AL_SUBALMACEN`, `ENTRADA_A_UBICACION_UNICA`,
       >   `UBICACION_INACTIVA` — esta última solo entradas: un bin inactivo está
       >   vacío por el guardrail `UBICACION_EN_USO_CON_SALDO` de C7.1). Incluye
       >   hacer **deterministas ante N bins** las 9 lecturas/listados de saldo que
       >   asumían una fila por `(sub_almacén, artículo)`: costo snapshot por la
       >   ubicación golpeada (salida RQ, vale, matrev), remanente agregado
       >   (DiferenciaPrecio→Contabilidad), ponderado (reporte Alfak), snapshot de
       >   conteo agregado por artículo, adapter público de Compras (autorizar RQ),
       >   y los listados SaldosPage/ExistenciaMpCnk **agregados por sub-almacén**
       >   como puente — exponer el bin (columna de ubicación, grano por-ubicación)
       >   es decisión de UX **futura** (C7.2b+), NO de este PR. Nadie manda bin aún:
       >   las validaciones existen pero nadie las dispara hasta C7.2b.
       > - **C7.2b — captura de bin en los movimientos de captura humana (✅
       >   2026-07-08, un solo PR backend+FE).** Los **8 handlers** ahora aceptan
       >   `ubicacion_id` por línea: entradas (recepción factura/packing, devolución
       >   interna, reincorporación) validadas por el guard compartido
       >   `UbicacionEntradaGuard` (pertenece al sub-almacén + no-ÚNICA
       >   `ENTRADA_A_UBICACION_UNICA` + asignación activa `ENTRADA_SIN_ASIGNACION`);
       >   salidas (RQ, vale, devolución a proveedor —bin al **registrar**—, baja por
       >   daño) mandan el bin sin exigir asignación, y las 3 lecturas de costo
       >   snapshot pasan a llavear por el bin elegido (`?? ÚNICA` de fallback).
       >   Nuevo guardrail `ASIGNACION_A_UNICA` (no se asigna a la ÚNICA → el modelo
       >   de asignaciones contiene solo racks reales). FE: `UbicacionBinSelector` de
       >   dos modos — **asignación** (entradas, ubicaciones asignadas del artículo)
       >   y **saldo** (salidas, ubicaciones con existencia vía nueva query
       >   `GET /almacen/saldos/por-ubicacion`, incluida la ÚNICA). El conteo **NO**
       >   entra aquí (sigue a nivel sub-almacén, C7.2c).
       > - **C7.2b-bis — helper de cabecera nivel 4 en recepción (workstream
       >   almacén-por-línea PR4, 2026-07-23).** La captura por línea de C7.2b se
       >   mantiene intacta (sigue siendo obligatoria y validada por
       >   `UbicacionEntradaGuard`); lo que se agrega es **ergonomía**: un campo
       >   opcional *"Ubicación por defecto"* en la cabecera del
       >   `NuevaRecepcionSheet` que **auto-aplica el bin a las líneas que aún no
       >   tienen ubicación**. Las que ya traen otra **no se tocan** — se muestra un
       >   aviso informativo *"N líneas van a otra ubicación"* y las que coinciden
       >   llevan un badge *"coincide"*. Se persiste en
       >   `almacen.movimientos_inventario.ubicacion_helper_id` (**nullable**;
       >   `NULL` = *el almacenista no usó el helper*, dato para medir adopción).
       >   No participa en ninguna invariante: la ubicación que manda para saldos
       >   sigue siendo la de **cada línea**. El evento
       >   `almacen.oc_recepcion.registrada.v1` gana `UbicacionId` por línea en
       >   `LineaRecepcionPayload` — cambio **aditivo, sin bump de versión** (los
       >   consumidores actuales lo ignoran). El `SubAlmacenId` de la cabecera del
       >   evento se **retiró después, en 6c** (ver la nota del split 6a/6b/6c más
       >   abajo); ya no viaja en `almacen.oc_recepcion.registrada.v1`. PR4 **no** promueve
       >   `lineas_movimiento.ubicacion_id` a NOT NULL ni hace backfill: eso exige
       >   que las salidas también la persistan (PR5) y se consolida en PR6 junto
       >   con el DROP de la cabecera y la reescritura del trigger de saldos.
       >
       >   **Caveat de UX**: el helper aplica su valor a las líneas vacías **sin
       >   validar la asignación artículo-bin**. Si el bin del helper no está
       >   asignado al artículo de una línea concreta, el `UbicacionEntradaGuard`
       >   rechaza el submit con error de dominio (`ENTRADA_SIN_ASIGNACION`). El
       >   selector por fila (`UbicacionBinSelector` modo asignación) sí filtra
       >   bins válidos, así que la divergencia es detectable antes del envío. Es
       >   comportamiento **esperado** por el modelo de asignación artículo-bin,
       >   no defecto de UX.
       > - **C7.2c — conteo por rack (✅ 2026-07-08, 4 commits C1–C4):** baja el
       >   conteo físico a grano `(ubicacion, articulo)`. **C1** — migración de
       >   `lineas_conteo` (`ubicacion_id` + índice único) y snapshot por bin
       >   (revierte el GroupBy por artículo de C7.2a). **C2** — `AplicarConteo`
       >   genera cada ajuste con el bin explícito de la línea contada
       >   (`AjustePositivo`/`AjusteNegativo` golpean el rack, no la ÚNICA). **C3** —
       >   los DTOs de captura/comparación exponen `UbicacionClave` (JOIN, nunca el
       >   GUID en pantalla) y el FE agrega la columna Rack. **C4** — este cierre
       >   documental. Aislado por riesgo (esquema + ajustes que mueven saldo).
       >   Tratable en arranque nuevo (ÚNICA vacía → los ajustes positivos aterrizan
       >   en racks reales, no chocan con `ENTRADA_A_UBICACION_UNICA`). Tres notas de
       >   cierre:
       >   1. **Migración robusta ante tabla no vacía (backfill a la ÚNICA) —
       >      corregida tras fallo de deploy.** La versión original de
       >      `ConteoPorRack` agregaba `ubicacion_id NOT NULL` sin backfill y exigía
       >      `lineas_conteo` vacía; **falló en el deploy de #498 en Azure dev**
       >      (2026-07-09, `23502: column "ubicacion_id" contains null values` — la
       >      tabla tenía conteos previos). El `Up` se reescribió robusto (fix
       >      posterior): agrega la columna NULL, backfillea cada línea con la
       >      ubicación `es_default` (ÚNICA) de su `sub_almacen_id`, y recién
       >      entonces `SET NOT NULL`; un guard lanza `P0001` con mensaje claro si
       >      alguna línea no tuviera ÚNICA (en vez del `23502` críptico). Sirve
       >      igual en arranque nuevo (tabla vacía → el UPDATE no toca filas) que
       >      sobre BD con histórico. **La precondición "tabla vacía / cero conteos
       >      en vuelo" ya NO aplica.** Editar la migración ya mergeada es seguro: el
       >      esquema final es idéntico (misma columna/índice/FK), solo cambia el
       >      CÓMO llega a NOT NULL — Azure dev/prod nunca la aplicaron (rollback) y
       >      local ya está en el estado final. Verificado en BD scratch (no-vacía
       >      con backfill correcto + vacía + guard que aborta y hace rollback
       >      atómico).
       >   2. **Borde del ajuste negativo en conteo Rotativo — limitación
       >      conocida.** El `AjusteNegativo` descuenta el bin contado y está sujeto
       >      al guard `SALDO_*` por bin. En un conteo rotativo (parcial), el teórico
       >      del snapshot y el saldo vivo del rack pueden divergir si hubo
       >      movimientos entre el snapshot y la aprobación; un ajuste negativo
       >      calculado sobre el teórico viejo puede intentar dejar el bin en negativo
       >      y ser rechazado por el guard. En arranque nuevo / conteo total está
       >      contenido; en rotativo queda como borde conocido, no bloqueante del
       >      movimiento intermedio.
       >   3. **GUID de artículo/sub-almacén sigue crudo (fuera de alcance).** Solo
       >      la columna Rack se enriquece a clave legible por JOIN; artículo y
       >      sub-almacén siguen mostrando el GUID en captura/comparación, igual que
       >      antes de C7.2c. Enriquecerlos es trabajo aparte.
       > - **C7.3+ (diferido):** traspaso entre ubicaciones (no existe
       >   `TipoMovimiento.Traspaso`).
       >
       > **Precondición de deploy (arranque nuevo en producción):** antes de operar
       > por racks — (1) racks creados (C7.1), (2) asignaciones artículo→rack
       > cargadas (siembran la fila-en-0 en cada rack, base de lo que el conteo
       > inicial cuenta), (3) conteo físico inicial que siembre saldo real por rack.
       > Con la **ÚNICA vacía**, las entradas y el conteo inicial aterrizan directo
       > en racks. En un ambiente con stock previo en la ÚNICA (p.ej. DEV) hay que
       > **limpiar la ÚNICA / usar datos frescos** para probar C7.2b — es setup de
       > prueba, no cambio de diseño.
       > - **C7.2d — el sub-almacén sale de la cabecera del movimiento
       >   (workstream almacén-por-línea, PR6a):** completa el modelo N4 del lado
       >   del dato. `almacen.lineas_movimiento.ubicacion_id` pasa a **NOT NULL**
       >   (backfill de las líneas históricas a la ÚNICA de su sub) y el trigger
       >   **deja de leer `movimientos_inventario.sub_almacen_id`**: deriva el sub
       >   de la ubicación de la línea y retira el fallback "línea sin bin → ÚNICA"
       >   (ya imposible con el NOT NULL). La columna de cabecera se **elimina**;
       >   quien necesite el sub de un movimiento lo obtiene de la vista
       >   `almacen.v_movimiento_sub_almacen` (una fila por movimiento, `DISTINCT
       >   ON`, sub de la primera línea). El invariante "un movimiento = un
       >   sub-almacén" —que antes garantizaba la cabecera y del que depende esa
       >   derivación— lo impone ahora el trigger por-fila
       >   (`MOVIMIENTO_MULTI_SUBALMACEN`), reemplazando al viejo
       >   `UBICACION_NO_PERTENECE_AL_SUBALMACEN`. El check de pertenencia
       >   línea-vs-cabecera desaparece: sin cabecera, una línea única a cualquier
       >   bin es válida (su sub se deriva del bin). Se parte en 3 PRs por
       >   reversibilidad y ventana de mensajes: **6a** (Almacén-interno: NOT NULL
       >   + trigger + DROP cabecera + rewire de 4 lectores), **6b** (expand-contract
       >   de la columna espejo `RecepcionOcLocal.sub_almacen_id` en CxP —relajar →
       >   dejar de mapear → DROP, #715/#716/#717/#718—) y **6c** (Almacén deja de
       >   emitir `SubAlmacenId` en el evento; **HECHO**). ⚠️ **Dos espejos, no uno:**
       >   `almacen.oc_recepcion.registrada.v1` lo deserializan **CxP**
       >   (`ContratosEspejo.OcRecepcionRegistradaPayload`) **y Compras**
       >   (`ContratosEspejoAlmacen.OcRecepcionRegistradaAlmacenPayload`); 6c limpió
       >   ambos (Compras nunca lo persistió —campo muerto—, CxP venía de 6b-1). Los
       >   eventos `salida_requisicion.registrada.v1` y `ajuste_inventario.aplicado`
       >   también llevan `SubAlmacenId` de cabecera y quedan **follow-up**: antes de
       >   retirarlo, **enumerar SUS espejos** (cada consumidor tiene el suyo) para no
       >   repetir el punto ciego que costó una sorpresa en 6c. Precondición de deploy: cada sub-almacén con
       >   líneas históricas NULL debe tener su ubicación `es_default` antes del
       >   backfill (el guard `PR6A_BACKFILL_INCOMPLETO` aborta si falta).
       >   **Cambio de semántica del evento de salida (consecuencia de 6a):** en
       >   PR5, `LineaSalidaPayload.UbicacionId` (evento
       >   `almacen.salida_requisicion.registrada.v1`) se documentó como *"null =
       >   el almacenista no eligió bin"* (se publicaba el valor crudo del input).
       >   Como el `SET NOT NULL` prohíbe la línea sin bin, los handlers de salida
       >   (con RQ y por vale) **coalescen** el bin faltante a la ÚNICA del
       >   sub-almacén **antes** de persistir —igual que 8.B— con fallo ruidoso
       >   `SUBALMACEN_SIN_UBICACION_DEFAULT` si el sub no tiene ÚNICA. Desde 6a,
       >   entonces, `UbicacionId` del evento **siempre viaja poblado y significa
       >   el destino físico efectivo** (el bin elegido o la ÚNICA); ya **no**
       >   transporta la señal "no eligió". El campo no tiene consumidor aguas
       >   abajo, así que el cambio es interno; se registra aquí para que la
       >   lectura de PR5 no quede como vigente.
- **Estado intermedio (cut-A), PR2→PR4**: la PK de saldos pasa a `(ubicacion_id,
  articulo_id)` pero saldos **conserva `sub_almacen_id` como columna denormalizada**
  (+ su FK, `cantidad_reservada`, la generada `CantidadDisponible` y los 2 CHECKs).
  Así el código de reserva (SQL crudo llaveado por `sub_almacen_id`) sigue
  **intacto** durante el intervalo: la relación 1:1 sub-almacén↔ÚNICA garantiza una
  sola fila por `(sub_almacen_id, articulo_id)`. PR4 desmonta reservas y limpia esas
  columnas.
- **Docs a actualizar**: `docs/modulos/almacen/00-levantamiento.md` §3 (jerarquía
  a 4 niveles), `01-diseno.md` (A4 saldos, A19 reservas → eliminadas), y crear la
  sección de asignación/reorden.
- **Identidad**: confirmar/instanciar el usuario de servicio para el reorden
  (patrón D3 de la integración A+W).
- **PLATFORM-TODO** a cerrar: `<ReservaEntregaPostRecepcion>` (queda sin objeto).
- **ADRs hijo probables**: modelo de la RQ de origen sistema (requisitante
  no-humano) si resulta más complejo de lo previsto; conversión unidad
  inventario↔compra en el reorden cuando aterrice ADR-0046 Etapa 4.
