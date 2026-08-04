# ADR-0043: Entrega post-recepción de requisiciones — la RQ cierra por entrega, no por cubrimiento

- **Estado**: Propuesta
- **Fecha**: 2026-06-12
- **Decisores**: Eduardo Paredes (owner, decisión de negocio), Victor (compras/almacén), Claude (análisis)
- **Etiquetas**: almacen, compras, requisiciones, máquina-de-estados, triada, salidas, recepciones

> Cierra un hueco sistémico entre el diseño (levantamiento de Almacén §6.7) y la
> implementación. Se apoya en patrones ya establecidos: lectura de presentación
> **state-agnostic** vía read-port ([ADR-0042](./0042-enriquecer-dtos-con-nombres-cross-modulo.md)),
> **RBAC por operación** y permiso-por-rol ([ADR-0041](./0041-autorizacion-por-operacion-y-lectura-de-catalogos.md),
> [ADR-0007](./0007-autorizacion-rbac-granular.md)), **Outbox/Service Bus** para
> eventos de integración ([ADR-0009](./0009-outbox-pattern-eventos-integracion.md))
> y la convención de **deuda de plataforma** ([ADR-0031](./0031-deuda-de-plataforma-y-stubs-noop.md)).

## Contexto y problema

Una requisición (RQ) de almacén puede cubrirse en parte desde stock
(`CantidadDeAlmacen`, que se reserva al autorizar) y en parte por compra
(`CantidadDeCompra`, que genera una OC). Cuando llega la mercancía de esa OC se
le da **recepción** y entra al sub-almacén. El diseño previó (levantamiento de
Almacén §6.7) que entonces *"se notifica al solicitante original que ya puede
recoger el saldo de su RQ"* — es decir, una **entrega** posterior por el flujo
de Salidas. La implementación no cerró esa pata:

- El **cubrimiento y la recepción cierran la RQ**. `Cubrimiento.CantidadPendiente
  = CantidadOriginal − CantidadDeAlmacen − CantidadRecibida`
  ([`Cubrimiento.cs`](../../backend/src/Compras/Domain/Cubrimiento.cs)); al
  recibirse la OC, `Requisicion.RegistrarRecepcion` deja todas las líneas en
  `CantidadPendiente == 0` y transiciona la RQ a `Cerrada`
  ([`Requisicion.cs:567-593`](../../backend/src/Compras/Domain/Requisicion.cs)).
  `Cerrada` hoy significa **"cubierto"**, no "entregado".
- El **pendiente de entregar ignora lo recibido**.
  `CantPendienteEntregar = CantDeAlmacen − CantEntregadoDeAlmacen`
  ([`ObtenerRequisicionPorIdHandler.cs:105-129`](../../backend/src/Compras/Application/ObtenerRequisicionPorId/ObtenerRequisicionPorIdHandler.cs),
  default Mapster en [`ComprasMapsterConfig.cs:35-36`](../../backend/src/Compras/Application/ComprasMapsterConfig.cs)).
  Una línea 100 % compra tiene `CantDeAlmacen = 0`, así que su pendiente es
  siempre 0 — el material recibido **nunca** entra al cálculo.
- La **UI filtra** esas líneas (`.filter(f => f.pendienteEntregar > 0)`,
  [`nueva-salida-helpers.ts`](../../frontend/src/features/almacen/components/nueva-salida-helpers.ts))
  y muestra el banner *"esta RQ no tiene líneas pendientes de entregar… no
  tienen CantDeAlmacen (cubierto por compras vía OC)"*.
- El **adapter de lectura operativa bloquea** RQs `Cerrada`: `ObtenerAsync`
  devuelve `null` salvo `Autorizada`/`EnSurtido`
  ([`ComprasRequisicionReadAdapter.cs:52-56`](../../backend/src/Compras/Infrastructure/PublicAdapters/ComprasRequisicionReadAdapter.cs)).
- La recepción **no reserva**: el material recibido entra como **stock libre**;
  cualquier otra salida puede consumirlo.

**Caso real (tester, en Azure):** RQ **MID2026-000012**, 3 líneas / 3 artículos
— L1 surtida de stock (ya entregada), L2 y L3 cubiertas por compra → OC →
recepcionadas. La RQ aparece en el selector de Nueva salida pero rinde **cero
líneas seleccionables**; no hay forma de entregar el material que sí llegó.

Es un hueco **sistémico**: aplica a toda RQ cubierta total o parcialmente por
compra (cada vez que no hay stock suficiente al autorizar). No es un bug de
pantalla ni un problema de capacitación: no existe ninguna ruta funcional.

## Drivers de la decisión

- Que el material comprado para una RQ llegue al solicitante por el **flujo
  normal de Salidas**, con trazabilidad a la RQ.
- Que **`Cerrada` signifique algo verdadero** (entregado al solicitante), no
  "cubierto por el proveedor".
- Dar al **jefe de almacén** una vía de cierre administrativo para RQs que el
  requisitante ya no necesita, sin que el material entregado/no entregado quede
  en un limbo.
- Respetar los límites de la triada Compras↔Almacén↔CxP (cero acceso a tablas
  ajenas; solo puertos de lectura o eventos) y el RBAC por permiso.
- **Strangler-fig / deploy incremental**: poder desplegar la serie por PRs sin
  ventanas en las que una RQ no pueda cerrar por ninguna vía.
- **No sobre-construir**: la reserva al solicitante se difiere; primero se
  resuelve la entrega.

## Opciones consideradas

1. **A** — Conectar `CantidadRecibida` al pendiente de entrega; la RQ no cambia
   de estado (sigue `Cerrada`, leída state-agnostic para entrega).
2. **B** — La RQ **no cierra hasta entregar**: el cierre lo dispara la entrega,
   no el cubrimiento; `Cerrada` pasa a significar "entregado". *(Elegida, con A
   embebida como mecánica del pendiente.)*
3. **C** — Reserva automática al recibir (apartar el material para el
   solicitante). *(Diferida.)*
4. **D** — Entrega directa en recepción (recepción + salida en un paso).
5. **E** — Vale urgente como vía oficial, con regularización real.

## Decisión

Se adopta **B (cierre por entrega) con la mecánica de A (el pendiente incluye lo
recibido) y un cierre manual nuevo**. La reserva (C) se **difiere**.

### Decisión de negocio (Eduardo)

- **R1 — La RQ no cierra al cubrirse por compra.** Si parte se surte y parte no,
  queda en **surtido parcial**; cuando llega el material (recepción) se **termina
  de surtir** por el flujo de Salidas. La RQ solo queda completamente **`Cerrada`
  cuando todo se entregó** al solicitante.
- **R2 — El material recepcionado queda libre (sin reserva), por ahora.** La
  reserva al solicitante (opción C) se **difiere** como follow-up; no se
  implementa en esta serie. Es un **riesgo aceptado** consciente (ver sección).
- **R3 — Cierre manual.** Para RQs no surtidas o surtidas parcialmente, el jefe
  de almacén puede cerrarlas si el requisitante ya no necesita el material, con
  dos estados terminales nuevos: **"Cerrada sin surtir"** (no se entregó nada) y
  **"Cerrada surtida parcialmente"** (se entregó una parte). En ambos, el
  material no entregado queda disponible como stock.

### Decisiones de modelado

1. **`EnSurtido` resignificado + badge "parcial" calculado.** No se agrega un
   estado no-terminal nuevo: `EnSurtido` cubre **toda la fase de surtido/entrega**
   (autorizada con saldo a OC, material recibido pendiente de entregar, entrega
   parcial). La "parcialidad" se **deriva de los datos de avance**
   (`entregado/original`) y se muestra como un badge/etiqueta calculada, no como
   un estado de la máquina. Se evita inflar el enum y duplicar transiciones.

   > **Concreción del badge calculado (serie de etiqueta de situación, post-#403).**
   > El "badge calculado" se concreta como **`SituacionSurtido`**, un enum
   > **derivado server-side** (no es un estado de la máquina; no toca
   > `EstadoRequisicion` ni las transiciones), con **3 valores** y precedencia
   > **`SurtidoParcial` > `ListoParaSurtir` > `EsperandoCompra`**:
   > - **EsperandoCompra** — nada entregado y nada disponible (todo en compra).
   > - **ListoParaSurtir** — nada entregado pero hay material disponible sin
   >   entregar (`Σ CantidadPendienteEntregar > 0`).
   > - **SurtidoParcial** — algo entregado (`Σ CantidadEntregada > 0`) sin cerrar.
   >
   > Solo tiene valor cuando `estado == EnSurtido` (null en otros estados). La
   > derivación es **fuente única**: una función pura del dominio
   > (`SituacionSurtidoDerivacion.Derivar`) que reusa el criterio de
   > `clasificarEntrega`; la consumen el list-query (sumas en SQL `GROUP BY`) y el
   > detalle (sumas en memoria de las líneas), garantizando cero drift. Se expone
   > como campo nullable en el list-DTO y el detail-DTO. La **presentación**
   > (reemplazar "En surtido" por la situación en bandeja + detalle, vía
   > `EstadoBadge` situación-aware) es una fase de frontend separada (PR2).

2. **Tracking de entrega: A2 con `LineaRqId` (acumulador en dominio).** El cierre
   por entrega total es una transición del agregado RQ, así que el dominio debe
   ser **dueño de cuánto se entregó**. Hoy **no existe canal Almacén→Compras de
   entregas**: el evento `almacen.salida_requisicion.registrada.v1` se publica
   pero **no tiene consumidor** ([`AlmacenEventListenerWorker.cs`](../../backend/src/Compras/Infrastructure/Workers/AlmacenEventListenerWorker.cs)
   solo maneja recepciones), y su payload de línea lleva solo `ArticuloId`, no
   `LineaRqId` ([`SalidaRequisicionRegistradaIntegrationEvent.cs`](../../backend/src/Almacen/Application/Integration/SalidaRequisicionRegistradaIntegrationEvent.cs)).
   Por eso A2 implica:
   - persistir **`LineaRqId` en `LineaMovimiento`** (hoy el comando de salida lo
     recibe y lo **descarta** — [`RegistrarSalidaConRequisicionCommand.cs:208-216`](../../backend/src/Almacen/Application/Salidas/RegistrarSalidaConRequisicionCommand.cs));
   - **enriquecer el evento** de salida con `LineaRqId`;
   - **crear el consumidor** en Compras → comando `RegistrarEntrega` →
     `Requisicion.RegistrarEntrega`, con acumulador **`CantidadEntregada`** por
     línea en `LineaRequisicion`. Es el espejo de la cadena ya existente de
     recepción (`OcRecepcionRegistradaEvent → OcRecepcionRegistradaListener →
     Requisicion.RegistrarRecepcion`).

   El pendiente de entregar pasa a ser
   `(CantDeAlmacen + CantRecibida) − CantidadEntregada`, calculado en dominio
   (resuelve además la atribución arbitraria por artículo del cálculo read-time).
   Descartada **A1 (read-time)**: muestra el pendiente pero **no puede disparar
   el auto-cierre** porque el agregado no sería dueño del dato.

3. **OC en vuelo al cerrar manual: permitir CON AVISO (nunca bloquear).** El
   cierre manual no se bloquea jamás. Si la RQ tiene una **OC en vuelo** (material
   pedido al proveedor aún no recibido), el diálogo de confirmación **advierte
   antes**: *"esta RQ tiene una OC en vuelo por X piezas que llegarán como
   stock"*, y el jefe decide si continúa. La OC **sobrevive** al cierre: su ciclo
   (recepción/facturación/pago/cierre) es **agnóstico al estado de la RQ** — el
   cierre de OC lo evalúa Compras por sus 3 sub-estados
   ([`OrdenCompra.cs:1440-1461`](../../backend/src/Compras/Domain/Oc/OrdenCompra.cs)),
   sin leer el estado de la RQ. El material sigue llegando y entra como stock
   libre. Requiere un **query de detección** de OC pendiente de recibir + el
   **confirm** en el frontend (van en el PR del cierre manual).

4. **Motivo del cierre manual: catálogo `MotivosRechazo` + bit `CierreManual`.**
   Se reusa el patrón de Cancelar/Rechazar (componente `ModalMotivo` +
   `MotivosRechazo` con bitmask `AplicaA`): motivo estructurado del catálogo +
   texto libre opcional. Plantilla directa: el flujo de cancelar RQ
   ([`CancelarRequisicionHandler.cs`](../../backend/src/Compras/Application/Cancelar/CancelarRequisicionHandler.cs)).

### Permiso

Permiso nuevo y específico **`compras.requisiciones.cerrar-manual`** (el permiso
**sigue al agregado**: la RQ vive en Compras aunque la ejecute el jefe de
almacén — convención del manifiesto [`PermisosCanonicos.cs`](../../backend/src/Identidad/Domain/PermisosCanonicos.cs)).
Se **asigna por rol** (ADR-0041): el seed se lo concede al rol **jefe-de-almacén**;
cualquier otro rol que lo necesite a futuro lo recibe vía administración de roles,
**sin tocar código**. No se hardcodea a ningún rol: el gate del endpoint y del
botón es el permiso, como en todo el ERP.

## Modelo de estados resultante

| Estado | Significado nuevo |
|---|---|
| Borrador / EnAutorización / Autorizada | sin cambio |
| **EnSurtido** | toda la fase de surtido/entrega: reservado de stock y/o esperando OC y/o material recibido pendiente de entregar. Estado desde el que opera Salidas. La parcialidad es un badge calculado. |
| **Cerrada** | **todo entregado al solicitante** (antes: "cubierto") |
| **Cerrada sin surtir** *(nuevo, terminal)* | cierre manual con `CantidadEntregada == 0` |
| **Cerrada surtida parcialmente** *(nuevo, terminal)* | cierre manual con `0 < entregado < original` |
| Cancelada / Rechazada / Eliminada | sin cambio |

**Transiciones que cambian:**

- `RegistrarCubrimiento` deja de tener el path directo a `Cerrada` (hoy cierra si
  `todoCubierto && !hayCompra`): siempre → `EnSurtido`.
- `RegistrarRecepcion` **deja de cerrar**: solo incrementa `CantidadRecibida`; la
  RQ permanece `EnSurtido`. Se silencia el `LogError` de la propagación
  recepción→RQ que hoy exige `EnSurtido`
  ([`OcRecepcionEnAlmacenCommand.cs:161-173`](../../backend/src/Compras/Application/Almacen/OcRecepcionEnAlmacenCommand.cs)).
- **Nueva** `RegistrarEntrega` (vía el canal A2): incrementa `CantidadEntregada`;
  si todas las líneas quedan `entregado == original` → `Cerrada`. Debe **tolerar
  RQs en estado terminal** (acumula idempotente; solo transiciona si `EnSurtido`),
  para la convivencia con el cierre viejo durante el despliegue (ver Notas).
- **Nueva** `CerrarManual` (jefe de almacén) → `CerradaSinSurtir` o
  `CerradaSurtidaParcial` según `CantidadEntregada`. Libera la reserva del tramo
  de stock (`linea.ReservaId`, vía `ILiberarReservaPort.LiberarAsync`,
  idempotente); el tramo comprado nunca tuvo reserva.

## Riesgo aceptado: reserva diferida

Con la opción C diferida, **el material recibido sigue siendo stock libre** entre
que llega y que el solicitante lo recoge: otra salida (otra RQ, o un vale
urgente) puede consumirlo. Eduardo lo acepta conscientemente **"por el momento"**.
Se registra como deuda de plataforma (ADR-0031): **PLATFORM-TODO
`<ReservaEntregaPostRecepcion>`** en el handler de recepción, con ticket de
follow-up para implementar la reserva automática al recibir (la opción C: el
agregado `ReservaStock` ya soporta atar la reserva a la RQ vía
`(DocumentoOrigenTipo, DocumentoOrigenId)` sin cambio de esquema; falta llevar el
vínculo OC→RQ→solicitante a la proyección que Almacén lee).

## Consecuencias

**Positivas**

- El material comprado para una RQ se entrega al solicitante por el flujo normal
  de Salidas, con trazabilidad por línea (`LineaRqId`).
- `Cerrada` pasa a significar "entregado": el estado deja de mentir. El selector
  de Salidas ya no necesita la lectura state-agnostic de RQ `Cerrada` (la RQ se
  queda `EnSurtido` mientras haya algo por entregar).
- El jefe de almacén tiene una vía administrativa de cierre, auditada (motivo de
  catálogo) y consciente (aviso de OC en vuelo).
- Se cierra un canal Almacén→Compras (entregas) que estaba a medio construir (el
  evento se emitía sin consumidor).

**Negativas / trade-offs**

- **Cambio en la máquina de estados de la RQ** (contrato transversal): ~40–50
  sitios productivos + 3–4 migraciones EF + 1 migración de datos, repartidos
  Compras/Almacén/Identidad/frontend. Es ~2× el costo de un solo estado nuevo.
- `RequisicionCerradaEvent` **cambia de momento** (de cubrir/recibir a entregar
  todo). No tiene consumidores de negocio (solo logging + outbox para BI futuro),
  pero **rompe los E2E del outbox** que asertan cuándo se emite y cambia la
  semántica para BI.
- **Riesgo aceptado** (R2): el material recibido es stock libre hasta que C se
  implemente.
- Durante el despliegue conviven dos caminos de cierre (viejo y nuevo); requiere
  que `RegistrarEntrega` tolere RQs terminales (ver Notas / secuenciación).

## Descartadas

- **A sola (la RQ sigue `Cerrada`, leída state-agnostic)**: resuelve la entrega
  con menor blast-radius, pero deja `Cerrada` significando "cubierto" y no da la
  base para el cierre manual ni para que la RQ refleje el avance de entrega.
  Eduardo prefirió que el estado diga la verdad (R1).
- **A1 (pendiente read-time, sin acumulador)**: no puede disparar el auto-cierre
  porque el dominio no sería dueño del "entregado"; el cierre quedaría siempre
  manual, contra R1 ("se cierra cuando todo se entregó").
- **Estado no-terminal `SurtidoParcial` nuevo**: más explícito en bandejas, pero
  suma un estado y más transiciones/sitios; la parcialidad se cubre con un badge
  calculado sobre `EnSurtido`.
- **Reserva ahora (opción C)**: deseable pero no urgente; se difiere para no
  agrandar la serie. Riesgo aceptado documentado.
- **Entrega directa en recepción (D)**: no ayuda al caso ya recibido
  (MID2026-000012) y choca con la operación real (quien recibe del proveedor ≠
  el solicitante, que recoge después). Queda como posible fast-path futuro.
- **Vale urgente como vía oficial (E)**: invierte el modelo (usar un mecanismo
  "sin RQ" para algo que sí tiene RQ) y su regularización es hoy casi un no-op.
  Sirve como parche manual, no como diseño. **Decisión: el caso del tester NO se
  saca con vale; espera la serie** (ver Notas).
- **OC en vuelo: bloquear el cierre**: evitaría material "huérfano" pero rompe el
  caso de uso (el requisitante ya no lo necesita aunque la OC siga). Se eligió
  permitir-con-aviso. **Permitir-sin-aviso** se descartó por dejar al jefe sin
  contexto de que llegará material como stock.

## Notas de implementación

### Descomposición en PRs y secuenciación

El cambio se parte en **4 PRs**. La restricción dura es: **quitar el auto-cierre
viejo debe desplegarse DESPUÉS de que el cierre nuevo (canal de entrega) esté
live** — si no, entre deploys habría RQs que no cierran por ninguna vía. Por eso
la serie se **reordena** (no se mergea como par): el canal y el cierre nuevo van
primero, de forma **aditiva y dormida** (el cierre viejo sigue ganando hasta que
se retire), y la conmutación va al final.

1. **PR #1 — Fundación aditiva + canal de entrega + cierre nuevo (sin quitar el
   viejo).** Incluye este ADR (commit docs). Migraciones EF: enum + CHECK con los
   2 terminales (definidos, sin usar aún), `CantidadEntregada` en
   `LineaRequisicion`, `LineaRqId` en `LineaMovimiento`. Persistir `LineaRqId`
   desde el comando de salida; enriquecer el evento; consumidor en Compras
   (de un salto: dedup `EventoProcesado` + mutación de RQ en la misma TX);
   `Requisicion.RegistrarEntrega` con auto-cierre por entrega total **guardado a
   `EnSurtido`** (tolerante a terminales; acumula y avisa sin lanzar si excede el
   techo). El **espejo FE** se limita a los 2 estados terminales nuevos
   (labels/colores/glosario + regen del contrato de enums) para mantener verde el
   guard cross-language; **NADA los alcanza aún**. **El auto-cierre viejo sigue
   activo**: el cierre nuevo queda dormido (las RQ cubiertas por compra aún
   cierran temprano por recepción — bug actual, sin regresión). Deployable solo.
   **Fuera de #1 (van en #3, ver abajo):** la fórmula nueva de
   `CantPendienteEntregar` (que suma `CantidadRecibida`), la lectura
   state-agnostic de RQ `Cerrada` y el FE de Nueva salida (badge "parcial",
   pintar líneas recibidas). Razón: sin quitar el cierre viejo esas piezas son
   inertes en la ventana (la RQ comprada sigue `Cerrada`-bloqueada) y la lectura
   state-agnostic sería superficie desechable tras #3 (la RQ queda `EnSurtido`,
   surtible por la lectura operativa normal). #1 se mantiene riel puro.
2. **PR #2 — Cierre manual.** Comando/handler/endpoint `cerrar-manual`; permiso
   `compras.requisiciones.cerrar-manual` + seed al rol jefe-de-almacén; bit
   `CierreManual` en `MotivosRechazo` + variante `ModalMotivo`; query de
   detección de OC en vuelo + confirm FE con aviso; FE de la acción y de los 2
   badges terminales. Depende solo del enum de #1; independiente de la
   conmutación de #3.
3. **PR #3 — Conmutación: quitar el auto-cierre viejo + encender la entrega de lo
   comprado.** `RegistrarCubrimiento` y `RegistrarRecepcion` dejan de cerrar; el
   único cierre automático pasa a ser el de entrega (live desde #1). Silenciar el
   `LogError` de la propagación recepción→RQ. **Aquí entran las piezas de
   presentación diferidas de #1**: fórmula nueva de `CantPendienteEntregar` que
   suma `CantidadRecibida`; FE de Nueva salida (pintar las líneas recibidas, badge
   "parcial" calculado sobre `EnSurtido`); resignificar `EnSurtido`. Con el cierre
   viejo retirado, la RQ comprada se queda en `EnSurtido` (surtible por la lectura
   operativa normal), así que **no hace falta lectura state-agnostic**. PR
   acotado y revertible — su reemplazo (el cierre nuevo) ya está desplegado.

   > **Ejecución (PR #3):** **sin migración de esquema** — puro cambio de
   > comportamiento (las columnas `cant_recibida`/`cant_entregada` ya existen
   > desde #1). Decisión de implementación: el cubrimiento **100% stock** se
   > reencauza a `EnSurtido` (no se deja en `Autorizada`) — si no, el cierre
   > nuevo (que solo dispara desde `EnSurtido`) nunca lo alcanzaría. El
   > `LogError` de la propagación recepción→RQ queda **moot**: al no cerrar la
   > recepción, la RQ se queda `EnSurtido` y la propagación nunca topa una RQ
   > terminal. Alcance reducido: no se toca `ComprasRequisicionReadAdapter`;
   > solo se quita `Cerrada` de `RequisicionSelector.DEFAULT_ESTADOS` y se
   > limpia el `estadosValidos` muerto del handler de salida.
4. **PR #4 — Migración de históricas (desbloquea al tester).** Job one-time;
   corre tras #3 (régimen final). Reclasifica las RQ hoy `Cerrada`. Despliega en
   sucesión cercana a #3.

> El PR de ADR/docs que se había contemplado aparte se **funde en #1**.

### Criterio de migración de históricas

Backfill de `CantidadEntregada` desde los movimientos `SalidaConsumo` por RQ
(lógica de [`AlmacenEntregasReadAdapter.cs`](../../backend/src/Compras/Infrastructure/PublicAdapters/AlmacenEntregasReadAdapter.cs)).
Para cada RQ hoy `Cerrada`:

| Condición | Migra a |
|---|---|
| `entregado == original` (entrega completa real) | **Cerrada** (nuevo significado, consistente) |
| `entregado < original` (recibido/cubierto sin entregar) — **caso MID2026-000012** | **EnSurtido** (vuelve a ser surtible; L2/L3 entregables) |

Las Cancelada/Rechazada/Eliminada **no se tocan**, ni los terminales del cierre
manual del #400 (`CerradaSinSurtir`/`CerradaSurtidaParcial` — decisión explícita
del jefe). Las `EnSurtido` reciben **solo backfill** de `CantidadEntregada` (sin
reclasificar). El backfill lee movimientos del esquema `almacen` para decidir
estado en `compras`: hacerlo como **job de reconciliación one-time**, no como
migración EF pura (cada módulo es dueño de su esquema).

> **Ejecución (PR #4):** **sin migración de esquema** (puro job de datos;
> revertible). `MigracionEntregaHistoricaJob` (`BackgroundService` one-shot)
> dormido por config (`Migraciones:EntregaHistorica:Enabled=false`).
>
> - **Dos grupos (riesgo del punto 2 — salidas pre-#399 sin `LineaRqId`).** El
>   clasificador separa cada RQ en **EXACTA** (atribuible por línea: salida con
>   `LineaRqId`, o artículo en una sola línea aunque sea pre-#399) o **AMBIGUA**
>   (artículo repetido + salida sin `LineaRqId` → reparto indecidible). Solo la
>   EXACTA se migra automático; la AMBIGUA se **emite a log y NO se toca**
>   (revisión manual caso por caso).
> - **Cap al techo.** `CantidadEntregada` por línea = `min(entregado, Cantidad)`.
>   Una RQ histórica puede tener sobre-entrega; sin cap, el pendiente
>   `(almacén+recibida)−entregada` daría negativo.
> - **Idempotente sin tabla marcador.** SET (no incrementa) + filtro de estado:
>   re-correr es inocuo y salta las ya movidas. Auditoría = log estructurado
>   (App Insights).
> - **Dry-run.** `DryRun=true` (default) reporta qué haría (movidas/quedan/
>   ambiguas con folios) **sin escribir**; `DryRun=false` aplica con log fuerte.
> - **Runbook (apagado).** (1) deploy con `Enabled=false`; (2) correr los 3
>   queries de diagnóstico (gate — si hay ambiguas, revisarlas); (3) `Enabled=true`
>   + `DryRun=true` → revisar reporte; (4) `DryRun=false` → aplica; (5) **tras la
>   corrida real exitosa, `Enabled=false` de nuevo** para que un redeploy/reinicio
>   no re-arme el job.

### Caso de aceptación end-to-end

**MID2026-000012 no se saca con vale; espera la serie.** Tras el PR #4 la
migración la regresa a `EnSurtido` y el tester debe poder **entregar las 2 líneas
recibidas por el flujo normal de Salidas**, cerrando la RQ al completar la
entrega. Hasta entonces el material sigue como stock libre (riesgo aceptado R2).
Este es el criterio de aceptación de la serie completa.

### Archivos / piezas clave (orientativo)

- Dominio Compras: `Requisicion` (transiciones, `RegistrarEntrega`,
  `CerrarManual`), `LineaRequisicion` (`CantidadEntregada`), `EstadoRequisicion`
  (+2 terminales), eventos (`RequisicionCerradaEvent` recolocado,
  `RequisicionCerradaManualmenteEvent`).
- Almacén: `LineaMovimiento` (`LineaRqId` + config EF), comando de salida
  (propagar `LineaRqId`), `SalidaRequisicionRegistradaIntegrationEvent` (payload).
- Canal: `AlmacenEventListenerWorker` (Compras, nuevo case) → `RegistrarEntrega`.
- Identidad: `PermisosCanonicos` (+`compras.requisiciones.cerrar-manual`) +
  migración seed + asignación al rol jefe-de-almacén (datos) + espejo
  `frontend/src/lib/auth/permission-codes.ts`.
- Frontend: `EstadoRequisicion`/`ESTADO_LABELS`/`ESTADO_KEYS`, `EstadoBadge`,
  `glosario`/`Ayuda`, `acciones-disponibles`/`AccionesRequisicion`/`useWorkflow`,
  `ModalMotivo` (variante), `nueva-salida-helpers`/`NuevaSalidaSheet`,
  `RequisicionSelector`, contrato de enums (`compras-enums.contract.json`).
- PLATFORM-TODO `<ReservaEntregaPostRecepcion>` en el handler de recepción.
