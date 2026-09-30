# ADR-0048: BD de integración on-prem para la ingesta de pedidos A+W → Facturación

- **Estado**: Aceptada
- **Fecha**: 2026-07-05
- **Decisores**: Eduardo Paredes (owner)
- **Etiquetas**: facturacion, integraciones-aw, datos-maestros, on-prem, hybrid-connection

## Contexto y problema

El backend de Facturación (F0–F11, PRs #340–#360) dejó la ingesta de pedidos
A+W construida **por dentro** pero conectada a stubs
(`StubAwSolicitudesReader` / `StubAwWriteBackPort`, PLATFORM-TODO
`<AwVistaPedidos>` / `<WriteBackOrigenes>`): la matriz operación×estado, el
doble candado `ingesta_control`, el `AwSolicitudesWorker` y la bandeja de
excepciones existen y tienen tests, pero ninguna solicitud real llega al ERP.

El diseño de Facturación (`docs/modulos/facturacion/01-diseno.md`, Decisión
01-E / D18) fijó el mecanismo: A+W escribe operaciones
(Alta/Modificación/Cancelación) en una **tabla-puente cuyo esquema define el
ERP**, y el ERP la consume en orden por `version` y escribe de vuelta claim,
estado de facturación y UUID. Quedaba abierto **dónde vive físicamente** esa
tabla, de dónde salen los **datos del pedido** (cabecera/líneas), y cómo se
resuelven cliente y producto en el master del ERP.

Dos hechos nuevos permiten cerrar esto:

1. La **Hybrid Connection a SER-DATA ya opera en producción** para el flujo de
   cotizaciones del Glass Agent (`hc-aw-business-sql` → SER-DATA:1433,
   `HybridConnectionAwSqlReader` sobre `Microsoft.Data.SqlClient`).
2. Existe un **diseño previo de integración por JSON** (mayo 2026, hecho
   cuando no existía la HC; **nunca se construyó**) que documenta campo por
   campo las tablas reales de A+W (`SYSADM.BW_AUFTR_KOPF`, `BW_AUFTR_POS`,
   `BW_AUFTR_STKL`, `KU_KUNDEN`, `KA_*`) con la lógica de transformación
   fiscal ya resuelta (uso CFDI, forma/método de pago, divisa ISO, RFC limpio,
   sentinel `<indf>`, versionado con detección de cambios). Ver
   [`docs/integration/04-ingesta-pedidos-facturacion.md`](../integration/04-ingesta-pedidos-facturacion.md).

## Frontera de scope: este NO es el flujo del Glass Agent

En `Millet.Integraciones.Aw` conviven ahora **dos flujos distintos que no se
mezclan**:

| | Flujo 1 — Cotizaciones (Glass Agent) | Flujo 2 — Pedidos en firme (este ADR) |
|---|---|---|
| Dirección | Saliente: ERP → A+W (EDI) | Entrante: A+W → ERP |
| Mecanismo | Drop service + `last_batch.log` + correlación `aw_doc_id` | Tabla-puente `aw_solicitud_pedido` + vistas SQL |
| Agregado | `EntidadExterna` | `PedidoFacturable` (Facturación) |
| Workers | `AwDropWorker`, `AwLateReconciliationWorker` | `AwSolicitudesWorker`, `WriteBackResultadoWorker` (Facturación) |
| Estado | Producción — **NO SE TOCA** | Este plan |

Lo único compartido es infraestructura neutral: la Hybrid Connection, el
patrón `SqlClient`/factory/clasificación de errores y las convenciones de
workers/config/KV. El código del flujo 2 vive en sub-área propia
(`Integraciones.Aw/Application/Pedidos/` e `Infrastructure/Pedidos/`).

## Decisiones

### D1 — La tabla-puente vive en una BD de integración nueva on-prem

Se crea la base **`MILLET_INTEGRACION`** (y **`MILLET_INTEGRACION_DEV`** para
dev/qa) en la misma instancia SQL Server de AWBUSINESS (SER-DATA). Contiene la
tabla `dbo.aw_solicitud_pedido` y las vistas de lectura.

- La customización A+W escribe **local** (simple, confiable, sin dependencia
  de internet para encolar).
- El ERP lee/escribe vía la HC existente (misma instancia:1433 → **cero infra
  Azure nueva**, solo un secreto KV con la connection string).
- Separar BD (y no un schema dentro de AWBUSINESS) aísla permisos, respaldos y
  objetos nuestros de la BD del proveedor.

Alternativas descartadas: schema dentro de AWBUSINESS (mezcla objetos con la
BD del proveedor); tabla en el Postgres del ERP (obligaría a A+W a escribir
hacia Azure vía ODBC/firewall — acopla A+W a la nube y pierde el encolado
offline).

### D2 — Los datos del pedido salen de vistas SQL derivadas del diseño JSON previo

No se rediseña el mapeo: las vistas `vw_erp_pedido_cabecera`,
`vw_erp_pedido_linea`, `vw_erp_pedido_componente`, `vw_erp_cliente` y
`vw_erp_articulo` implementan las fuentes y transformaciones que el diseño
JSON de mayo 2026 ya resolvió (contrato de columnas congelado en el doc 04;
SQL interno iterable con datos reales).

### D3 — Write-back completo por la misma tabla-puente

El ERP escribe de vuelta en `aw_solicitud_pedido`: claim `erp_pedido_id` +
`resultado`/`motivo` al procesar cada solicitud, y `uuid` +
`estado_facturacion` al timbrar/cancelar (worker de reintento
`WriteBackResultadoWorker` sobre columnas nuevas de `ingesta_control`). Esto
resuelve el flujo "notificación inversa ERP→A+W al facturar" que el diseño
previo dejó pendiente: A+W **lee columnas**, no expone endpoint.

### D4 — Dependencia de ensamblado: `Integraciones.Aw → Millet.Facturacion`

Los adapters reales implementan los puertos que Facturación define
(`IAwSolicitudesReader`, `IAwWriteBackPort`, `IMasterProvisioningPort`), así
que `Millet.Integraciones.Aw.csproj` referencia `Millet.Facturacion.csproj`
(sin ciclo — verificado; Facturación no referencia Aw). El override de los
stubs se hace en `Program.cs` DESPUÉS de `AddFacturacionModule`, con **toggle
por presencia de `ConnectionStrings:AwIntegracionDb`** (sin connection string
→ quedan los stubs; tercer candado de ambientes).

### D5 — Master de venta separado: `ProductoAw` (no se toca `Articulo`)

Los productos de venta manufacturados (vidrio transformado de A+W) van a una
entidad **nueva** `ProductoAw` (`compartido.producto_aw`), separada del
`Articulo` de compras/almacén:

- Poblaciones disjuntas y ciclos de vida distintos (el producto terminado no
  se compra ni tiene inventario en el ERP; el vidrio crudo sigue en A+W).
- Meter miles de productos A+W en `compartido.articulos` exigiría filtros de
  "naturaleza" en cada selector/query/reporte de la triada
  Compras↔Almacén↔CxP — riesgo de regresión inaceptable por una necesidad de
  Facturación.
- `IProductosReadPort` ya aísla a Facturación de la tabla física.
- Se **reutilizan** los catálogos de la triada (ADR-0046): FK a
  `UnidadMedida`, `CategoriaArticulo` opcional, selectores y convenciones de
  reconciliación.
- "ERP = master único de Producto" (levantamiento Facturación §16) se refiere
  al sistema de registro (ERP vs A+W), no a una tabla única compra+venta.

Si a futuro el master de venta se amplía más allá de A+W (Planta Pintura,
captura manual con catálogo propio), se evalúa generalizar el nombre.

### D6 — Master `Cliente` nuevo en DatosMaestros

No existe master de clientes en el ERP (solo Proveedor/Articulo). Se crea
`Cliente` (`compartido.clientes`) con los datos fiscales mínimos para
facturación; los clientes **nacen desde A+W** por auto-provisión
(`IMasterProvisioningPort`) usando `vw_erp_cliente` + los datos del propio
pedido. Datos fiscales incompletos no bloquean el alta (bloquean el timbrado —
regla ya implementada en Facturación). El bridge de provisión
(`AwMasterProvisioningAdapter`) vive en Integraciones.Aw por grafo de
dependencias (Compartido no puede referenciar Facturación); el alta real la
ejecutan commands de Compartido (`ProvisionarClienteDesdeAwCommand` /
`ProvisionarProductoAwCommand`) — se respeta "cero escritura directa al
esquema de otro módulo".

### D7 — Estrategia de ambientes (A+W es único = producción)

Tres candados contra consumo accidental de solicitudes reales:

1. Dev/QA apuntan a `MILLET_INTEGRACION_DEV` (la customización solo escribe en
   la de prod).
2. `Facturacion:Workers:AwSolicitudes:EmpresaId` vacío por default → el worker
   no procesa.
3. Sin `ConnectionStrings:AwIntegracionDb` → los stubs siguen registrados.

### D8 — BOM estructurado y descripción de línea

- El BOM (componentes `BW_AUFTR_STKL`) se persiste **estructurado por línea**:
  columna jsonb `bom_json` en `facturacion.pedido_facturable_linea` +
  extensión aditiva del contrato F3 (`LineaPedidoAw.BomJson`). Única
  modificación permitida al contrato F3.
- La descripción de la línea CFDI es **solo el producto** (`PROD_BEZ1`);
  `detalle_procesos` queda como columna informativa de la vista y se conserva
  en el snapshot.

## Consecuencias

**Positivas**: cierra `<AwVistaPedidos>`, `<WriteBackOrigenes>` (mitad A+W),
`<MasterProvisioningAw>` y `<DatosMaestrosFiscal>`; reutiliza el trabajo de
diseño JSON de mayo 2026 y la HC en producción; la triada no se toca.

**Negativas / deuda**: DDL on-prem fuera de EF (se gobierna con scripts +
runbook, patrón carga-SAP); la pata Planta Pintura de `<WriteBackOrigenes>`
sigue pendiente (`Integraciones.Origenes` no existe); los mapeos
canal/sucursal/comportamiento viven en configuración (si crecen, se promueven
a tabla administrable); gaps G1–G13 requieren confirmación del equipo A+W
(lista en el doc 04 y en la spec de customización).

## Adenda — ADM-06 — Sincronización de clientes (extensión, 2026-09)

**Estado**: implementado en la rama de ADM-06; conexión real a A+W pendiente.

**Decisión.** `Integraciones.Aw` incorpora un lector de clientes de **solo
lectura** sobre la base `MILMAIN` de A+W, con conexión y flags **independientes**
del flujo de pedidos. Los nombres son los **implementados** (ver doc 05 §10):

- `ConnectionStrings:AwClientesDb` (conexión propia; no se reutiliza
  `ConnectionStrings:AwIntegracionDb`, que además registra adaptadores de
  escritura).
- `IntegracionesAw:Clientes` (flags separados para leer, aplicar y programar
  el maestro). **Deshabilitado por defecto**.

**Adaptador futuro.** Si la arquitectura general despliega `vw_erp_cliente` en
`MILMAIN_INTEGRACION`, se agrega un adaptador de vista que cumple el **mismo
contrato** de lectura; no cambia el dominio ni la aplicación.

**Sin cambios en pedidos.** La tabla-puente `aw_solicitud_pedido`, sus vistas y
los write-backs (D1–D3) permanecen intactos. Habilitar o configurar clientes no
activa pedidos; los workers `Facturacion:Workers:AwSolicitudes` y
`AwWriteBack` deben poder permanecer deshabilitados al probar clientes.

**Seguridad.** Solo SELECT sobre las columnas/objetos necesarios; cuenta de
servicio por canal técnico; TLS verificado (no se copia `Encrypt=False` de
ejemplos históricos); sin secretos en repositorio ni en `appsettings`
(Key Vault). Camino privado desde el entorno de ejecución (p. ej. Hybrid
Connection) por confirmar; no se expone SQL a internet.

**Consecuencias.** (+) ADM-06 avanza con fixtures sin bloquearse por la BD
puente; el mismo contrato admite luego la vista. (-) Segundo punto de conexión a
A+W que operar y monitorear; el endpoint/puerto efectivo de la instancia
nombrada y el permiso de lectura siguen sin validar desde el entorno del ERP.
Detalle del contrato y de la detección de cambios en
[`docs/integration/05-sincronizacion-clientes-aw.md`](../integration/05-sincronizacion-clientes-aw.md).

## Referencias

- [`docs/integration/04-ingesta-pedidos-facturacion.md`](../integration/04-ingesta-pedidos-facturacion.md) — contratos y flujo
- [`docs/integration/runbooks/aw-solicitud-pedido-customizing.md`](../integration/runbooks/aw-solicitud-pedido-customizing.md) — spec para el equipo A+W
- [`docs/operacion/runbook-integracion-aw-pedidos.md`](../operacion/runbook-integracion-aw-pedidos.md) — DDL y operación
- `docs/modulos/facturacion/01-diseno.md` §3 (Decisión 01-E), §5, §6.1, §9, §12.1
- ADR-0046 (catálogos UM/Categoría), ADR-0009 (Outbox), ADR-0031 (PLATFORM-TODO)
- [`docs/integration/05-sincronizacion-clientes-aw.md`](../integration/05-sincronizacion-clientes-aw.md) — sincronización de clientes (adenda ADM-06)
