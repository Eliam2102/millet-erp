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
Pedido pasa a "en firme"
  └─ customización A+W ──INSERT──►  MILLET_INTEGRACION.dbo.aw_solicitud_pedido
                                       (operacion, version, …)
                                            ▲          │
                                            │          │  Hybrid Connection hc-aw-business-sql
                             write-back     │          ▼  (SER-DATA:1433, ya en producción)
                             (claim,        │   AwSolicitudesWorker (Facturación, cada N min)
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

El polling (IntervalSeconds, default 120s) es el transporte garantizado. Para
latencia de segundos, la customización A+W llama **tras su INSERT** al exe
`MilletAwPedidoNotify` (`on-prem/aw-pedido-notify/`), que hace
`POST /api/v1/integraciones/aw/pedidos/nudge` con la API key
`IntegracionesAw:Pedidos:NudgeApiKey` (header `X-Millet-Nudge-Key`,
comparación en tiempo constante; key vacía = endpoint apagado con 404). El
endpoint solo dispara `AwSolicitudesTickSignal` — el worker despierta y
barre. **Best-effort por diseño**: nudge perdido = la solicitud espera al
siguiente tick; la tabla-puente sigue siendo la única fuente de verdad.
Múltiples nudges colapsan en un tick (el barrido drena todo lo pendiente).

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
(solo dígitos). Régimen fiscal y CP **fiscal** probablemente no existen en
A+W (gap G12) → se completan en la UI del ERP antes de timbrar.

### 3.5 `vw_erp_articulo` (auto-provisión)

Del catálogo de productos A+W: `producto_ref`, `descripcion`,
`unidad_medida` normalizada. Claves SAT **no existen en A+W** — las asigna el
master del ERP (`compartido.producto_aw`) vía mapeo de unidad + UI.

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
