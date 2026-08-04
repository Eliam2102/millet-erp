# ADR-0050: Consumo del centro de costo de máquina (Dim3) en los documentos de compras

- **Estado**: Aceptada
- **Fecha**: 2026-07-18
- **Decisores**: Eduardo Paredes, Victor
- **Etiquetas**: centros-costo, compras, almacén, consumo, alcance, reportería

## Contexto y problema

Cerrado el catálogo de Centros de Costo (Dim1 → Dim2 → Dim3, planta → área →
máquina) y su asignación tri-estado por usuario (CECO-PR1..6), la **Fase E**
lo pone a trabajar: cada línea de los documentos de la cadena de compras
debe llevar su **CC-Máquina** (el Dim3, la máquina que "paga" el gasto).

El campo no es homogéneo entre documentos. En unos **el usuario elige** la
máquina; en otros **la hereda** de un documento aguas arriba. Y quien elige
no siempre es el dueño del gasto: un almacenista despacha material urgente
para cualquier área; un comprador levanta una OC sin requisición para
cualquier departamento. El alcance del usuario (qué máquinas tiene asignadas)
sirve para **restringir a un dueño lo que puede cargarse a sí mismo** — pero
aplicado a un despachador lo **bloquearía** para operar sobre áreas ajenas.

Se necesita una regla única que diga, por documento: **quién elige, filtrado
por qué, o de quién hereda**; **cómo se resuelve el nombre para mostrarlo**
sin que el alcance filtre la *lectura*; y **qué garantías de confianza** tiene
cada dato. La máquina (Dim3) es además un **catálogo independiente**: no
cuelga de la sucursal ni del departamento del ERP (separación total, #620),
así que **no hay eje que cruce un departamento con sus máquinas** — una
consecuencia de diseño que condiciona el selector.

## Decisión

Tres **semánticas de captura** del CC-Máquina, elegidas por la relación entre
el capturador y el gasto, más la regla de resolución de nombre para el
display. Se aplican a los **cuatro documentos** en alcance (RQ, OC, entrada,
salida); **nota de crédito y entrada manual quedan fuera** (diferidas).

### 1. Elegido-filtrado — el dueño elige (RQ)

El **requisitante** (`compras.requisiciones.crear`) captura la RQ para su
propia área. El selector se **filtra por su alcance** (`AlcanceDim3Evaluator`,
como hoy `BuscarDim3Query`): solo ofrece sus máquinas. **Obligatorio.**
Prellenado si su alcance resuelve a **exactamente 1** máquina. Es el único
origen donde el capturador **es** el dueño del gasto, y por eso el único donde
el alcance restringe con sentido.

### 2. Elegido-abierto-por-proxy — un despachador elige (vale + línea manual de OC)

**Un solo concepto, dos instancias.** El capturador **no es el dueño del
gasto**: despacha o compra para terceros, sin documento del cual heredar.

- **Vale urgente** (salida sin RQ): el **almacenista**
  (`almacen.salidas.por-vale`) elige de **todas** las máquinas activas.
- **Línea manual de OC** (OC sin RQ, FOC11): el **comprador**
  (`compras.ordenes.crear-sin-rq`) elige de **todas** las máquinas activas.

El selector va **abierto** (sin filtro de alcance): el despachador debe poder
cargar el gasto a cualquier área, igual que el almacén debe poder **recibir**
mercancía de un área que no es suya. **Obligatorio** en ambos.

**El backend es quien abre el selector, no el frontend.** El selector abierto
vive detrás de un **endpoint gateado por el permiso que ya identifica a la
población proxy** (`almacen.salidas.por-vale` / `compras.ordenes.crear-sin-rq`).
Un requisitante cuyo FE llame ese endpoint recibe **403** — no puede saltarse
su alcance eligiendo la URL. No se introduce un permiso nuevo (redundante:
esos permisos ya nombran exacto a la población) ni se autoriza por rol
(violaría el RBAC granular por permiso del ADR-0007). Es el mismo patrón con
que se resolvió `esAlcanceTotal` en FE-PR3: la autorización se resuelve en el
**endpoint** y el módulo CentrosCosto sigue **dependency-free** (#620) — la
query abierta ni siquiera usa el evaluador de alcance.

### 3. Heredado-solo-lectura — el dato viaja con el documento (OC-desde-RQ, entrada, salida-con-RQ)

Cuando el documento **nace de otro** que ya trae la máquina, el valor **se
copia y viaja con él**; aguas abajo es **solo lectura, sin alcance, ni
siquiera selector**:

- **OC desde RQ** hereda de la línea de RQ.
- **Entrada** (recepción) hereda de la línea de OC (vía `oc_linea_id` de
  cabecera + match por artículo como fallback).
- **Salida con RQ** hereda de la línea de RQ (vía `LineaRqId` por renglón;
  fallback a `RqId` de cabecera + artículo donde el renglón no lo trae).

El alcance **no** aplica: un almacenista debe poder recibir mercancía de
Laminado aunque no tenga esa máquina asignada. Editable solo permitiría
reasignarlo a *sus* máquinas — justo lo que no se quiere. Es restricción de
captura, **no de dato**: el alcance filtra el *selector* del origen, nunca el
*campo* heredado.

### Resolución de nombre para el display — ver ≠ elegir

Mostrar el CC-Máquina en un documento (detalle, impresión, contexto) **nunca**
reusa el selector con alcance. Se resuelve por un **read-port batch dedicado**
(patrón ADR-0042), **sin filtro de alcance** y **resolviendo inactivas**
(ADR-0049: una máquina dada de baja debe seguir mostrando su nombre en un
documento histórico). Sin este puerto, un aprobador que no posea esa máquina
en su alcance vería "No catalogado" sobre un dato perfectamente válido. El
selector filtra *lo que puedes elegir*; la resolución de nombre muestra *lo
que el documento ya dice*. Son caminos distintos.

**Display = `clave — nombre`, sin jerarquía** (CC-G4 cerrado). La jerarquía
(planta → área → máquina) es material de los **reportes de la Fase F**, no del
documento operativo.

### Consecuencia aceptada — la captura por proxy es dato de MENOR confianza

Va **sí o sí** en este ADR: en la captura por proxy (vale + línea manual de
OC), el despachador elige a mano de las **~361** máquinas **sin nada que lo
guíe** — el catálogo de máquinas es independiente y no hay eje del ERP
(departamento, sucursal) que lo acote. Por tanto:

- **La línea manual de OC (y el vale) son dato de menor confianza que lo
  heredado de una RQ.** Un reporte de la Fase F que no cuadre ahí **no es un
  bug**: es esta consecuencia.
- **Mitigación**: el dato se revisa/reasigna en el **consumo** (la salida es
  donde la máquina se conoce de verdad), no se trata la captura del comprador
  como verdad final.

### Divergencia contable del vale regularizado

El CC-Máquina del vale **no se sobreescribe al regularizar**. La regularización
solo liga vale↔RQ y cierra el SLA de 48h; el campo lo fijó el almacenista y se
queda. **El gasto se cuenta una sola vez, en la salida**, con la máquina del
almacenista. La RQ regularizadora lleva su **propia** CC-Máquina (obligatoria)
que **nunca genera movimiento** — puede diferir de la del vale y **no hay doble
conteo, solo divergencia en papel**. Es correcto por construcción:
`RegularizarVale` solo toca `RqRegularizadoraId` + `PendienteRegularizacion`,
nunca las líneas.

## Consecuencias

**Positivas**: cada peso de la cadena de compras queda etiquetado con una
máquina; una sola fuente por dato (el origen lo fija, aguas abajo lo hereda);
el "ver ≠ elegir" queda estructuralmente limpio (el evaluador de alcance vive
solo en el selector); el criterio dueño-vs-proxy es objetivo y reutilizable
por la próxima dimensión analítica (proyecto).

**Negativas / aceptadas**: la captura por proxy es de menor confianza (sin eje
que la guíe entre ~361 máquinas); el vale regularizado puede mostrar dos
CC-Máquina distintas (sin doble conteo); la obligatoriedad **no** puede
encenderse hasta que los requisitantes tengan alcance sembrado — se entrega el
picker primero y se activa la obligatoriedad después, sin flag ni ventana de
bloqueo (bloqueo de arranque).

## Primera implementación

Fase E, 5 PRs (PR1 toolkit de consumo → PR2 RQ → PR3 OC → PR4 entrada → PR5
salida), diseño en
[`docs/modulos/centros-costo/08-consumo-compras.md`](../modulos/centros-costo/08-consumo-compras.md);
espejo del FE en `05-frontend-diseno.md` §4.3. **PR2 es la primera cableada real
del read-port**: el detalle de RQ enriquece cada línea con `IDim3ReadPort`
(arista `Compras → CentrosCosto`; el obligatorio se desprende a PR2.1).
**PR2.1 activa la obligatoriedad en la línea de RQ** — el campo pasa de opcional
a required en el schema (`idLike`) y en los 2 validators
(`RuleFor(CentroCostoId).NotNull()`, código `LINEA_RQ_CENTRO_COSTO_REQUERIDO`);
sin esperar sembrado masivo de alcance (config por demanda en
`/centros-costo/asignaciones`).
**PR3 (OC) es la primera materialización del patrón elegido-abierto-por-proxy**
(§2): la línea manual (`crear-sin-rq`) elige de todo el catálogo activo contra el
endpoint gateado `/api/v1/compras/ordenes/dim3/buscar` (`BuscarDim3AbiertoQuery`,
sin filtro de alcance) — **el backend abre el selector, no el FE**. En paralelo,
la línea heredada de RQ materializa el heredado-solo-lectura (§3): hereda 1:1 y se
**bloquea** (guarda `LINEA_OC_CC_HEREDADO_INMUTABLE`); el flip a obligatorio de la
manual se desprende a PR3.1.
**PR3.1 activa la obligatoriedad en la línea manual de OC** — el campo pasa de
opcional a required en el schema y el validator
(`LINEA_OC_MANUAL_CENTRO_COSTO_REQUERIDO`). La línea heredada ya está cubierta
por PR2.1 y por la guarda de dominio existente `LINEA_OC_CC_HEREDADO_INMUTABLE`.
Dos matices que el molde de PR2.1 no anticipaba, ambos derivados de que en OC
conviven los dos tipos de línea: (a) en el PATCH la regla es un invariante de
**post-estado** en `LineaOrdenCompra.ActualizarEstructural`, no una regla de
input — el comando es PATCH parcial (`null` = *no tocar*) y exigirlo como input
obligaría a reenviar el CC en cada edición parcial; (b) en el FE la exigencia es
**condicional** (`ccRequerido`), porque un solo schema resuelve el form en ambos
modos y en la heredada el campo se pinta read-only: requerirlo siempre dejaría
una línea heredada legada con CC null imposible de guardar. Con esto **no quedan
flips desprendidos** en Fase E.
**PR5 (salida) cierra la cadena**: cabla el segundo endpoint abierto por proxy
`/api/v1/almacen/salidas/dim3/buscar` (gateado por `almacen.salidas.por-vale`,
para el vale sin RQ) y la salida-con-RQ hereda el CC autoritativamente del
read-port de RQ (bloqueado). Su display **NO** agrega una arista de proyecto
`Almacén→CentrosCosto` como hizo Compras (PR2): esa arista cerraría un **ciclo**
`Almacén→CentrosCosto→Compartido→Almacén` (Compartido consume el placeholder
`Almacen.cs`). Se usa el patrón `IComprasOcReadPort` — puerto propio
`Almacen.Domain.Ports.ICentroCostoReadPort` + NoOp stub + adapter bridge en
`Compras.Infrastructure.PublicAdapters` (delega en `IDim3ReadPort`) wireado en
`Program.cs`. Es la **primera vez que Almacén lee CentrosCosto**, pero por puerto,
no por arista.
Mecánica del read-port: ADR-0042; baja lógica de las Dim3 mostradas: ADR-0049. El campo vestigial
`MovimientoInventario.MaquinaDestinoId` (cabecera de salida, sin FK, 0 de 93
poblado) **muere** en PR5 (`DROP COLUMN`) — máquina destino y CC-Máquina son lo
mismo, y este último es por línea.
