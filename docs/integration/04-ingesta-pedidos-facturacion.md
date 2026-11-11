# 04 — Ingesta de pedidos en firme A+W → Facturación

> **Versión:** 1.0.0
> **Última actualización:** 2026-07-05
> **Decisión ancla:** [ADR-0048](../decisiones/0048-bd-integracion-aw-pedidos.md)
> **Diseño del módulo consumidor:** `docs/modulos/facturacion/01-diseno.md` (§3 Decisión 01-E, §5, §6.1, §9, §12.1)

---

## ⚠️ Este flujo NO es el del Glass Agent

Este documento describe el **flujo 2** de `Millet.Integraciones.Aw`: pedidos
**en firme** que A+W libera para facturar, **entrantes** hacia el ERP vía
tabla-puente + vistas SQL en la BD `MILLET_INTEGRACION` (on-prem).

El **flujo 1** (cotizaciones del Glass Agent: EDI → drop service →
correlación `aw_doc_id` → webhooks, docs 00–03) es independiente, está en
producción y **no participa aquí**. Solo comparten la Hybrid Connection
`hc-aw-business-sql` y las convenciones del módulo. Si estás tocando
`EntidadExterna`, `AwDropWorker` o `AwLateReconciliationWorker`, estás en el
flujo equivocado.

---

## 1. Mapa del flujo

```
A+W (Gupta, on-prem)                        SER-DATA                        Azure (Millet.Api)
────────────────────                        ────────                        ──────────────────
Pedido pasa a "en firme" (15/42)
  └─ customización A+W ──INSERT──►  MILLET_INTEGRACION.dbo.aw_solicitud_pedido
                                       (operacion, version, …)
                                            ▲          │
                                            │          │  Hybrid Connection hc-aw-business-sql
                             write-back     │          ▼  (SER-DATA:1433, ya en producción)
                             (claim,        │   AwSolicitudesWorker (Facturación, cada 15 s)
                              resultado,    │     ├─ IAwSolicitudesReader (Integraciones.Aw)
                              uuid,         │     │    ├─ barre solicitudes pendientes en orden
                              estado)       │     │    └─ lee vistas vw_erp_pedido_* (cabecera/líneas/BOM)
                                            │     ├─ ProcesarSolicitudAwCommand (matriz op×estado,
                                            │     │    doble candado ingesta_control, D20)
                                            │     │    └─ IMasterProvisioningPort → auto-provisión
                                            │     │       Cliente / ProductoAw (DatosMaestros)
                                            └─────┴─ IAwWriteBackPort (UPDATE a la misma fila)

Al timbrar/cancelar el CFDI:
  EmitirFacturaVentaHandler / cancelación ──► ingesta_control.write_back_* (pendiente)
  WriteBackResultadoWorker ──► UPDATE uuid + estado_facturacion en aw_solicitud_pedido
```

Piezas del ERP que **ya existen** (F3, PRs #346–#348): `AwSolicitudesWorker`,
`ProcesarSolicitudAwCommand` (matriz §12.1), `ingesta_control`,
`pedido_facturable(_linea)(_snapshot)`, `bandeja_excepcion_importacion`.
Este flujo solo implementa los puertos reales y el master de datos.

### 1.bis Nudge de baja latencia (PR7)

El polling (IntervalSeconds, default 15s) es el transporte garantizado. Para
latencia de segundos, la customización A+W llama **tras su INSERT** al exe
`MilletAwPedidoNotify` (`on-prem/aw-pedido-notify/`), que hace
`POST /api/v1/integraciones/aw/pedidos/nudge` con la API key
`IntegracionesAw:Pedidos:NudgeApiKey` (header `X-Millet-Nudge-Key`,
comparación en tiempo constante; key vacía = endpoint apagado con 404). El
endpoint solo dispara `AwSolicitudesTickSignal` — el worker despierta y
barre. **Best-effort por diseño**: nudge perdido = la solicitud espera al
siguiente tick; la tabla-puente sigue siendo la única fuente de verdad.
Múltiples nudges colapsan en un tick (el barrido drena todo lo pendiente).

### 1.ter Job de respaldo del disparo (U1.4)

Mientras A+W no tenga la customización, el INSERT lo hace un job de SQL
Server Agent cada 15 s en `MILLET_INTEGRACION`
([`07_job_aviso_pedidos.sql`](../operacion/aw-integracion-scripts/07_job_aviso_pedidos.sql)):
lee en solo lectura `BW_AUFTR_KOPF.STATUS` (15/42 en firme, 900/999
cancelado) y `BW_AUFTR_HIST`, y escribe Alta/Modificación/Cancelación con
`version` creciente y sin duplicados. Job (15 s) + polling (15 s) ⇒ peor
caso ~30 s, dentro del «menos de 1 minuto» (Módulo 13 R14). Medición en
[`08_latencia_avisos.sql`](../operacion/aw-integracion-scripts/08_latencia_avisos.sql).
Detalle y desactivación al llegar el customizing: runbook de customizing §3.ter.

## 2. Tabla-puente `aw_solicitud_pedido`

Vive en `MILLET_INTEGRACION` (prod) / `MILLET_INTEGRACION_DEV` (dev/qa).
Esquema **definido por el ERP** (D18); A+W solo INSERTa; el ERP solo escribe
las columnas de write-back. DDL completo en
[`docs/operacion/aw-integracion-scripts/02_create_table.sql`](../operacion/aw-integracion-scripts/02_create_table.sql).

| Columna | Tipo | Escribe | Significado |
|---|---|---|---|
| `solicitud_id` | `uniqueidentifier` PK, default `NEWID()` | A+W (default) | Identidad de la solicitud |
| `numero_pedido` | `nvarchar(50)` | A+W | Llave natural del pedido (BW_AUFTR_KOPF.ID) |
| `operacion` | `tinyint` | A+W | 1=Alta · 2=Modificación · 3=Cancelación |
| `version` | `bigint` | A+W | **Estrictamente creciente por pedido** (orden + idempotencia) |
| `creada_at` | `datetimeoffset(3)`, default `SYSDATETIMEOFFSET()` | A+W (default) | Timestamp de la solicitud |
| `erp_pedido_id` | `uniqueidentifier` NULL | **ERP** | Claim; **persiste siempre, nunca se borra** |
| `estado_facturacion` | `nvarchar(20)` NULL | **ERP** | `SinFacturar` / `Facturado` / `Cancelado` |
| `uuid` | `nvarchar(36)` NULL | **ERP** | Folio fiscal del CFDI al timbrar |
| `resultado` | `tinyint` NULL | **ERP** | NULL=pendiente · 1=Aplicada · 2=Rechazada · 3=Pospuesta · 4=Error |
| `motivo` | `nvarchar(500)` NULL | **ERP** | Detalle del resultado |
| `procesada_at` | `datetimeoffset(3)` NULL | **ERP** | Cuándo la procesó el ERP |

- `UNIQUE (numero_pedido, version)`; índice de barrido
  `(resultado, numero_pedido, version) INCLUDE (operacion, creada_at)`.
- **Pendiente** = `resultado IS NULL OR resultado = 3` (Pospuesta se relee en
  cada tick hasta resolverse).
- El barrido lee `TOP(@max) … ORDER BY numero_pedido, version`.
- Doble candado: la tabla es la **entrada**; `facturacion.ingesta_control`
  (Postgres) es la **fuente de verdad** (hash + última versión aplicada) —
  sobrevive aunque A+W reescriba algo.

## 3. Vistas de lectura (contrato de columnas — congelado)

Las vistas viven en `MILLET_INTEGRACION` y hacen SELECT cross-database a
`MILMAIN.SYSADM.*` (la BD de A+W en la instancia `SERDATA\AWBUSINESS`). **El contrato de columnas es fijo** (los adapters
seleccionan por nombre); el SQL interno se itera con datos reales en la fase
guiada. Derivan del **diseño JSON de mayo 2026** (anexo A): mismas fuentes,
misma lógica de transformación.

### 3.1 `vw_erp_pedido_cabecera`

| Columna | Fuente (diseño JSON) | Regla |
|---|---|---|
| `numero_pedido` | BW_AUFTR_KOPF.ID | — |
| `numero_sucursal` | OR_AVBEREICH | CASE `PERIFERICO`→`CONKAL` |
| `cliente_ref` | AH_IDENT | — |
| `cliente_nombre` | KU_KUNDEN.NAME1+' '+NAME2 | LTRIM/RTRIM + ISNULL |
| `rfc_cliente` | KU_KUNDEN.UST_ID | Solo `[A-Za-z0-9]` |
| `uso_cfdi` | KA_LIEFERBED.FREMD_KEY (JOIN OR_LIEFERBED) | Exportación (`AH_KOPF='Interfaz EDI'` o `GRUPPE='Ventas Internacionales'`) → `S01`; vacío/`<indf>` → NULL |
| `metodo_pago` | Derivado de `condicion_pago` | `CONTADO`→`PUE`; resto→`PPD` |
| `forma_pago` | KA_ZAHLWEG.FREMD_KEY (JOIN FI_ZAHLWEG) | Vacío/`<indf>` → `99` |
| `condicion_pago` | FI_ZAHLBED (catálogo KA_ZAHLBED) | Vacío/`<indf>` → `CONTADO` |
| `divisa` | KA_WAEHRUNGEN.FREMD_KEY (JOIN FI_WAEHRUNG) | ISO 4217; sin match → `MXN` |
| `obra_id` / `obra_nombre` | KO_OBJEKT_KUNDE / KA_OBJEKT.BEZ | `0` → ambos NULL |
| `notas_pedido` | BW_AUFTR_KTXT.BEZ (TOP 1) | `RtfToText()`, `\r\n`→`\n` |
| `canal_ventas` | KA_FIRMA_MITARB.GRUPPE (JOIN OR_BEARBEITER) | — |
| `clase` | IIF AH_KOPF='Interfaz EDI' + CASE GRUPPE | — |
| `fecha_transaccion` | DATUM_ERF | `date` |
| `pedido_sustituido_numero` | AH_HAUPT_AUFTR | `0` → NULL |
| `estado_origen` | Estatus del pedido en A+W | Confirmar columna (gap G6) |
| `total_cantidad` / `total_m2` / `importe_total` | SUM de posiciones | **Totales de control** — el ERP valida cabecera-vs-líneas (`TotalesNoCuadran`) |
| `iva_porcentaje` | KA_MWST.MWST (JOIN `MWST = FI_MWST1`) | **IVA a nivel documento** (FAC-DET-PR2), en porcentaje (16.00 / 0.00); NULL = sin match en catálogo. `MWST` es el porcentaje mismo — confirmado en SER-DATA 2026-07-08 (G15 cerrado) |
| `ranura` | BW_AUFTR_KOPF.KO_FALZ (campo 32 del catálogo JSON, confirmado por el owner 2026-07-15) | **Descuento a nivel cabecera**, BRUTO (con IVA); `0` → NULL. NO se resta en el CFDI de la venta: se documenta con **NC automática post-timbrado** (relación 01, motivo `Ranura`) y la caja cobra `total − NC` ([Decisión 13-K]). Pendiente validar con pedido real que es bruto (RANURA-PR1) |

Todos los strings pasan por limpieza `<indf>` → NULL.

**Semántica de importes (FAC-DET-PR2, confirmada por el owner 2026-07-08):**
los importes que exponen las vistas (`importe_pieza`, `descuento`,
`importe_total`) son **BRUTOS (con IVA)**. En A+W el IVA es a nivel
documento (`BW_AUFTR_KOPF.FI_MWST1` → `KA_MWST`) y la tasa se transfiere a
las posiciones. El reader del ERP:

1. Normaliza `iva_porcentaje` a fracción (16.00 → 0.16; tolera 0.16).
2. Calcula el **neto hacia atrás** por línea: `neto = bruto / (1 + tasa)`,
   redondeo a 6 decimales (límite CFDI de ValorUnitario) — así A+W y el ERP
   no calculan importes distintos.
3. Concilia totales cabecera-vs-líneas comparando **brutos contra brutos**
   (antes de convertir) — cuando `importe_total` se active (gap G13) no hay
   que tocar nada.
4. Con `iva_porcentaje` NULL las líneas viajan como antes (precio tal cual,
   sin tasa) — el comportamiento previo queda intacto.
5. El snapshot (`PayloadCrudo`) conserva los brutos originales.

**Orden operativo:** correr el `ALTER VIEW` del script 03 en SER-DATA
**ANTES** del deploy del ERP con este cambio. Si el deploy llega primero, el
SELECT del reader falla con SqlException 207 (columna inexistente) → error
transitorio recuperable del worker, sin corrupción — por eso el reader no
lleva try/fallback.

### 3.2 `vw_erp_pedido_linea`

| Columna | Fuente | Regla |
|---|---|---|
| `numero_pedido` / `numero_posicion` | BW_AUFTR_POS ID / POS_NR | — |
| `producto_ref` | PROD_ID | — |
| `descripcion` | PROD_BEZ1 | **Solo producto** (decisión D8 del ADR); sin procesos |
| `detalle_procesos` | BW_AUFTR_STKL.STL_BEZ concatenados (filtro BOM_BASE_ID<>1, BOM_MASTER_ID<>8, BOM_LEVEL=1) | Informativo → snapshot |
| `cantidad` | PP_MENGE | — |
| `unidad_medida` | PR_EINHEIT | `UPPER(REPLACE(…,'²','2'))` → M2/PZA/ML/KG |
| `importe_pieza` | SYSADM.DEVUELVE_IMPORTE_Y_DESCTO_N(…,'P') | En la moneda del pedido (FI_WAEHRUNG) |
| `descuento_porcentaje` | SYSADM.DEVUELVE_IMPORTE_Y_DESCTO_N(…,'D') | — |
| `descuento` | importe_posicion × descuento_porcentaje / 100 | En la moneda del pedido |
| `almacen_nivel_1..4` / `almacen_id_ubicacion` | KA_LAGER_DEF LEVEL1..4 / PROD_LAGERORT | — |
| `requiere_pedimento` | Pendiente definir origen (gap G5) | Hoy NULL → false |

### 3.3 `vw_erp_pedido_componente` (BOM)

De BW_AUFTR_STKL, filtro `PREISRELEVANT=1 AND STL_PRODART IN (1,2,3,30,50,60)`,
ORDER BY POS_NR: `numero_pedido`, `numero_posicion`, `producto_ref`
(BOM_PRODUKT), `descripcion` (STL_BEZ), `alto_mm`/`ancho_mm`
(STL_HOEHE/STL_BREITE), `m2_por_pieza` (STL_QM), `importe`
(PR_BETR_NETTO o PR_BETR_NETTO_FW según moneda del pedido). Alimenta el
`bom_json` de cada `pedido_facturable_linea` (jsonb, informativo).

### 3.4 `vw_erp_cliente` (auto-provisión)

De KU_KUNDEN: `cliente_ref` (ID), `razon_social` (NAME1+NAME2), `rfc`
(UST_ID limpio), `calle`/`colonia`/`cp`/`ciudad`/`estado`/`pais`, `telefono`
(solo dígitos). La vista no trae régimen fiscal ni CP **fiscal** (gap G12) → se
completan en la UI del ERP antes de timbrar. Actualización (U1.8b): Millet documentó
que el régimen sí existe en A+W como `KA_BONITAET.FREMD_KEY` (unido por
`KU_KUNDEN.BONITAET`); hoy solo se lee para el reporte de diferencias fiscales
(doc 05 §14), no para la auto-provisión.

### 3.5 `vw_erp_articulo` (auto-provisión)

Del catálogo de productos A+W: `producto_ref`, `descripcion`,
`unidad_medida` normalizada. Claves SAT **no existen en A+W** — las asigna el
master del ERP (`compartido.producto_aw`) vía mapeo de unidad + UI.

### 3.6 `vw_erp_albaran_pos` (descuento de PT por albarán de A+W) · propuesta 10-nov

El albarán se queda en A+W; el ERP solo emite la hoja de salida (Geovany, 10-nov). En A+W el albarán es un dato de la
cabecera (`BW_AUFTR_KOPF.NR_LIEFERSCHEIN` y `DATUM_LIEFERSCHEIN`, Jorge Toache 10-nov). DDL en
[`20_create_vista_albaran.sql`](../operacion/aw-integracion-scripts/20_create_vista_albaran.sql), pendiente de visto bueno de TI.

- Una fila por albarán y posición: `numero_pedido`, `numero_posicion` (donde se produjeron las piezas: lote de PT
  pedido-posición), `pedido_albaran`, `posicion_albaran`, `piezas`, `albaran_folio`, `albaran_fecha`, `estatus_pedido`.
- La cabecera guarda un solo folio. Cada entrega parcial es un pedido hijo (`DOK_TYP = 'Expedición parcial'`) con su folio;
  sus posiciones apuntan al origen por `REF_AUSLIEF_AUFTR_ID/POS`. Las reclamaciones se quedan con su propio pedido.
- Lector: `AlbaranesAwOrigenAdapter` (Demo/Real), worker `AlbaranesAwWorker`, bandera `IntegracionesAw:PiezasPt:AlbaranAwHabilitado`
  (apagada), ventana `AlbaranAwVentanaDias` (7). PT: `AplicarAlbaranesAwCommand`, idempotente por pedido + posición + albarán
  (movimiento `aw-albaran:{pedido_albaran}:{folio}`: lleva el pedido de A+W que trae el albarán para no depender de que
  `NR_LIEFERSCHEIN` sea único en todo A+W); no descuenta otra vez lo que ya salió o está cargado por la hoja de salida del ERP.
- Robustez: una fila con pedido, posición, folio, fecha o `pedido_albaran` nulos se omite con aviso en el log (piezas nulas
  cuentan 0); una falla al leer la vista se registra y se reintenta en el siguiente intervalo. Piezas no enteras se truncan
  con aviso. Si una hoja de salida del ERP carga una pieza a la vez, el token `Version` de la pieza (ADR-0012) rechaza el
  guardado de esa posición y se reintenta en el siguiente ciclo con el estado nuevo.
- Ventana de 7 días: el worker solo ve pedidos con algún albarán con `albaran_fecha` dentro de la ventana (de ese pedido trae
  todos sus albaranes). Un albarán más viejo que la ventana que no se leyó a tiempo (worker apagado, origen caído, piezas
  que llegaron a PT después) ya no se descuenta. **Backfill:** subir temporalmente
  `IntegracionesAw:PiezasPt:AlbaranAwVentanaDias` a los días que cubran el hueco, reiniciar la app, esperar uno o dos ciclos
  (la idempotencia evita duplicar lo ya aplicado) y regresarla a 7.
- Límite: si TI borra o cambia un folio ya leído, el ERP no revierte la salida.
- Límite: la cancelación del albarán del ERP (PT5.8, #512) no revierte las salidas aplicadas por un albarán de A+W (movimiento
  `aw-albaran:…`). Si el albarán de A+W estaba mal, se corrige en A+W; el ERP no tiene reversa automática para ese movimiento.

## 4. Mapeos A+W → tipos del ERP

Rediseño 2026-07-07 (sustituye a los diccionarios de configuración por
ambiente; motivación: los valores crudos de A+W traen espacios — `Ventas
Cancun` — y no son representables como nombre de app setting, y la relación
debe ser administrable sin redeploy):

| Dato | Dónde vive la relación | Mecánica |
|---|---|---|
| Sucursal | **Catálogo del ERP** — `compartido.sucursales.clave_aw`, editable en `/admin/sucursales` | El reader resuelve `numero_sucursal` contra `clave_aw` (case-insensitive). Valores conocidos: CIRCUITO, CHICHI SUAREZ, CANCUN, CONKAL |
| Canal de venta | **Vista `vw_erp_pedido_cabecera`** (CASE versionado en git, `03_create_views.sql`) | La vista traduce `GRUPPE` → nombre del enum `CanalVenta` (`Ventas Cancun`→`TiendaCancun`, `Ventas Internacionales`→`Exportacion`); el reader hace `Enum.TryParse`. GRUPPE restantes = gap G6 |
| Comportamiento fiscal | **Vista** (mismo CASE) | Gap G14 CERRADO (fiscal, 2026-07-08): la señal es la **condición de pago**, no el canal. Exportación → `ExportacionConCce`; `FI_ZAHLBED` = CONTADO (o vacío) → `MostradorInmediato`; cualquier otra condición (crédito, REPARTO = contraentrega/crédito 24 h) → `ConAnticipo` |
| Unidad SAT | `MapeoUnidadSat` en `IntegracionesAw:Pedidos` (defaults en código) | M2→MTK, PZA→H87, KG→KGM, ML→confirmar |

Valor no resoluble (sin `clave_aw` que machee, o texto que no parsea al
enum) → el reader devuelve `LecturaPedidoAw` sin datos con el motivo
específico (`CanalVentaSinClaveAw`/`SucursalSinClaveAw`/
`ComportamientoSinRegla`) y la ingesta la POSPONE (FAC-ING-PR3): la
excepción queda visible en bandeja con el valor crudo en el detalle y el
pedido entra solo al corregir el catálogo (tope
`Facturacion:Workers:AwSolicitudes:PospuestaMaxDias`, default 7 días).
Los diccionarios `MapeoCanalVenta`/`MapeoComportamiento` se conservan como
**override opcional** que se consulta antes del `Enum.TryParse` (escape
hatch operativo sin tocar la vista); `MapeoSucursal` se elimina.

## 5. Semántica del write-back

1. **Por solicitud** (síncrono, al procesarla): `erp_pedido_id` (claim) +
   `resultado` + `motivo` + `procesada_at`, `UPDATE … WHERE solicitud_id = @id`.
   Si la HC está caída, el fallo NO tumba la ingesta: queda pendiente en
   `ingesta_control.write_back_*` y lo reintenta el worker.
2. **Por pedido** (asíncrono, al timbrar/cancelar): `uuid` +
   `estado_facturacion`, `UPDATE … WHERE numero_pedido = @np` (fila destino:
   última `version` vs todas — gap G-writeback, se decide con el equipo A+W).
   Lo ejecuta `WriteBackResultadoWorker` barriendo
   `ingesta_control WHERE write_back_pendiente`. Idempotente (UPDATE absoluto).
3. El claim **nunca se borra** — las operaciones son explícitas, no "ausencia
   de claim" (cero huérfanos, D18).

## 6. Auto-provisión de masters

- **Cliente** (`compartido.clientes`, nuevo): al ingestar, si `cliente_ref` no
  existe → `EnsureClienteDesdeAwAsync` lee `vw_erp_cliente` (+ RFC del propio
  pedido) y ejecuta `ProvisionarClienteDesdeAwCommand` (upsert por
  `ReferenciaExterna`, `Origen=Aw`). `uso_cfdi`/`forma_pago`/`metodo_pago` del
  pedido alimentan los defaults del cliente al crearlo.
- **ProductoAw** (`compartido.producto_aw`, nuevo — separado de
  `compartido.articulos`, ADR-0048 D5): `EnsureArticuloDesdeAwAsync` lee
  `vw_erp_articulo` y ejecuta `ProvisionarProductoAwCommand`; la unidad A+W se
  resuelve contra el catálogo `UnidadMedida` (ADR-0046) y `MapeoUnidadSat`
  propone la clave unidad SAT.
- Datos fiscales incompletos **no bloquean el alta** del master ni la ingesta
  del pedido; bloquean el **timbrado** (regla ya implementada en Facturación).
  Se completan en `/admin/datos-maestros/clientes` y
  `/admin/datos-maestros/productos-aw`.

## 7. Gaps abiertos con el equipo A+W

Lista completa y actualizable en la
[spec de customización](runbooks/aw-solicitud-pedido-customizing.md) §7:
G1 disparo/definición de "en firme" · G2 generación de `version` (blueprint:
detección de cambios del diseño JSON) · G3 encoding/colación · G5 origen de
`requiere_pedimento` · G6 valores reales de canal/clase/estatus · G7 volumen ·
G8 logins · G9 estado `Liquidado(70)` · G10 proceso ante `Rechazada`
post-factura (D20) · G-writeback fila destino del UUID · G11 Cancelación ·
G12 régimen/CP fiscal en KU_KUNDEN · G13 EXECUTE/perf de funciones SYSADM en
vistas · ~~G15~~ **CERRADO 2026-07-08**: `KA_MWST.MWST` es el porcentaje
mismo (decimal; `KZ_GESPERRT=1` = tasa histórica bloqueada; join
`MWST = BW_AUFTR_KOPF.FI_MWST1` → FI_MWST1 guarda la tasa, no el ID).
Pendiente FAC-DET-PR2: validar con un pedido real que `descuento` e
`importe_pieza` son brutos (con IVA) — el warning centinela del reader lo
delata si no.

**Gap interno G14 — CERRADO (equipo fiscal, 2026-07-08):** la regla
`ComportamientoFiscal` NO va por canal sino por **condición de pago**
(`FI_ZAHLBED`): CONTADO ≈ mostrador inmediato; las demás condiciones
implican anticipo/crédito (REPARTO = contraentrega, crédito de 24 horas)
→ `ConAnticipo`. Implementada en el CASE `clase` de
`vw_erp_pedido_cabecera` (`03_create_views.sql`); la `clase` ya no
depende del GRUPPE para pedidos nacionales.

---

## 8. Retorno ERP → A+W (`aw_retorno`) · decisiones de TI del 5-nov (86e3jvv6z)

El ERP solo INSERTa filas en `aw_retorno` (script 14) y lee el acuse; la aplica el servicio de TI. Filas que escribe:

| Tipo | Qué | Cuándo |
|---|---|---|
| 3 Documento | `FacturaFinal`, `NotaCredito`, `Cancelacion` | evento de Facturación (factura/NC timbrada, comprobante cancelado) |
| 4 Pago | un grupo por factura + forma + moneda, con el importe **acumulado** | cobro de mostrador registrado o cancelado, REPP timbrado o cancelado |
| 1 Estatus 115 | «facturado y entregado» (una sola vez) | `producto_terminado.pedido.facturado-entregado.v1` |
| 1 Estatus 70 | **solo** liberación manual de Crédito y Cobranza: `origen = 2` + `usuario_libera` | `pedido.estado-cambiado.v1` con `Liberado`, motivo `DecisionCxc` y decidida por una persona |
| 2 Saldo | cifra de control: una fila por pedido solo cuando el saldo cambia; nunca es la fuente | junto con cada 115 / 70 manual |

Reglas confirmadas:
- **Forma de pago y moneda** van con el valor de A+W: `forma_pago` = `KA_ZAHLWEG.BEZ` (EFECTIVO, TARJETA CREDITO…, hasta 40) y `moneda` = `KA_WAEHRUNGEN.WAEHRUNG` (pesos = `PESOSMX`, no MXN; hasta 8). El ERP mantiene la equivalencia en `IntegracionesAw:Pedidos:MapeoFormaPagoAw` / `MapeoMonedaAw` (appsettings). A+W cruza por nombre: si lo renombran deja de cruzar sin error; el ERP **avisa en el log** (y no manda el pago) cuando una clave no está en la equivalencia. La equivalencia no se lee en vivo de `KA_ZAHLWEG`/`KA_WAEHRUNGEN`: es configuración que se revisa contra esos catálogos.
- **Cobro cancelado**: fila nueva del mismo grupo con la suma de los cobros que quedan; en 0 solo si no queda ninguno. La secuencia es única y creciente por pedido, así el cero siempre es posterior al pago que anula.
- **70**: el ERP no lo manda por pago, saldo ni reparto (lo decide el servicio). Una decisión automática de CxC (usuario de sistema) tampoco viaja. Un 70 a un pedido ya liberado vuelve del servicio como «nada que hacer»: no cuenta como fila detenida ni como falla (se reconoce por el motivo; si TI lo marca distinto, se ajusta en `AwRetornoAcuse.NadaQueHacer`). `usuario_libera` es el id del usuario del ERP; A+W no lo valida, queda en su bitácora.
- **Notas de crédito de A+W (lectura directa y consumidor)**: TI otorgó el 5-nov SELECT a `millet_erp_lector_aw` (conexión `ConnectionStrings:AwClientesDb`) sobre `BW_GUTSCH_KOPF`, `BW_GUTSCH_HIST`, `BW_GUTSCH_POS` y `KA_FIRMA_MITARB`. Solo SELECT; el ERP **no escribe nada en A+W** (no hay acuse hacia A+W: `AwNotasCreditoDirectoPort.EscribirAcuseAsync` no abre conexión).
  - **Bandera (apagada por defecto)**: `IntegracionesAw:NotasCreditoLectura:Habilitada` (variable `IntegracionesAw__NotasCreditoLectura__Habilitada=true`). Con ella, el puerto `IAwNotasCreditoPort` «Real» pasa de la tabla puente `aw_solicitud_nota_credito` a la lectura directa; el origen Demo conserva su adaptador. Otras opciones: `VentanaDias` (7: solo notas cuyo estatus 95 es de los últimos N días; los ~27,500 históricos son de antes del ERP), `MaxPorLectura` (200), `ColumnaPedidoPadre` (`AH_HAUPT_AUFTR`), `GrupoClientesInternos` (`MOVIMIENTO INTERNO`). El barrido es el de `AwSolicitudesWorker` (`Facturacion:Workers:AwSolicitudes`, 15 s, requiere `EmpresaId`).
  - **Filtros de TI (verificados con SELECT real el 5-nov)**: estatus 95 (27,873 de 28,157 notas); solo con pedido original (`AH_HAUPT_AUFTR` = `BW_AUFTR_KOPF.ID`, ≠ 0; el JOIN deja fuera 327 cuyo pedido ya no existe → 27,546); del historial el último registro de esa transición (`DATUM`, luego `ID`; 4 notas pasaron dos veces por 95: 1 fila por nota); sin clientes internos de almacén (`KU_KUNDEN.GRUPPE = 'MOVIMIENTO INTERNO'`: hoy ninguna nota en 95). Vendedor: `BW_GUTSCH_KOPF.OR_BEARBEITER` = `KA_FIRMA_MITARB.ID` (LEFT JOIN, nunca se lee `PASSWORD`); las 27,546 resuelven.
  - **Mapeo a `SolicitudNotaCreditoAw`** (contrato del puerto, con dos campos opcionales nuevos `Vendedor`, `EsRanura`): `NumeroNota` = `BW_GUTSCH_KOPF.ID`; `NumeroPedido` = `AH_HAUPT_AUFTR`; `ClienteRef` = `AH_IDENT`; `Importe` = `FI_BETR_BRUTTO`; `Version` = 1 (A+W no versiona la nota); sin folio de factura (el handler toma la factura timbrada del pedido). **Motivo = `Bonificacion` siempre**: A+W no distingue bonificación de devolución y una devolución movería inventario; Contabilidad debe confirmar cómo separarlas.
  - **Ranura (no duplicar)**: en A+W, 18,855 de las 27,546 notas (y 286 de las 296 de la última semana) son exactamente la ranura del pedido (`BW_AUFTR_KOPF.KO_FALZ`, tolerancia 0.02). Esa NC ya la emite el ERP al timbrar la factura (`NcRanuraEmisor`, motivo `Ranura`). El lector marca `EsRanura`; el handler, si existe la NC de ranura vigente de esa factura con el mismo importe, responde `Aplicada` («ya cubierta por la NC de ranura …») y **no crea otra**. Sin NC de ranura en el ERP, se trata como una nota más.
  - **Consumidor** (`ProcesarNotaCreditoAwHandler`, sin cambio de reglas): idempotente por número de nota (índice único `ux_nota_credito_aw_numero_nota`); crea la NC «Por autorizar» ligada a la factura timbrada del pedido, sin timbrar; una persona la autoriza (`Facturacion.NotasCredito.Autorizar`) y de ahí sigue el evento `NotaCreditoTimbrada` hacia CxC y Contabilidad. Factura inexistente, cliente distinto o importe mayor al saldo → excepción en la bandeja de importación (se relee mientras la nota esté dentro de la ventana). El vendedor se guarda en `nota_credito.aw_vendedor` (migración `NotaCreditoAwVendedor`) y se ve en «Notas de crédito por autorizar».
  - **Límites**: sin cursor persistente (una nota aplicada se recuerda en memoria, `AwNotasCreditoVistas`; tras reiniciar se relee la ventana y la idempotencia evita duplicar). Una nota que A+W cambie de 95 a otro estatus después de leída no se retira del ERP.
  - **Probado**: SELECT real contra A+W (conteos, columnas, filtros, vendedor); simulador y pruebas unitarias (caso feliz, duplicado, falla, lento, sin escritura). Pendiente contra el sistema real: encender la bandera en un ambiente con facturas del ERP ligadas a pedidos de A+W y ver el flujo completo.
- **Clientes y obras**: A+W no crea clientes ni obras de forma automática (proceso manual, sin SQL); el ERP **no** tiene ni debe tener salida de altas hacia A+W y no necesita rango de numeración. La clave de A+W del cliente se guarda en `ReferenciaExterna` (`Origen = Aw`, ver `LigarAw` en doc 05) y el id de la obra (`KA_OBJEKT.ID`) viaja como `ObraId` en pedido, factura y anticipo.

### 8.1 A+W caído: avisos pendientes y reintento (M-160)

Si `aw_retorno` no responde **por conexión** (SQL Server inalcanzable, login o base no disponible, tiempo agotado, socket; en el origen Demo, cualquier falla transitoria de Npgsql), el aviso no se pierde ni cae a dead-letter:

- El listener (`FacturacionRetornoListenerWorker`, `integraciones-aw-subscription`) y el consumidor de PT (`ProductoTerminadoRetornoLocalConsumer`, `integraciones-aw-pt-sub`) guardan el evento tal cual (tipo + JSON) en `integraciones_aw.retorno_pendiente` con estado `Pendiente`, intento 1 y su error, y **completan** el mensaje. Si guardar el pendiente falla (base del ERP caída), el mensaje se abandona como antes.
- La clasificación vive en `AwRetornoAplicador.EsDeConexion`. Payload corrupto (`JsonException`) sigue yendo a dead-letter con motivo `InvalidPayload`; una regla (`AW_RETORNO_MAPEO_PAGO`) o un error SQL del aviso (constraint, tabla inexistente) sigue el camino de siempre (abandono → dead-letter tras 5 entregas).
- `AwRetornoAcuseWorker` corre cada minuto: reintenta los pendientes vencidos en orden de llegada con espera **1, 5, 15 y 60 min, y luego cada hora, sin tope**. Si A+W sigue caído, el resto de la pasada cuenta el intento con el mismo error sin esperar el timeout de cada uno. Al aplicarse queda `Aplicado` (con `aplicado_en_utc`); si la falla ya no es de conexión queda `Detenido` para revisión. La conciliación del acuse sigue cada 5 minutos.
- **Una sola vez**: el reintento corre el mismo código que el evento y `aw_retorno` es idempotente en todos los tipos (documento por UUID + tipo, estatus por estatus, pago por último importe del grupo, saldo solo si cambió).
- **Alerta**: si el pendiente más antiguo pasa de 4 h, el worker deja un warning en el log y el tablero lo marca.
- **Tablero**: «Sincronización con A+W» muestra la sección «Avisos a A+W» (pendientes, detenidos, el más antiguo, intentos, siguiente intento y último error) a quien tiene `integraciones.aw.administracion.configuracion`. API: `GET /api/v1/integraciones/aw/retorno/pendientes/resumen`.
- **Dead-letter**: `POST /api/v1/integraciones/aw/retorno/dead-letter/reprocesar` (mismo permiso, `Idempotency-Key`) lee la dead-letter de `integraciones-aw-subscription` (hasta 500 por llamada), aplica cada mensaje con el mismo servicio (o lo deja pendiente si A+W sigue caído) y lo completa. El payload inválido y las demás fallas se quedan en la dead-letter. Sin Service Bus responde 422 `SERVICE_BUS_NO_CONFIGURADO`.
- Límite conocido: mientras hay pendientes, un aviso nuevo con A+W ya de vuelta se escribe de inmediato y puede quedar antes que uno viejo del mismo pedido; el pago que llega antes que su factura no se escribe (lo recalcula la factura al aplicarse).

## Anexo A — Procedencia: diseño JSON de mayo 2026 (no construido)

El contrato de las vistas deriva de un diseño previo de integración por JSON
elaborado con el equipo A+W cuando no existía la Hybrid Connection. **Nunca se
construyó**, pero su trabajo de mapeo campo-por-campo es la especificación de
las vistas de este documento:

- Catálogo campo-por-campo (Encabezado 32 campos, Posiciones 30, Componentes
  7) con tabla origen, columna SQL, tipo, tamaño, nulabilidad y reglas —
  origen: `Catalogo_JSON_Integracion_ERP_TAMAÑOS.xlsx` (equipo A+W Millet).
- Respuesta del equipo A+W a la solicitud de cambios v0.1 (14/15 cambios
  aceptados): copia íntegra en
  [`anexos/2026-05-respuesta-cambios-json-aw.md`](anexos/2026-05-respuesta-cambios-json-aw.md).
  De ahí salen las reglas de `uso_cfdi`, `metodo_pago`, `forma_pago`, divisa
  ISO, RFC limpio, `<indf>`→NULL, obra separada, importes en una sola moneda,
  unidad normalizada y el blueprint de versionado (`version_pedido` +
  historial con detección de cambios, exit 200/204).

Diferencias deliberadas respecto al diseño JSON: el transporte es tabla-puente
+ vistas (no HTTP push); `total_pie2` no se lee (derivable); `departamento`
(constante) no se lee; los campos de entrega/refs/asesor viajan solo en el
snapshot; la descripción de línea NO concatena procesos (decisión D8).
