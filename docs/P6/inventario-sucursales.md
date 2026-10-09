# Inventario de separación por sucursal P6

Corte original: `4bf1690`. Revisión P6: fusión `687fe82` de `origin/main` (P1/P2/P3/P5/P8/P9/G1.13/A4.5) más correcciones de la [adenda 3](adenda3-integracion-09oct.md). El seguimiento P6b sobre `main` local `ea7bce2` se documenta al final. Este documento describe controles de código; el verde PostgreSQL posterior a estas correcciones y la aceptación Millet están **Por confirmar**.

## Regla y permisos

Los permisos del endpoint se validan antes del alcance territorial. En autorización de RQ/OC y solicitud de cancelación OC el permiso depende del paso: se comprueba primero, luego se consulta la sucursal del documento y finalmente se envía el comando. Sin permiso devuelve su 403 específico incluso si el ID no existe; con permiso mantiene 404 para inexistente y 403 `SUCURSAL_NO_ASOCIADA` para ajeno. La resolución de cancelación usa su policy de nivel 2 antes de ejecutar la guarda.

Los permisos de operación conservan su autorización habitual. Leer documentos de todas las sucursales requiere `*.leer-todas-sucursales`; escribir requiere `*.gestionar-todas-sucursales`. Tener lectura corporativa no concede escritura corporativa sobre los documentos. Excepción conservada por la adenda: los adjuntos propios de OC usan el bypass territorial previo `compras.ordenes.leer-todas-sucursales` también para subir/remover; exigen además `compras.ordenes.adjuntar` o `compras.ordenes.crear`, respectivamente. No se amplía ese bypass a cabeceras, líneas ni firmas. Los permisos nuevos se agregan al manifiesto/migración P6; no se asignan automáticamente a roles operativos personalizados. El bootstrap mantiene el super-admin con todos los permisos.

`SucursalScopeQueryBehavior` determina las sucursales autorizadas antes del handler. Las consultas filtran antes de conteos, paginación y totales. `DocumentoSucursalScope` resuelve la sucursal desde el documento persistido o sus orígenes. Un documento de varias sucursales exige acceso a todas; nunca se autoriza por una coincidencia parcial.

## Cobertura por familia

| Familia | Origen de sucursal | Listados e informes | Detalles y escrituras |
|---|---|---|---|
| Requisición | RQ.SucursalId | Bandeja y pendientes | Cabecera, líneas, transmitir, firmas, rechazo, eliminación, cancelación, cierre y adjuntos |
| Orden de compra | OC.SucursalDestinoId | Bandeja, pendientes, partidas abiertas, KPI, hermanas, selector RQ, historial de material | Alta, duplicación, origen, detalle, PDF, cabecera, líneas, datos del proveedor/logística/importación, transmisión, autorización, cierre, cancelaciones, rechazo, adjuntos existentes y pedimento |
| Factura de proveedor | Factura.SucursalId | Bandeja, revisión y reportes antigüedad/cartera/pasivos-obras/auxiliar-proveedores | Captura valida destino y OC; detalle, edición, revisión, autorización, cancelación, aplicación NC/anticipo, evidencias y adjuntos |
| Nota de cargo | Sucursal propia o factura origen | Bandeja | Alta, autorización, aplicación y detalle; alta valida también factura origen |
| Nota de crédito proveedor | Factura origen | Bandeja | Captura, vínculo y detalle; vínculo valida ambos documentos |
| Anticipo proveedor | OC origen | Bandeja e informe de antigüedad | Captura valida OC; aplicación valida factura y anticipo |
| Comprobaciones | Comprobación.SucursalId | Bandeja | Caja chica, aduanales (también cada factura), revisiones, autorizaciones, aplicación, rechazo y detalle |
| Reposiciones | Reposición.SucursalId | Bandeja y saldo acumulado por sucursal | Emisión y configuración validan la sucursal del comando |
| CFDI recibidos | Factura/NC/anticipo que lo utiliza | Bandeja | Detalle, XML, PDF, parseado, descarte y duplicado |
| Movimiento TC | Factura origen; refund hereda movimiento original | Bandeja e informe pendientes | Registro con CFDI, refund, disputas y resolución; sin origen verificable exige corporativo |
| Estado de cuenta TC | Factura agregada y todos los movimientos vinculados | Bandeja e informe consolidado | Archivo, conciliación, match, captura retroactiva, cierre y pago; match valida también el movimiento destino |
| Cartera CxC | Factura de venta origen | Facturas abiertas, saldo neto, antigüedad y estado de cuenta | La consulta global de crédito exige todas las sucursales de la cartera del cliente |
| Anticipos cliente | Comprobante de anticipo | Anticipos y estado de cuenta | Se conserva la lógica de saldo y compensación; se agrega metadata de sucursal al puerto existente |
| Propuesta CxC | Todas sus facturas de cartera | Bandeja | Alta valida cada factura y detalle. P5 mueve confirmación/rechazo a depósitos de Tesorería |
| Cobranza y alertas | Sucursales de la cartera del cliente | Bandejas | Registrar seguimiento y atender alerta requieren el alcance correspondiente |
| Pasivo pendiente | Factura proveedor o reposición origen | Bandeja y REPP pendientes | Solicitud de cancelación valida la factura |
| Pago proveedor | Factura de cada aplicación | Se refleja en movimientos/reportes | Registro valida todas las aplicaciones; reversa valida su documento |
| Depósito | Propuesta CxC o sesión de caja | Bandeja | Confirmación/rechazo; confirmación valida también el movimiento bancario |
| Movimiento bancario | Aplicaciones a facturas y depósitos ligados; reversa hereda origen | Bandeja | Detalle, ingreso y reclasificación; sin origen verificable exige corporativo |
| Reportes bancarios P5 | Todas las sucursales de todos los movimientos de la cuenta | Auxiliar y flujo filtran cuentas completas antes de saldos/totales | Cuenta mixta requiere acceso a todas sus sucursales; cuenta vacía o con movimiento sin origen requiere corporativo. Se conserva cálculo de saldo inicial/final de P5 |
| Pago a cuenta | Movimiento bancario origen | Bandeja (los globales sin origen solo corporativo) | Alta corporativa cuando no hay origen; ligar valida movimiento y factura |
| REPP recibido | Factura proveedor | Pendientes filtrados por pasivos | Registro valida factura |
| Trazabilidad | Cada nodo RQ/OC/recepción/factura/pago | Árbol comprobado antes de responder | Se comprueba raíz y todos los nodos relacionados |

## Documentos sin origen verificable y catálogos

Los documentos financieros sin sucursal verificable se ocultan del listado operativo y sus accesos por ID requieren alcance corporativo. Esto incluye anticipos sin OC, NC sin factura, pasivos internos de viáticos sin relación territorial, movimientos globales, estados TC sin origen y CFDI aún sin documento destino. Se devuelve un error claro; no se adivina sucursal a partir de quien inició sesión.

La carga inicial de CFDI conserva su permiso de ingesta. Un CFDI sin asignación es visible corporativamente; al vincularse hereda la sucursal del destino. Para la demostración deben usarse roles con los permisos explícitos correspondientes.

Las cuentas bancarias maestras, tarjetas maestras, catálogos CxP, tolerancias de propuestas, configuración y líneas/límites de crédito no son documentos con sucursal propia; conservan alcance de empresa y sus permisos. Solicitudes de viáticos mantienen sus reglas de empleado/jefe y empresa. Sus comprobaciones territoriales y los pasivos/movimientos derivados entran en los filtros anteriores. Pólizas quedan fuera hasta existir su motor, como indica la ficha.

## Rutas verificadas en el código

Tabla de rutas literales (el permiso/guarda de grupo aplica a todas sus rutas). Los catálogos maestros de tarjetas en el mismo archivo se distinguen de los movimientos TC, a los que sí se aplicó la guarda.

| Método | Ruta | Fuente |
|---|---|---|
| POST | `/api/v1/compras/requisiciones` | [Compras/RequisicionesEndpoints.cs:47](../../backend/src/Api/Endpoints/Compras/RequisicionesEndpoints.cs#L47) |
| GET | `/api/v1/compras/requisiciones/{id:guid}` | [Compras/RequisicionesEndpoints.cs:111](../../backend/src/Api/Endpoints/Compras/RequisicionesEndpoints.cs#L111) |
| GET | `/api/v1/compras/requisiciones/{id:guid}/cubrimiento-estimado` | [Compras/RequisicionesEndpoints.cs:145](../../backend/src/Api/Endpoints/Compras/RequisicionesEndpoints.cs#L145) |
| PATCH | `/api/v1/compras/requisiciones/{id:guid}` | [Compras/RequisicionesEndpoints.cs:177](../../backend/src/Api/Endpoints/Compras/RequisicionesEndpoints.cs#L177) |
| POST | `/api/v1/compras/requisiciones/{id:guid}/transmitir` | [Compras/RequisicionesEndpoints.cs:226](../../backend/src/Api/Endpoints/Compras/RequisicionesEndpoints.cs#L226) |
| POST | `/api/v1/compras/requisiciones/{id:guid}/autorizaciones` | [Compras/RequisicionesEndpoints.cs:258](../../backend/src/Api/Endpoints/Compras/RequisicionesEndpoints.cs#L258) |
| GET | `/api/v1/compras/motivos-rechazo` | [Compras/RequisicionesEndpoints.cs:333](../../backend/src/Api/Endpoints/Compras/RequisicionesEndpoints.cs#L333) |
| POST | `/api/v1/compras/requisiciones/{id:guid}/rechazar` | [Compras/RequisicionesEndpoints.cs:356](../../backend/src/Api/Endpoints/Compras/RequisicionesEndpoints.cs#L356) |
| POST | `/api/v1/compras/requisiciones/{id:guid}/eliminar` | [Compras/RequisicionesEndpoints.cs:392](../../backend/src/Api/Endpoints/Compras/RequisicionesEndpoints.cs#L392) |
| POST | `/api/v1/compras/requisiciones/{id:guid}/cancelar` | [Compras/RequisicionesEndpoints.cs:427](../../backend/src/Api/Endpoints/Compras/RequisicionesEndpoints.cs#L427) |
| POST | `/api/v1/compras/requisiciones/{id:guid}/cerrar-manual` | [Compras/RequisicionesEndpoints.cs:463](../../backend/src/Api/Endpoints/Compras/RequisicionesEndpoints.cs#L463) |
| GET | `/api/v1/compras/requisiciones` | [Compras/RequisicionesEndpoints.cs:501](../../backend/src/Api/Endpoints/Compras/RequisicionesEndpoints.cs#L501) |
| GET | `/api/v1/compras/pendientes-autorizacion` | [Compras/RequisicionesEndpoints.cs:537](../../backend/src/Api/Endpoints/Compras/RequisicionesEndpoints.cs#L537) |
| GET | `/api/v1/compras/requisiciones/{id:guid}/historico` | [Compras/RequisicionesEndpoints.cs:586](../../backend/src/Api/Endpoints/Compras/RequisicionesEndpoints.cs#L586) |
| POST | `/api/v1/compras/requisiciones/{id:guid}/lineas` | [Compras/LineasEndpoints.cs:41](../../backend/src/Api/Endpoints/Compras/LineasEndpoints.cs#L41) |
| PATCH | `/api/v1/compras/requisiciones/{id:guid}/lineas/{lineaId:guid}` | [Compras/LineasEndpoints.cs:83](../../backend/src/Api/Endpoints/Compras/LineasEndpoints.cs#L83) |
| PATCH | `/api/v1/compras/requisiciones/{id:guid}/lineas/{lineaId:guid}/notas` | [Compras/LineasEndpoints.cs:122](../../backend/src/Api/Endpoints/Compras/LineasEndpoints.cs#L122) |
| DELETE | `/api/v1/compras/requisiciones/{id:guid}/lineas/{lineaId:guid}` | [Compras/LineasEndpoints.cs:149](../../backend/src/Api/Endpoints/Compras/LineasEndpoints.cs#L149) |
| POST | `/api/v1/compras/ordenes` | [Compras/Oc/OrdenesCompraEndpoints.cs:69](../../backend/src/Api/Endpoints/Compras/Oc/OrdenesCompraEndpoints.cs#L69) |
| POST | `/api/v1/compras/ordenes/{id:guid}/duplicar` | [Compras/Oc/OrdenesCompraEndpoints.cs:140](../../backend/src/Api/Endpoints/Compras/Oc/OrdenesCompraEndpoints.cs#L140) |
| GET | `/api/v1/compras/ordenes/{id:guid}/origen` | [Compras/Oc/OrdenesCompraEndpoints.cs:179](../../backend/src/Api/Endpoints/Compras/Oc/OrdenesCompraEndpoints.cs#L179) |
| GET | `/api/v1/compras/ordenes/{id:guid}` | [Compras/Oc/OrdenesCompraEndpoints.cs:204](../../backend/src/Api/Endpoints/Compras/Oc/OrdenesCompraEndpoints.cs#L204) |
| GET | `/api/v1/compras/ordenes/{id:guid}/pdf` | [Compras/Oc/OrdenesCompraEndpoints.cs:238](../../backend/src/Api/Endpoints/Compras/Oc/OrdenesCompraEndpoints.cs#L238) |
| PATCH | `/api/v1/compras/ordenes/{id:guid}` | [Compras/Oc/OrdenesCompraEndpoints.cs:275](../../backend/src/Api/Endpoints/Compras/Oc/OrdenesCompraEndpoints.cs#L275) |
| POST | `/api/v1/compras/ordenes/{id:guid}/lineas` | [Compras/Oc/OrdenesCompraEndpoints.cs:330](../../backend/src/Api/Endpoints/Compras/Oc/OrdenesCompraEndpoints.cs#L330) |
| PATCH | `/api/v1/compras/ordenes/{id:guid}/lineas/{lineaId:guid}` | [Compras/Oc/OrdenesCompraEndpoints.cs:379](../../backend/src/Api/Endpoints/Compras/Oc/OrdenesCompraEndpoints.cs#L379) |
| DELETE | `/api/v1/compras/ordenes/{id:guid}/lineas/{lineaId:guid}` | [Compras/Oc/OrdenesCompraEndpoints.cs:425](../../backend/src/Api/Endpoints/Compras/Oc/OrdenesCompraEndpoints.cs#L425) |
| PATCH | `/api/v1/compras/ordenes/{id:guid}/lineas/{lineaId:guid}/texto-adicional` | [Compras/Oc/OrdenesCompraEndpoints.cs:453](../../backend/src/Api/Endpoints/Compras/Oc/OrdenesCompraEndpoints.cs#L453) |
| PATCH | `/api/v1/compras/ordenes/{id:guid}/referencia-proveedor` | [Compras/Oc/OrdenesCompraEndpoints.cs:486](../../backend/src/Api/Endpoints/Compras/Oc/OrdenesCompraEndpoints.cs#L486) |
| PATCH | `/api/v1/compras/ordenes/{id:guid}/contacto-proveedor` | [Compras/Oc/OrdenesCompraEndpoints.cs:517](../../backend/src/Api/Endpoints/Compras/Oc/OrdenesCompraEndpoints.cs#L517) |
| PATCH | `/api/v1/compras/ordenes/{id:guid}/informacion-logistica` | [Compras/Oc/OrdenesCompraEndpoints.cs:546](../../backend/src/Api/Endpoints/Compras/Oc/OrdenesCompraEndpoints.cs#L546) |
| PATCH | `/api/v1/compras/ordenes/{id:guid}/informacion-importacion` | [Compras/Oc/OrdenesCompraEndpoints.cs:583](../../backend/src/Api/Endpoints/Compras/Oc/OrdenesCompraEndpoints.cs#L583) |
| POST | `/api/v1/compras/ordenes/{id:guid}/transmitir` | [Compras/Oc/OrdenesCompraEndpoints.cs:620](../../backend/src/Api/Endpoints/Compras/Oc/OrdenesCompraEndpoints.cs#L620) |
| POST | `/api/v1/compras/ordenes/{id:guid}/cerrar-manual` | [Compras/Oc/OrdenesCompraEndpoints.cs:652](../../backend/src/Api/Endpoints/Compras/Oc/OrdenesCompraEndpoints.cs#L652) |
| POST | `/api/v1/compras/ordenes/{id:guid}/autorizaciones` | [Compras/Oc/OrdenesCompraEndpoints.cs:684](../../backend/src/Api/Endpoints/Compras/Oc/OrdenesCompraEndpoints.cs#L684) |
| POST | `/api/v1/compras/ordenes/desde-requisicion` | [Compras/Oc/OrdenesCompraEndpoints.cs:756](../../backend/src/Api/Endpoints/Compras/Oc/OrdenesCompraEndpoints.cs#L756) |
| POST | `/api/v1/compras/ordenes/{id:guid}/lineas/desde-requisicion` | [Compras/Oc/OrdenesCompraEndpoints.cs:784](../../backend/src/Api/Endpoints/Compras/Oc/OrdenesCompraEndpoints.cs#L784) |
| GET | `/api/v1/compras/ordenes` | [Compras/Oc/OrdenesCompraEndpoints.cs:820](../../backend/src/Api/Endpoints/Compras/Oc/OrdenesCompraEndpoints.cs#L820) |
| GET | `/api/v1/compras/ordenes/pendientes-autorizacion` | [Compras/Oc/OrdenesCompraEndpoints.cs:874](../../backend/src/Api/Endpoints/Compras/Oc/OrdenesCompraEndpoints.cs#L874) |
| GET | `/api/v1/compras/ordenes/partidas-abiertas` | [Compras/Oc/OrdenesCompraEndpoints.cs:904](../../backend/src/Api/Endpoints/Compras/Oc/OrdenesCompraEndpoints.cs#L904) |
| GET | `/api/v1/compras/ordenes/partidas-abiertas/kpis` | [Compras/Oc/OrdenesCompraEndpoints.cs:960](../../backend/src/Api/Endpoints/Compras/Oc/OrdenesCompraEndpoints.cs#L960) |
| GET | `/api/v1/compras/ordenes/{id:guid}/historico` | [Compras/Oc/OrdenesCompraEndpoints.cs:988](../../backend/src/Api/Endpoints/Compras/Oc/OrdenesCompraEndpoints.cs#L988) |
| GET | `/api/v1/compras/ordenes/{id:guid}/duplicadas` | [Compras/Oc/OrdenesCompraEndpoints.cs:1015](../../backend/src/Api/Endpoints/Compras/Oc/OrdenesCompraEndpoints.cs#L1015) |
| GET | `/api/v1/compras/ordenes/requisiciones-disponibles` | [Compras/Oc/OrdenesCompraEndpoints.cs:1042](../../backend/src/Api/Endpoints/Compras/Oc/OrdenesCompraEndpoints.cs#L1042) |
| POST | `/api/v1/compras/ordenes/{id:guid}/cancelar` | [Compras/Oc/OrdenesCompraEndpoints.cs:1073](../../backend/src/Api/Endpoints/Compras/Oc/OrdenesCompraEndpoints.cs#L1073) |
| POST | `/api/v1/compras/ordenes/{id:guid}/cancelar-con-recepciones` | [Compras/Oc/OrdenesCompraEndpoints.cs:1102](../../backend/src/Api/Endpoints/Compras/Oc/OrdenesCompraEndpoints.cs#L1102) |
| POST | `/api/v1/compras/ordenes/{id:guid}/rechazar` | [Compras/Oc/OrdenesCompraEndpoints.cs:1183](../../backend/src/Api/Endpoints/Compras/Oc/OrdenesCompraEndpoints.cs#L1183) |
| GET | `/api/v1/compras/ordenes/{id:guid}/adjuntos/{adjuntoId:guid}/contenido` | [Compras/Oc/OrdenesCompraEndpoints.cs:1273](../../backend/src/Api/Endpoints/Compras/Oc/OrdenesCompraEndpoints.cs#L1273) |
| POST | `/api/v1/compras/ordenes/{id:guid}/adjuntos` | [Compras/Oc/OrdenesCompraEndpoints.cs:1318](../../backend/src/Api/Endpoints/Compras/Oc/OrdenesCompraEndpoints.cs#L1318) |
| DELETE | `/api/v1/compras/ordenes/{id:guid}/adjuntos/{adjuntoId:guid}` | [Compras/Oc/OrdenesCompraEndpoints.cs:1430](../../backend/src/Api/Endpoints/Compras/Oc/OrdenesCompraEndpoints.cs#L1430) |
| PATCH | `/api/v1/compras/ordenes/{id:guid}/numero-pedimento` | [Compras/Oc/OrdenesCompraEndpoints.cs:1461](../../backend/src/Api/Endpoints/Compras/Oc/OrdenesCompraEndpoints.cs#L1461) |
| GET | `/api/v1/compras/articulos/{id:guid}/historial-compras` | [Compras/Articulos/HistorialComprasMaterialEndpoint.cs:22](../../backend/src/Api/Endpoints/Compras/Articulos/HistorialComprasMaterialEndpoint.cs#L22) |
| GET | `/api/v1/compras/trazabilidad/arbol-documentos` | [Compras/Trazabilidad/ArbolDocumentosEndpoint.cs:28](../../backend/src/Api/Endpoints/Compras/Trazabilidad/ArbolDocumentosEndpoint.cs#L28) |
| GET | `/api/v1/cuentas-por-pagar/facturas` | [CuentasPorPagar/FacturasEndpoints.cs:46](../../backend/src/Api/Endpoints/CuentasPorPagar/FacturasEndpoints.cs#L46) |
| GET | `/api/v1/cuentas-por-pagar/facturas/{id:guid}` | [CuentasPorPagar/FacturasEndpoints.cs:67](../../backend/src/Api/Endpoints/CuentasPorPagar/FacturasEndpoints.cs#L67) |
| POST | `/api/v1/cuentas-por-pagar/facturas` | [CuentasPorPagar/FacturasEndpoints.cs:80](../../backend/src/Api/Endpoints/CuentasPorPagar/FacturasEndpoints.cs#L80) |
| PATCH | `/api/v1/cuentas-por-pagar/facturas/{id:guid}` | [CuentasPorPagar/FacturasEndpoints.cs:113](../../backend/src/Api/Endpoints/CuentasPorPagar/FacturasEndpoints.cs#L113) |
| GET | `/api/v1/cuentas-por-pagar/facturas/en-revision` | [CuentasPorPagar/FacturasEndpoints.cs:150](../../backend/src/Api/Endpoints/CuentasPorPagar/FacturasEndpoints.cs#L150) |
| POST | `/api/v1/cuentas-por-pagar/facturas/{id:guid}/enviar-revision` | [CuentasPorPagar/FacturasEndpoints.cs:168](../../backend/src/Api/Endpoints/CuentasPorPagar/FacturasEndpoints.cs#L168) |
| POST | `/api/v1/cuentas-por-pagar/facturas/{id:guid}/liberar-revision` | [CuentasPorPagar/FacturasEndpoints.cs:197](../../backend/src/Api/Endpoints/CuentasPorPagar/FacturasEndpoints.cs#L197) |
| POST | `/api/v1/cuentas-por-pagar/facturas/{id:guid}/autorizar` | [CuentasPorPagar/FacturasEndpoints.cs:221](../../backend/src/Api/Endpoints/CuentasPorPagar/FacturasEndpoints.cs#L221) |
| POST | `/api/v1/cuentas-por-pagar/facturas/{id:guid}/cancelar` | [CuentasPorPagar/FacturasEndpoints.cs:251](../../backend/src/Api/Endpoints/CuentasPorPagar/FacturasEndpoints.cs#L251) |
| POST | `/api/v1/cuentas-por-pagar/facturas/{id:guid}/aplicar-nc` | [CuentasPorPagar/FacturasEndpoints.cs:283](../../backend/src/Api/Endpoints/CuentasPorPagar/FacturasEndpoints.cs#L283) |
| POST | `/api/v1/cuentas-por-pagar/facturas/{id:guid}/aplicar-anticipo` | [CuentasPorPagar/FacturasEndpoints.cs:328](../../backend/src/Api/Endpoints/CuentasPorPagar/FacturasEndpoints.cs#L328) |
| GET | `/api/v1/cuentas-por-pagar/facturas/{facturaId:guid}/evidencias` | [CuentasPorPagar/EvidenciasEndpoints.cs:30](../../backend/src/Api/Endpoints/CuentasPorPagar/EvidenciasEndpoints.cs#L30) |
| POST | `/api/v1/cuentas-por-pagar/facturas/{facturaId:guid}/evidencias` | [CuentasPorPagar/EvidenciasEndpoints.cs:44](../../backend/src/Api/Endpoints/CuentasPorPagar/EvidenciasEndpoints.cs#L44) |
| GET | `/api/v1/cuentas-por-pagar/anticipos` | [CuentasPorPagar/AnticiposEndpoints.cs:30](../../backend/src/Api/Endpoints/CuentasPorPagar/AnticiposEndpoints.cs#L30) |
| POST | `/api/v1/cuentas-por-pagar/anticipos` | [CuentasPorPagar/AnticiposEndpoints.cs:48](../../backend/src/Api/Endpoints/CuentasPorPagar/AnticiposEndpoints.cs#L48) |
| GET | `/api/v1/cuentas-por-pagar/notas-cargo` | [CuentasPorPagar/NotasCargoEndpoints.cs:29](../../backend/src/Api/Endpoints/CuentasPorPagar/NotasCargoEndpoints.cs#L29) |
| POST | `/api/v1/cuentas-por-pagar/notas-cargo` | [CuentasPorPagar/NotasCargoEndpoints.cs:47](../../backend/src/Api/Endpoints/CuentasPorPagar/NotasCargoEndpoints.cs#L47) |
| POST | `/api/v1/cuentas-por-pagar/notas-cargo/{id:guid}/autorizar` | [CuentasPorPagar/NotasCargoEndpoints.cs:71](../../backend/src/Api/Endpoints/CuentasPorPagar/NotasCargoEndpoints.cs#L71) |
| POST | `/api/v1/cuentas-por-pagar/notas-cargo/{id:guid}/aplicar` | [CuentasPorPagar/NotasCargoEndpoints.cs:98](../../backend/src/Api/Endpoints/CuentasPorPagar/NotasCargoEndpoints.cs#L98) |
| GET | `/api/v1/cuentas-por-pagar/notas-cargo/{id:guid}` | [CuentasPorPagar/NotasCargoEndpoints.cs:126](../../backend/src/Api/Endpoints/CuentasPorPagar/NotasCargoEndpoints.cs#L126) |
| GET | `/api/v1/cuentas-por-pagar/notas-credito` | [CuentasPorPagar/NotasCreditoEndpoints.cs:34](../../backend/src/Api/Endpoints/CuentasPorPagar/NotasCreditoEndpoints.cs#L34) |
| POST | `/api/v1/cuentas-por-pagar/notas-credito` | [CuentasPorPagar/NotasCreditoEndpoints.cs:52](../../backend/src/Api/Endpoints/CuentasPorPagar/NotasCreditoEndpoints.cs#L52) |
| POST | `/api/v1/cuentas-por-pagar/notas-credito/{id:guid}/vincular-factura` | [CuentasPorPagar/NotasCreditoEndpoints.cs:80](../../backend/src/Api/Endpoints/CuentasPorPagar/NotasCreditoEndpoints.cs#L80) |
| GET | `/api/v1/cuentas-por-pagar/notas-credito/{id:guid}` | [CuentasPorPagar/NotasCreditoEndpoints.cs:114](../../backend/src/Api/Endpoints/CuentasPorPagar/NotasCreditoEndpoints.cs#L114) |
| GET | `/api/v1/cuentas-por-pagar/comprobaciones` | [CuentasPorPagar/ComprobacionesEndpoints.cs:34](../../backend/src/Api/Endpoints/CuentasPorPagar/ComprobacionesEndpoints.cs#L34) |
| POST | `/api/v1/cuentas-por-pagar/comprobaciones/caja-chica` | [CuentasPorPagar/ComprobacionesEndpoints.cs:53](../../backend/src/Api/Endpoints/CuentasPorPagar/ComprobacionesEndpoints.cs#L53) |
| POST | `/api/v1/cuentas-por-pagar/comprobaciones/{id:guid}/enviar-revision` | [CuentasPorPagar/ComprobacionesEndpoints.cs:74](../../backend/src/Api/Endpoints/CuentasPorPagar/ComprobacionesEndpoints.cs#L74) |
| POST | `/api/v1/cuentas-por-pagar/comprobaciones/{id:guid}/autorizar` | [CuentasPorPagar/ComprobacionesEndpoints.cs:95](../../backend/src/Api/Endpoints/CuentasPorPagar/ComprobacionesEndpoints.cs#L95) |
| POST | `/api/v1/cuentas-por-pagar/comprobaciones/{id:guid}/aplicar` | [CuentasPorPagar/ComprobacionesEndpoints.cs:117](../../backend/src/Api/Endpoints/CuentasPorPagar/ComprobacionesEndpoints.cs#L117) |
| POST | `/api/v1/cuentas-por-pagar/comprobaciones/aduanales` | [CuentasPorPagar/ComprobacionesEndpoints.cs:140](../../backend/src/Api/Endpoints/CuentasPorPagar/ComprobacionesEndpoints.cs#L140) |
| POST | `/api/v1/cuentas-por-pagar/comprobaciones/{id:guid}/autorizar-nivel1` | [CuentasPorPagar/ComprobacionesEndpoints.cs:165](../../backend/src/Api/Endpoints/CuentasPorPagar/ComprobacionesEndpoints.cs#L165) |
| POST | `/api/v1/cuentas-por-pagar/comprobaciones/{id:guid}/autorizar-nivel2` | [CuentasPorPagar/ComprobacionesEndpoints.cs:187](../../backend/src/Api/Endpoints/CuentasPorPagar/ComprobacionesEndpoints.cs#L187) |
| POST | `/api/v1/cuentas-por-pagar/comprobaciones/{id:guid}/rechazar` | [CuentasPorPagar/ComprobacionesEndpoints.cs:211](../../backend/src/Api/Endpoints/CuentasPorPagar/ComprobacionesEndpoints.cs#L211) |
| GET | `/api/v1/cuentas-por-pagar/comprobaciones/{id:guid}` | [CuentasPorPagar/ComprobacionesEndpoints.cs:236](../../backend/src/Api/Endpoints/CuentasPorPagar/ComprobacionesEndpoints.cs#L236) |
| GET | `/api/v1/cuentas-por-pagar/reposiciones-caja` | [CuentasPorPagar/ReposicionesCajaEndpoints.cs:27](../../backend/src/Api/Endpoints/CuentasPorPagar/ReposicionesCajaEndpoints.cs#L27) |
| GET | `/api/v1/cuentas-por-pagar/reposiciones-caja/saldos` | [CuentasPorPagar/ReposicionesCajaEndpoints.cs:42](../../backend/src/Api/Endpoints/CuentasPorPagar/ReposicionesCajaEndpoints.cs#L42) |
| POST | `/api/v1/cuentas-por-pagar/reposiciones-caja/emitir` | [CuentasPorPagar/ReposicionesCajaEndpoints.cs:54](../../backend/src/Api/Endpoints/CuentasPorPagar/ReposicionesCajaEndpoints.cs#L54) |
| PUT | `/api/v1/cuentas-por-pagar/reposiciones-caja/configuracion` | [CuentasPorPagar/ReposicionesCajaEndpoints.cs:72](../../backend/src/Api/Endpoints/CuentasPorPagar/ReposicionesCajaEndpoints.cs#L72) |
| GET | `/api/v1/cuentas-por-pagar/cfdis` | [CuentasPorPagar/CfdisEndpoints.cs:42](../../backend/src/Api/Endpoints/CuentasPorPagar/CfdisEndpoints.cs#L42) |
| GET | `/api/v1/cuentas-por-pagar/cfdis/{id:guid}` | [CuentasPorPagar/CfdisEndpoints.cs:72](../../backend/src/Api/Endpoints/CuentasPorPagar/CfdisEndpoints.cs#L72) |
| GET | `/api/v1/cuentas-por-pagar/cfdis/{id:guid}/xml` | [CuentasPorPagar/CfdisEndpoints.cs:92](../../backend/src/Api/Endpoints/CuentasPorPagar/CfdisEndpoints.cs#L92) |
| GET | `/api/v1/cuentas-por-pagar/cfdis/{id:guid}/pdf` | [CuentasPorPagar/CfdisEndpoints.cs:108](../../backend/src/Api/Endpoints/CuentasPorPagar/CfdisEndpoints.cs#L108) |
| GET | `/api/v1/cuentas-por-pagar/cfdis/{id:guid}/parseado` | [CuentasPorPagar/CfdisEndpoints.cs:124](../../backend/src/Api/Endpoints/CuentasPorPagar/CfdisEndpoints.cs#L124) |
| POST | `/api/v1/cuentas-por-pagar/cfdis/cargar` | [CuentasPorPagar/CfdisEndpoints.cs:145](../../backend/src/Api/Endpoints/CuentasPorPagar/CfdisEndpoints.cs#L145) |
| POST | `/api/v1/cuentas-por-pagar/cfdis/{id:guid}/descartar` | [CuentasPorPagar/CfdisEndpoints.cs:200](../../backend/src/Api/Endpoints/CuentasPorPagar/CfdisEndpoints.cs#L200) |
| POST | `/api/v1/cuentas-por-pagar/cfdis/{id:guid}/marcar-duplicado` | [CuentasPorPagar/CfdisEndpoints.cs:218](../../backend/src/Api/Endpoints/CuentasPorPagar/CfdisEndpoints.cs#L218) |
| GET | `/api/v1/cuentas-por-pagar/estados-cuenta-tc` | [CuentasPorPagar/EstadosCuentaTcEndpoints.cs:29](../../backend/src/Api/Endpoints/CuentasPorPagar/EstadosCuentaTcEndpoints.cs#L29) |
| POST | `/api/v1/cuentas-por-pagar/estados-cuenta-tc` | [CuentasPorPagar/EstadosCuentaTcEndpoints.cs:46](../../backend/src/Api/Endpoints/CuentasPorPagar/EstadosCuentaTcEndpoints.cs#L46) |
| POST | `/api/v1/cuentas-por-pagar/estados-cuenta-tc/{id:guid}/archivo` | [CuentasPorPagar/EstadosCuentaTcEndpoints.cs:66](../../backend/src/Api/Endpoints/CuentasPorPagar/EstadosCuentaTcEndpoints.cs#L66) |
| POST | `/api/v1/cuentas-por-pagar/estados-cuenta-tc/{id:guid}/conciliar-automatico` | [CuentasPorPagar/EstadosCuentaTcEndpoints.cs:120](../../backend/src/Api/Endpoints/CuentasPorPagar/EstadosCuentaTcEndpoints.cs#L120) |
| POST | `/api/v1/cuentas-por-pagar/estados-cuenta-tc/{id:guid}/lineas/{lineaId:guid}/confirmar-match` | [CuentasPorPagar/EstadosCuentaTcEndpoints.cs:144](../../backend/src/Api/Endpoints/CuentasPorPagar/EstadosCuentaTcEndpoints.cs#L144) |
| POST | `/api/v1/cuentas-por-pagar/estados-cuenta-tc/{id:guid}/lineas/{lineaId:guid}/capturar-retroactiva` | [CuentasPorPagar/EstadosCuentaTcEndpoints.cs:171](../../backend/src/Api/Endpoints/CuentasPorPagar/EstadosCuentaTcEndpoints.cs#L171) |
| POST | `/api/v1/cuentas-por-pagar/estados-cuenta-tc/{id:guid}/marcar-conciliado` | [CuentasPorPagar/EstadosCuentaTcEndpoints.cs:193](../../backend/src/Api/Endpoints/CuentasPorPagar/EstadosCuentaTcEndpoints.cs#L193) |
| POST | `/api/v1/cuentas-por-pagar/estados-cuenta-tc/{id:guid}/cerrar` | [CuentasPorPagar/EstadosCuentaTcEndpoints.cs:214](../../backend/src/Api/Endpoints/CuentasPorPagar/EstadosCuentaTcEndpoints.cs#L214) |
| POST | `/api/v1/cuentas-por-pagar/estados-cuenta-tc/{id:guid}/marcar-pagado-banco` | [CuentasPorPagar/EstadosCuentaTcEndpoints.cs:236](../../backend/src/Api/Endpoints/CuentasPorPagar/EstadosCuentaTcEndpoints.cs#L236) |
| GET | `/api/v1/cuentas-por-pagar/tarjetas` | [CuentasPorPagar/TarjetasCreditoEndpoints.cs:33](../../backend/src/Api/Endpoints/CuentasPorPagar/TarjetasCreditoEndpoints.cs#L33) |
| POST | `/api/v1/cuentas-por-pagar/tarjetas` | [CuentasPorPagar/TarjetasCreditoEndpoints.cs:50](../../backend/src/Api/Endpoints/CuentasPorPagar/TarjetasCreditoEndpoints.cs#L50) |
| PATCH | `/api/v1/cuentas-por-pagar/tarjetas/{id:guid}` | [CuentasPorPagar/TarjetasCreditoEndpoints.cs:64](../../backend/src/Api/Endpoints/CuentasPorPagar/TarjetasCreditoEndpoints.cs#L64) |
| POST | `/api/v1/cuentas-por-pagar/tarjetas/{id:guid}/bloquear` | [CuentasPorPagar/TarjetasCreditoEndpoints.cs:83](../../backend/src/Api/Endpoints/CuentasPorPagar/TarjetasCreditoEndpoints.cs#L83) |
| POST | `/api/v1/cuentas-por-pagar/tarjetas/{id:guid}/reactivar` | [CuentasPorPagar/TarjetasCreditoEndpoints.cs:102](../../backend/src/Api/Endpoints/CuentasPorPagar/TarjetasCreditoEndpoints.cs#L102) |
| POST | `/api/v1/cuentas-por-pagar/tarjetas/{id:guid}/cancelar` | [CuentasPorPagar/TarjetasCreditoEndpoints.cs:119](../../backend/src/Api/Endpoints/CuentasPorPagar/TarjetasCreditoEndpoints.cs#L119) |
| POST | `/api/v1/cuentas-por-pagar/tarjetas/{id:guid}/usuarios` | [CuentasPorPagar/TarjetasCreditoEndpoints.cs:138](../../backend/src/Api/Endpoints/CuentasPorPagar/TarjetasCreditoEndpoints.cs#L138) |
| POST | `/api/v1/cuentas-por-pagar/tarjetas/{id:guid}/usuarios/{usuarioId:guid}/cerrar` | [CuentasPorPagar/TarjetasCreditoEndpoints.cs:158](../../backend/src/Api/Endpoints/CuentasPorPagar/TarjetasCreditoEndpoints.cs#L158) |
| GET | `/api/v1/cuentas-por-pagar/movimientos-tc` | [CuentasPorPagar/TarjetasCreditoEndpoints.cs:184](../../backend/src/Api/Endpoints/CuentasPorPagar/TarjetasCreditoEndpoints.cs#L184) |
| POST | `/api/v1/cuentas-por-pagar/movimientos-tc/con-cfdi` | [CuentasPorPagar/TarjetasCreditoEndpoints.cs:207](../../backend/src/Api/Endpoints/CuentasPorPagar/TarjetasCreditoEndpoints.cs#L207) |
| POST | `/api/v1/cuentas-por-pagar/movimientos-tc/sin-cfdi` | [CuentasPorPagar/TarjetasCreditoEndpoints.cs:226](../../backend/src/Api/Endpoints/CuentasPorPagar/TarjetasCreditoEndpoints.cs#L226) |
| POST | `/api/v1/cuentas-por-pagar/movimientos-tc/refund` | [CuentasPorPagar/TarjetasCreditoEndpoints.cs:247](../../backend/src/Api/Endpoints/CuentasPorPagar/TarjetasCreditoEndpoints.cs#L247) |
| POST | `/api/v1/cuentas-por-pagar/movimientos-tc/especial` | [CuentasPorPagar/TarjetasCreditoEndpoints.cs:266](../../backend/src/Api/Endpoints/CuentasPorPagar/TarjetasCreditoEndpoints.cs#L266) |
| POST | `/api/v1/cuentas-por-pagar/movimientos-tc/{id:guid}/disputar` | [CuentasPorPagar/TarjetasCreditoEndpoints.cs:283](../../backend/src/Api/Endpoints/CuentasPorPagar/TarjetasCreditoEndpoints.cs#L283) |
| POST | `/api/v1/cuentas-por-pagar/movimientos-tc/{id:guid}/resolver-disputa` | [CuentasPorPagar/TarjetasCreditoEndpoints.cs:309](../../backend/src/Api/Endpoints/CuentasPorPagar/TarjetasCreditoEndpoints.cs#L309) |
| GET | `/api/v1/cuentas-por-pagar/reportes/antiguedad-saldos` | [CuentasPorPagar/ReportesEndpoints.cs:33](../../backend/src/Api/Endpoints/CuentasPorPagar/ReportesEndpoints.cs#L33) |
| GET | `/api/v1/cuentas-por-pagar/reportes/antiguedad-anticipos` | [CuentasPorPagar/ReportesEndpoints.cs:50](../../backend/src/Api/Endpoints/CuentasPorPagar/ReportesEndpoints.cs#L50) |
| GET | `/api/v1/cuentas-por-pagar/reportes/cartera` | [CuentasPorPagar/ReportesEndpoints.cs:66](../../backend/src/Api/Endpoints/CuentasPorPagar/ReportesEndpoints.cs#L66) |
| GET | `/api/v1/cuentas-por-pagar/reportes/movimientos-tc-pendientes` | [CuentasPorPagar/ReportesEndpoints.cs:86](../../backend/src/Api/Endpoints/CuentasPorPagar/ReportesEndpoints.cs#L86) |
| GET | `/api/v1/cuentas-por-pagar/reportes/estados-cuenta-tc-consolidado` | [CuentasPorPagar/ReportesEndpoints.cs:104](../../backend/src/Api/Endpoints/CuentasPorPagar/ReportesEndpoints.cs#L104) |
| GET | `/api/v1/cuentas-por-pagar/reportes/pasivos-obras` | [CuentasPorPagar/ReportesEndpoints.cs:122](../../backend/src/Api/Endpoints/CuentasPorPagar/ReportesEndpoints.cs#L122) |
| GET | `/api/v1/cuentas-por-cobrar/cartera/saldo-neto/{clienteId:guid}` | [CuentasPorCobrar/CarteraEndpoints.cs:30](../../backend/src/Api/Endpoints/CuentasPorCobrar/CarteraEndpoints.cs#L30) |
| GET | `/api/v1/cuentas-por-cobrar/cartera/antiguedad` | [CuentasPorCobrar/CarteraEndpoints.cs:43](../../backend/src/Api/Endpoints/CuentasPorCobrar/CarteraEndpoints.cs#L43) |
| GET | `/api/v1/cuentas-por-cobrar/cartera/estado-cuenta/{clienteId:guid}` | [CuentasPorCobrar/CarteraEndpoints.cs:58](../../backend/src/Api/Endpoints/CuentasPorCobrar/CarteraEndpoints.cs#L58) |
| GET | `/api/v1/cuentas-por-cobrar/cartera/facturas-abiertas` | [CuentasPorCobrar/CarteraEndpoints.cs:73](../../backend/src/Api/Endpoints/CuentasPorCobrar/CarteraEndpoints.cs#L73) |
| GET | `/api/v1/cuentas-por-cobrar/anticipos` | [CuentasPorCobrar/CarteraEndpoints.cs:87](../../backend/src/Api/Endpoints/CuentasPorCobrar/CarteraEndpoints.cs#L87) |
| GET | `/api/v1/cuentas-por-cobrar/alertas` | [CuentasPorCobrar/AlertasEndpoints.cs:28](../../backend/src/Api/Endpoints/CuentasPorCobrar/AlertasEndpoints.cs#L28) |
| POST | `/api/v1/cuentas-por-cobrar/alertas/{id:guid}/atender` | [CuentasPorCobrar/AlertasEndpoints.cs:46](../../backend/src/Api/Endpoints/CuentasPorCobrar/AlertasEndpoints.cs#L46) |
| GET | `/api/v1/cuentas-por-cobrar/cobranza` | [CuentasPorCobrar/CobranzaEndpoints.cs:28](../../backend/src/Api/Endpoints/CuentasPorCobrar/CobranzaEndpoints.cs#L28) |
| POST | `/api/v1/cuentas-por-cobrar/cobranza` | [CuentasPorCobrar/CobranzaEndpoints.cs:45](../../backend/src/Api/Endpoints/CuentasPorCobrar/CobranzaEndpoints.cs#L45) |
| GET | `/api/v1/cuentas-por-cobrar/propuestas-aplicacion/tolerancias` | [CuentasPorCobrar/PropuestasAplicacionEndpoints.cs:35](../../backend/src/Api/Endpoints/CuentasPorCobrar/PropuestasAplicacionEndpoints.cs#L35) |
| GET | `/api/v1/cuentas-por-cobrar/propuestas-aplicacion` | [CuentasPorCobrar/PropuestasAplicacionEndpoints.cs:42](../../backend/src/Api/Endpoints/CuentasPorCobrar/PropuestasAplicacionEndpoints.cs#L42) |
| GET | `/api/v1/cuentas-por-cobrar/propuestas-aplicacion/{id:guid}` | [CuentasPorCobrar/PropuestasAplicacionEndpoints.cs:59](../../backend/src/Api/Endpoints/CuentasPorCobrar/PropuestasAplicacionEndpoints.cs#L59) |
| POST | `/api/v1/cuentas-por-cobrar/propuestas-aplicacion` | [CuentasPorCobrar/PropuestasAplicacionEndpoints.cs:72](../../backend/src/Api/Endpoints/CuentasPorCobrar/PropuestasAplicacionEndpoints.cs#L72) |
| GET | `/api/v1/cuentas-por-cobrar/credito-disponible/{clienteId:guid}` | [CuentasPorCobrar/CreditoDisponibleEndpoints.cs:20](../../backend/src/Api/Endpoints/CuentasPorCobrar/CreditoDisponibleEndpoints.cs#L20) |
| GET | `/api/v1/tesoreria/pasivos-pendientes` | [Tesoreria/PasivosEndpoints.cs:29](../../backend/src/Api/Endpoints/Tesoreria/PasivosEndpoints.cs#L29) |
| POST | `/api/v1/tesoreria/pasivos-pendientes/{facturaProveedorId:guid}/solicitar-cancelacion` | [Tesoreria/PasivosEndpoints.cs:58](../../backend/src/Api/Endpoints/Tesoreria/PasivosEndpoints.cs#L58) |
| POST | `/api/v1/tesoreria/pagos` | [Tesoreria/PagosEndpoints.cs:27](../../backend/src/Api/Endpoints/Tesoreria/PagosEndpoints.cs#L27) |
| POST | `/api/v1/tesoreria/pagos/{pagoId:guid}/revertir` | [Tesoreria/PagosEndpoints.cs:48](../../backend/src/Api/Endpoints/Tesoreria/PagosEndpoints.cs#L48) |
| GET | `/api/v1/tesoreria/pagos-cuenta` | [Tesoreria/PagosACuentaEndpoints.cs:28](../../backend/src/Api/Endpoints/Tesoreria/PagosACuentaEndpoints.cs#L28) |
| POST | `/api/v1/tesoreria/pagos-cuenta` | [Tesoreria/PagosACuentaEndpoints.cs:45](../../backend/src/Api/Endpoints/Tesoreria/PagosACuentaEndpoints.cs#L45) |
| POST | `/api/v1/tesoreria/pagos-cuenta/{movimientoId:guid}/ligar` | [Tesoreria/PagosACuentaEndpoints.cs:64](../../backend/src/Api/Endpoints/Tesoreria/PagosACuentaEndpoints.cs#L64) |
| GET | `/api/v1/tesoreria/depositos` | [Tesoreria/DepositosEndpoints.cs:36](../../backend/src/Api/Endpoints/Tesoreria/DepositosEndpoints.cs#L36) |
| POST | `/api/v1/tesoreria/depositos/{id:guid}/confirmar` | [Tesoreria/DepositosEndpoints.cs:54](../../backend/src/Api/Endpoints/Tesoreria/DepositosEndpoints.cs#L54) |
| POST | `/api/v1/tesoreria/depositos/{id:guid}/rechazar` | [Tesoreria/DepositosEndpoints.cs:80](../../backend/src/Api/Endpoints/Tesoreria/DepositosEndpoints.cs#L80) |
| GET | `/api/v1/tesoreria/movimientos` | [Tesoreria/MovimientosEndpoints.cs:29](../../backend/src/Api/Endpoints/Tesoreria/MovimientosEndpoints.cs#L29) |
| GET | `/api/v1/tesoreria/movimientos/{id:guid}` | [Tesoreria/MovimientosEndpoints.cs:52](../../backend/src/Api/Endpoints/Tesoreria/MovimientosEndpoints.cs#L52) |
| POST | `/api/v1/tesoreria/movimientos` | [Tesoreria/MovimientosEndpoints.cs:65](../../backend/src/Api/Endpoints/Tesoreria/MovimientosEndpoints.cs#L65) |
| GET | `/api/v1/tesoreria/repp-pendientes` | [Tesoreria/ReppEndpoints.cs:25](../../backend/src/Api/Endpoints/Tesoreria/ReppEndpoints.cs#L25) |
| POST | `/api/v1/tesoreria/repp-recibidos` | [Tesoreria/ReppEndpoints.cs:46](../../backend/src/Api/Endpoints/Tesoreria/ReppEndpoints.cs#L46) |
| GET | `/api/v1/tesoreria/reportes/flujo-efectivo` | [Tesoreria/ReportesEndpoints.cs:25](../../backend/src/Api/Endpoints/Tesoreria/ReportesEndpoints.cs#L25) |
| GET | `/api/v1/tesoreria/reportes/auxiliar-bancos` | [Tesoreria/ReportesEndpoints.cs:42](../../backend/src/Api/Endpoints/Tesoreria/ReportesEndpoints.cs#L42) |

La plataforma genérica agrega, para RQ y factura de proveedor: `POST /{id}/adjuntos`, `GET /{id}/adjuntos`, `GET /{id}/adjuntos/{adjuntoId}`, `GET /{id}/adjuntos/{adjuntoId}/contenido`, `POST /{id}/adjuntos/{adjuntoId}/enlace` y `DELETE /{id}/adjuntos/{adjuntoId}`. Valida permiso del tipo y alcance en `AdjuntoAcceso`/`IAdjuntoPropietario`. Los adjuntos OC existentes conservan sus rutas y permisos.

## Pruebas y aceptación

`P6SucursalEndpointsTests` contiene casos por ruta para lecturas, escrituras y altas: documento de otra sucursal → 403; listados → se excluye el ID ajeno; lectura corporativa → ambos documentos; escritura propia/corporativa válida de RQ → persiste el cambio. Los archivos de prueba y referencias adicionales están en `RESUMEN.md`.

Se reutilizan sucursales y proveedor del seed; las cuentas y tarjetas ficticias se limpian. Las pruebas con PostgreSQL están escritas y compiladas, **no verdes aquí**. El filtro por empresa se conserva en todos los puertos. Los casos usan dos sucursales del seed; la reproducción con los usuarios reales Cancún/Circuito permanece pendiente de la sesión Millet.

## Rutas incorporadas por main y revisadas en esta continuación

| Método | Ruta | Control confirmado en código |
|---|---|---|
| POST | `/api/v1/compras/ordenes/{id:guid}/resolver-cancelacion` | Guarda de escritura OC agregada; firma de Dirección y resolución P2 intactas |
| POST | `/api/v1/tesoreria/movimientos/{id:guid}/reclasificar` | Hereda filtro de endpoint del grupo `movimiento_bancario`; solo se añadió regresión |
| POST | `/api/v1/tesoreria/pagos-cuenta/{movimientoId:guid}/aplicaciones/{aplicacionId:guid}/desligar` | Hereda guarda del movimiento padre; handler comprueba que la aplicación pertenezca a ese movimiento |
| GET | `/api/v1/cuentas-por-pagar/reportes/auxiliar-proveedores` | `SaldosHistoricos` de P8 aplica asociación/bypass `cuentas_por_pagar.reportes.leer-todas-sucursales` |
| GET | `/api/v1/tesoreria/reportes/auxiliar-bancos` | Guarda por cuenta y filtro CQRS; valida todos los orígenes antes de mostrar el saldo completo |
| GET | `/api/v1/tesoreria/reportes/flujo-efectivo` | Filtro de cuentas antes de calcular saldos iniciales/finales; una cuenta solicitada explícitamente valida su alcance |

Las rutas de confirmación/rechazo bajo propuestas de CxC fueron retiradas por P5; sus operaciones permanecen en `/tesoreria/depositos/{id}/confirmar` y `/rechazar`, con la guarda P6 del grupo. Los catálogos nuevos de conceptos y retenciones y la captura de saldo inicial de una cuenta maestra conservan sus permisos de administración, por alcance de empresa. Los reportes históricos de P8 usan su permiso corporativo específico; no se sustituyó por el de facturas ni se duplicó su lector.

## Seguimiento P6b · rutas de P4 (09-oct-2026)

Cotejo completo de `CuentasPorPagar/*` y `Tesoreria/*` en la rama `fix/P6b-sucursal-rutas-p4`, desde `main` local `ea7bce2` (PR #68 y #69 integrados). Se encontraron **37 rutas literales ausentes de las tablas previas**: 7 de anticipos/NC/cargos y 30 de catálogos, cuentas y viáticos ya exceptuados por la regla P6. Estar ausente de la tabla no significa carecer de autorización. No se modifican reglas, estados, importes, validaciones fiscales ni publicación de eventos de P4.

### Rutas nuevas incorporadas al inventario

| Método | Ruta | Control y decisión P6b |
|---|---|---|
| POST | `/api/v1/cuentas-por-pagar/anticipos/{id:guid}/cancelar` | Permiso de captura primero; guarda del grupo `anticipo_proveedor`, con `cuentas_por_pagar.documentos.gestionar-todas-sucursales`. [Fuente](../../backend/src/Api/Endpoints/CuentasPorPagar/AnticiposEndpoints.cs#L78) |
| GET | `/api/v1/cuentas-por-pagar/anticipos/serie/{proveedorId:guid}` | Configuración por proveedor y empresa, sin sucursal. GET exige `cuentas_por_pagar.anticipos.leer`; PUT exige `cuentas_por_pagar.anticipos.capturar`. El filtro del grupo busca `id`, no `proveedorId`. [Fuente](../../backend/src/Api/Endpoints/CuentasPorPagar/AnticiposEndpoints.cs#L89) |
| PUT | `/api/v1/cuentas-por-pagar/anticipos/serie/{proveedorId:guid}` | Configuración por proveedor y empresa, sin sucursal. GET exige `cuentas_por_pagar.anticipos.leer`; PUT exige `cuentas_por_pagar.anticipos.capturar`. El filtro del grupo busca `id`, no `proveedorId`. [Fuente](../../backend/src/Api/Endpoints/CuentasPorPagar/AnticiposEndpoints.cs#L92) |
| POST | `/api/v1/cuentas-por-pagar/anticipos/{id:guid}/amortizar-nc` | Permiso de captura primero; guarda del grupo `anticipo_proveedor`, con `cuentas_por_pagar.documentos.gestionar-todas-sucursales`; comprueba también la NC del cuerpo. [Fuente](../../backend/src/Api/Endpoints/CuentasPorPagar/AnticiposEndpoints.cs#L96) |
| POST | `/api/v1/cuentas-por-pagar/notas-cargo/{id:guid}/cancelar` | Guarda de escritura del grupo heredada de P6; permiso de crear/capturar según el documento. [Fuente](../../backend/src/Api/Endpoints/CuentasPorPagar/NotasCargoEndpoints.cs#L156) |
| POST | `/api/v1/cuentas-por-pagar/notas-cargo/{id:guid}/formalizar` | Guarda de escritura `nota_cargo` heredada de P6; se agrega verificación de la NC fiscal del cuerpo. [Fuente](../../backend/src/Api/Endpoints/CuentasPorPagar/NotasCargoEndpoints.cs#L165) |
| POST | `/api/v1/cuentas-por-pagar/notas-credito/{id:guid}/cancelar` | Guarda de escritura del grupo heredada de P6; permiso de crear/capturar según el documento. [Fuente](../../backend/src/Api/Endpoints/CuentasPorPagar/NotasCreditoEndpoints.cs#L132) |

### Rutas existentes revisadas y conservadas

- `POST /notas-cargo/{id}/aplicar`: ya tenía guarda del cargo. Se agrega la guarda de la factura elegida en el cuerpo o de la factura persistida si el cuerpo la omite, con `cuentas_por_pagar.facturas.gestionar-todas-sucursales`.
- `POST /notas-credito/{id}/vincular-factura`: ya comprueba NC y factura; conserva permiso de captura y validaciones P4. Una NC en espera sin origen verificable solo puede vincularla el perfil corporativo. Una NC propia ya vinculada pasa la guarda pero conserva el 422 de estado; no se autoriza por la sucursal destino de un vínculo propuesto.
- `POST /facturas/{id}/aplicar-nc` y `/aplicar-anticipo`: ya comprueban factura y NC/anticipo. Se añaden regresiones para referencias secundarias ajenas.
- `GET /tesoreria/repp-pendientes`: ya usa `IDocumentoScopedQuery` de pasivo y filtra `DocumentosPermitidos` antes de totales/paginación. `POST /tesoreria/repp-recibidos` ya valida la factura; P6b agrega la validación de cada `PagoId` incorporado por P4 antes del handler fiscal.
- Retiro de pasivo: **no existe ruta HTTP nueva**. `RetirarPasivoDePagoCommand` consume el evento de CxP en el worker de Tesorería. Las entradas HTTP existentes (`POST /facturas/{id}/enviar-revision` y `POST /tesoreria/pasivos-pendientes/{facturaProveedorId}/solicitar-cancelacion`) ya tienen guarda P6. No se añade autorización de usuario a un consumidor interno de eventos.

El lector `CxpSucursalReadAdapter` incorpora el origen persistido de P4: NC tipo 07 → `AnticipoOrigenId` → OC → sucursal. El CFDI asociado hereda el mismo alcance. No resuelve un origen por UUID/proveedor ni por usuario. Anticipo sin OC, NC sin factura/anticipo vinculado o con origen no verificable sigue siendo solo corporativo.

### Otras ausencias de la tabla, sin cambios de alcance

Se enumeran para que el cotejo de ambas carpetas sea completo. Conservan las excepciones ya documentadas en «Documentos sin origen verificable y catálogos».

| Método | Ruta | Motivo de conservar su alcance |
|---|---|---|
| GET | `/api/v1/cuentas-por-pagar/catalogos/aprobadores-limites` | Catálogo o cuenta maestra por empresa; exige su permiso de operación. [Fuente](../../backend/src/Api/Endpoints/CuentasPorPagar/CatalogosCxpAdminEndpoints.cs#L32) |
| POST | `/api/v1/cuentas-por-pagar/catalogos/aprobadores-limites` | Catálogo o cuenta maestra por empresa; exige su permiso de operación. [Fuente](../../backend/src/Api/Endpoints/CuentasPorPagar/CatalogosCxpAdminEndpoints.cs#L51) |
| PATCH | `/api/v1/cuentas-por-pagar/catalogos/aprobadores-limites/{id:guid}` | Catálogo o cuenta maestra por empresa; exige su permiso de operación. [Fuente](../../backend/src/Api/Endpoints/CuentasPorPagar/CatalogosCxpAdminEndpoints.cs#L65) |
| POST | `/api/v1/cuentas-por-pagar/catalogos/aprobadores-limites/{id:guid}/cerrar` | Catálogo o cuenta maestra por empresa; exige su permiso de operación. [Fuente](../../backend/src/Api/Endpoints/CuentasPorPagar/CatalogosCxpAdminEndpoints.cs#L84) |
| GET | `/api/v1/cuentas-por-pagar/catalogos/politicas-viaticos` | Catálogo o cuenta maestra por empresa; exige su permiso de operación. [Fuente](../../backend/src/Api/Endpoints/CuentasPorPagar/CatalogosCxpAdminEndpoints.cs#L107) |
| POST | `/api/v1/cuentas-por-pagar/catalogos/politicas-viaticos` | Catálogo o cuenta maestra por empresa; exige su permiso de operación. [Fuente](../../backend/src/Api/Endpoints/CuentasPorPagar/CatalogosCxpAdminEndpoints.cs#L124) |
| PATCH | `/api/v1/cuentas-por-pagar/catalogos/politicas-viaticos/{id:guid}` | Catálogo o cuenta maestra por empresa; exige su permiso de operación. [Fuente](../../backend/src/Api/Endpoints/CuentasPorPagar/CatalogosCxpAdminEndpoints.cs#L138) |
| GET | `/api/v1/cuentas-por-pagar/catalogos/retenciones` | Catálogo o cuenta maestra por empresa; exige su permiso de operación. [Fuente](../../backend/src/Api/Endpoints/CuentasPorPagar/CatalogosCxpAdminEndpoints.cs#L167) |
| GET | `/api/v1/cuentas-por-pagar/catalogos/retenciones/propuesta` | Catálogo o cuenta maestra por empresa; exige su permiso de operación. [Fuente](../../backend/src/Api/Endpoints/CuentasPorPagar/CatalogosCxpAdminEndpoints.cs#L170) |
| POST | `/api/v1/cuentas-por-pagar/catalogos/retenciones` | Catálogo o cuenta maestra por empresa; exige su permiso de operación. [Fuente](../../backend/src/Api/Endpoints/CuentasPorPagar/CatalogosCxpAdminEndpoints.cs#L173) |
| PUT | `/api/v1/cuentas-por-pagar/catalogos/retenciones/{id:guid}` | Catálogo o cuenta maestra por empresa; exige su permiso de operación. [Fuente](../../backend/src/Api/Endpoints/CuentasPorPagar/CatalogosCxpAdminEndpoints.cs#L180) |
| GET | `/api/v1/cuentas-por-pagar/catalogos/motivos-revision` | Catálogo o cuenta maestra por empresa; exige su permiso de operación. [Fuente](../../backend/src/Api/Endpoints/CuentasPorPagar/CatalogosCxpEndpoints.cs#L24) |
| GET | `/api/v1/cuentas-por-pagar/viaticos` | Solicitud por empleado/jefe y empresa; excepción P6 vigente. [Fuente](../../backend/src/Api/Endpoints/CuentasPorPagar/ViaticosEndpoints.cs#L32) |
| POST | `/api/v1/cuentas-por-pagar/viaticos` | Solicitud por empleado/jefe y empresa; excepción P6 vigente. [Fuente](../../backend/src/Api/Endpoints/CuentasPorPagar/ViaticosEndpoints.cs#L50) |
| POST | `/api/v1/cuentas-por-pagar/viaticos/{id:guid}/autorizar-jefe` | Solicitud por empleado/jefe y empresa; excepción P6 vigente. [Fuente](../../backend/src/Api/Endpoints/CuentasPorPagar/ViaticosEndpoints.cs#L67) |
| POST | `/api/v1/cuentas-por-pagar/viaticos/{id:guid}/autorizar-df` | Solicitud por empleado/jefe y empresa; excepción P6 vigente. [Fuente](../../backend/src/Api/Endpoints/CuentasPorPagar/ViaticosEndpoints.cs#L89) |
| POST | `/api/v1/cuentas-por-pagar/viaticos/{id:guid}/marcar-pagado` | Solicitud por empleado/jefe y empresa; excepción P6 vigente. [Fuente](../../backend/src/Api/Endpoints/CuentasPorPagar/ViaticosEndpoints.cs#L111) |
| POST | `/api/v1/cuentas-por-pagar/viaticos/{id:guid}/capturar-comprobacion` | Solicitud por empleado/jefe y empresa; excepción P6 vigente. [Fuente](../../backend/src/Api/Endpoints/CuentasPorPagar/ViaticosEndpoints.cs#L128) |
| POST | `/api/v1/cuentas-por-pagar/viaticos/{id:guid}/liberar` | Solicitud por empleado/jefe y empresa; excepción P6 vigente. [Fuente](../../backend/src/Api/Endpoints/CuentasPorPagar/ViaticosEndpoints.cs#L149) |
| POST | `/api/v1/cuentas-por-pagar/viaticos/{id:guid}/rechazar` | Solicitud por empleado/jefe y empresa; excepción P6 vigente. [Fuente](../../backend/src/Api/Endpoints/CuentasPorPagar/ViaticosEndpoints.cs#L170) |
| GET | `/api/v1/cuentas-por-pagar/viaticos/{id:guid}` | Solicitud por empleado/jefe y empresa; excepción P6 vigente. [Fuente](../../backend/src/Api/Endpoints/CuentasPorPagar/ViaticosEndpoints.cs#L191) |
| GET | `/api/v1/tesoreria/conceptos` | Catálogo o cuenta maestra por empresa; exige su permiso de operación. [Fuente](../../backend/src/Api/Endpoints/Tesoreria/ConceptosEndpoints.cs#L16) |
| POST | `/api/v1/tesoreria/conceptos` | Catálogo o cuenta maestra por empresa; exige su permiso de operación. [Fuente](../../backend/src/Api/Endpoints/Tesoreria/ConceptosEndpoints.cs#L19) |
| PUT | `/api/v1/tesoreria/conceptos/{id:guid}` | Catálogo o cuenta maestra por empresa; exige su permiso de operación. [Fuente](../../backend/src/Api/Endpoints/Tesoreria/ConceptosEndpoints.cs#L23) |
| GET | `/api/v1/tesoreria/cuentas` | Catálogo o cuenta maestra por empresa; exige su permiso de operación. [Fuente](../../backend/src/Api/Endpoints/Tesoreria/CuentasEndpoints.cs#L31) |
| POST | `/api/v1/tesoreria/cuentas` | Catálogo o cuenta maestra por empresa; exige su permiso de operación. [Fuente](../../backend/src/Api/Endpoints/Tesoreria/CuentasEndpoints.cs#L44) |
| PUT | `/api/v1/tesoreria/cuentas/{id:guid}` | Catálogo o cuenta maestra por empresa; exige su permiso de operación. [Fuente](../../backend/src/Api/Endpoints/Tesoreria/CuentasEndpoints.cs#L60) |
| POST | `/api/v1/tesoreria/cuentas/{id:guid}/activar` | Catálogo o cuenta maestra por empresa; exige su permiso de operación. [Fuente](../../backend/src/Api/Endpoints/Tesoreria/CuentasEndpoints.cs#L85) |
| POST | `/api/v1/tesoreria/cuentas/{id:guid}/desactivar` | Catálogo o cuenta maestra por empresa; exige su permiso de operación. [Fuente](../../backend/src/Api/Endpoints/Tesoreria/CuentasEndpoints.cs#L97) |
| POST | `/api/v1/tesoreria/cuentas/{id:guid}/saldo-inicial` | Catálogo o cuenta maestra por empresa; exige su permiso de operación. [Fuente](../../backend/src/Api/Endpoints/Tesoreria/CuentasEndpoints.cs#L109) |

### Verificación P6b

`P6bSucursalEndpointsTests.cs` amplía la misma clase/fixture de P6: periodo abierto, usuario operativo y super-admin, documentos del seed, cuerpos/versiones válidos, 403 sin mutación, operaciones propias/corporativas, referencias secundarias, permiso antes de alcance, configuración de serie y REPP con XML fiscal ficticio y Blob simulado. La serie usa un proveedor simulado exclusivo y limpia su configuración. También se limpian las NC adicionales, REPP y Outbox por IDs de cada ejecución.

`SucursalNcP6bTests` agrega cinco casos unitarios del lector: NC 07 vinculada con/sin OC, NC 07 sin vínculo y NC con/sin factura. Las sucursales de prueba P6 son `MID`/`MTY`, no usuarios ni registros reales de Cancún/Circuito. La reproducción territorial con esos usuarios reales permanece **Por confirmar**.

PostgreSQL desechable y rojo/verde de integración: **Por confirmar**; este sandbox no tiene Docker. Claude debe ejecutar `tools/validate-integration-isolated.sh` completo, incluidos los casos P6/P6b. No se afirma aceptación ni despliegue.

Resultados locales P6b (09-oct-2026):

| Comprobación | Resultado verificado |
|---|---|
| `dotnet build Millet.sln` (MSBuild secuencial, restauración desde caché NuGet local, `NuGetAudit=false`) | 0 errores, 0 advertencias; incluye compilación de las integraciones nuevas |
| CxP unitarias | 444/444, incluidos los 5 casos nuevos del lector |
| Tesorería unitarias | 132/132 |
| `SucursalScopeP6Tests` | 3/3 |
| `npx tsc --noEmit -p tsconfig.json` | Código de salida 0 |
| `npm run -s typecheck:test` | Código de salida 0 |
| `npm run -s lint` | 0 errores, 10 advertencias existentes; código de salida 0 |
| `npx vitest run` | 363 archivos, 2,084 pruebas en verde; código de salida 0 |
| `git diff --check` | Sin errores |

VSTest abortó antes de ejecutar las unitarias con `SocketException (13): Permission denied`: el sandbox bloquea su socket local. Los resultados unitarios de la tabla se obtuvieron ejecutando **xUnit en proceso**, mediante `AssemblyRunner.WithoutAppDomain` del paquete ya restaurado `xunit.runner.visualstudio`, sobre las DLL compiladas. El ejecutor temporal está en `/tmp/p6b-runner`; no modifica el runner del repositorio. No hubo pruebas fallidas ni omitidas en esas ejecuciones. Esto no sustituye el verde PostgreSQL requerido.

La base Obsidian está fuera del alcance de escritura de este sandbox. Esta sección conserva el cierre local y su evidencia para trasladar a la ficha/Bitácora pertinente cuando se valide la integración. No se modificaron tareas externas, no se publicaron documentos y no se hizo commit ni push.


## Adenda P6b · cobertura de P7 sobre `b8cceaa`

La continuación comenzó con el worktree limpio: P4 ya estaba en `a40b134` y P7 (#70, `be819ad`) integrado por `b8cceaa`. No se revirtió ni rehízo P4. El diff de `a40b134` a esta base no cambia rutas CxP/Tesorería: siguen vigentes las 37 ausencias y las decisiones de la sección P4, incluida la serie por proveedor (permiso de operación propio, configuración sin sucursal). El cotejo actual resuelve 133 rutas literales CxP/Tesorería: 37 ausentes de las tablas anteriores a P6b y 0 sin fila en el inventario actualizado. Se normalizan nombres de parámetros y la barra final; las rutas genéricas de adjuntos siguen documentadas aparte.

P7 no añadió una ruta HTTP literal: modificó el PATCH de obra de RQ, los filtros de avisos de salidas y el alcance del árbol, además de handlers y DTOs de recepción/salida/reorden. El cotejo de esas capacidades encontró **14 rutas existentes de Almacén ausentes del inventario P6**, sin guarda/filtro territorial; el árbol ya estaba inventariado pero omitía el tipo `Recepcion`. Se incorporan las 14 y se completa la guarda del árbol. Las demás carpetas de Almacén no forman parte de esta revisión acotada a P7; no se declara aislamiento de todo el módulo.

### Rutas incorporadas/revisadas

| Método | Ruta | Control P6b/P7 |
|---|---|---|
| GET | `/api/v1/almacen/recepciones/` | Permiso de entradas; `IDocumentoScopedQuery` filtra IDs antes de conteo/paginación. |
| GET | `/api/v1/almacen/recepciones/{id:guid}` | Permiso de entradas; guarda del movimiento persistido. |
| POST | `/api/v1/almacen/recepciones/` | Permiso de registrar entradas; guarda OC, cada bin y CFDI del cuerpo si se proporciona. |
| POST | `/api/v1/almacen/recepciones/packing-list` | Permiso de registrar entradas; guarda OC y cada bin antes del handler P7. |
| GET | `/api/v1/almacen/salidas/` | Permiso de salidas; filtro de IDs antes de conteo/paginación y avisos `soloVencidos`/`soloPorVencer`. |
| GET | `/api/v1/almacen/salidas/{id:guid}` | Permiso de salidas; guarda del movimiento persistido. |
| POST | `/api/v1/almacen/salidas/` | Permiso de registrar salidas; guarda RQ y cada bin del cuerpo. |
| POST | `/api/v1/almacen/salidas/vale` | Permiso de vale; guarda cada bin del cuerpo. |
| POST | `/api/v1/almacen/salidas/{id:guid}/regularizar` | Permiso de vale; guarda movimiento y RQ regularizadora antes del handler. |
| GET | `/api/v1/almacen/reorden/` | Permiso de lectura de reorden; filtro de IDs antes de conteo/paginación. |
| GET | `/api/v1/almacen/reorden/{id:guid}` | Permiso de lectura de reorden; guarda N1 por sucursal, N2 por almacén persistido. |
| POST | `/api/v1/almacen/reorden/` | Permiso de administrar reorden; guarda entidad N1/N2 del cuerpo. |
| PATCH | `/api/v1/almacen/reorden/{id:guid}` | Permiso de administrar reorden; guarda entidad persistida, conserva la llave inmutable. |
| POST | `/api/v1/almacen/reorden/{id:guid}/desactivar` | Permiso de administrar reorden; guarda entidad persistida. |
| GET | `/api/v1/compras/trazabilidad/arbol-documentos` | Policy `compras.ordenes.leer` primero; verifica raíz y todos los ascendentes/descendentes RQ/OC/recepción/factura/pago antes de responder. Árbol mixto → 403 completo, igual que P6. |
| PATCH | `/api/v1/compras/requisiciones/{id:guid}` | Obra conserva permiso y guarda de escritura de RQ existentes; se agrega regresión con cambio persistido. |

Fuentes: `RecepcionesEndpoints.cs`, `SalidasEndpoints.cs`, `ValesEndpoints.cs`, `AlmacenReordenEndpoints.cs`, `ArbolDocumentosEndpoint.cs` y los tres handlers de listado modificados.

### Origen territorial y permisos

`AlmacenSucursalReadAdapter` implementa el puerto público `IAlmacenSucursalReadPort`, reutilizado por `DocumentoSucursalScope` y `SucursalScopeQueryBehavior`. Para movimientos, resuelve **todas** las ubicaciones → sub-almacén → almacén → sucursal y reúne las sucursales de OC, RQ, RQ regularizadora, factura y CFDI vinculados mediante puertos públicos de lectura. Si no hay líneas, o falta un bin u origen persistido no se infiere alcance: documento sin origen verificable → solo corporativo. No basta un bin propio si otro origen es ajeno. El UUID capturado manualmente no se usa para inventar una sucursal.

Reorden sí tiene destino territorial: N1 referencia sucursal; N2 referencia almacén. La cantidad fija de P7 conserva su cálculo. El motor `EvaluarReordenQuery`/`GenerarBorradoresReordenCommand` solo tiene consumidores internos (no ruta HTTP) y conserva el barrido del worker sin JWT. Apartados se manipulan desde autorización/cancelación/cierre de RQ y surtido: las rutas RQ/OC mantienen P6, y las entradas de surtido ahora verifican RQ y bins. La conversión de unidades no tiene una ruta nueva: sigue dentro de los handlers P7, después del control de acceso.

Se agregan seis permisos canónicos y una migración de Identidad (sin tablas nuevas): `almacen.{entradas,salidas,reorden}.{leer,gestionar}-todas-sucursales`. El permiso histórico `almacen.salidas.leer-todas` sigue siendo la capacidad de lectura; **no** es el bypass territorial. Lectura corporativa no concede gestión corporativa. La migración siembra permisos, no los asigna a roles operativos; el super-admin sigue el bootstrap existente.

### Pruebas nuevas y límites

`P6bP7SucursalEndpointsTests.cs` reutiliza la clase y fixture P6 con periodo abierto también para Almacén. Cubre cada detalle/alta/mutación (403 ajeno sin cambios y 2xx propio/corporativo), permisos antes de sucursal, bandejas/avisos, los cinco orígenes del árbol con un nodo relacionado ajeno, alta con bin/CFDI ajeno, recepción sin líneas y obra de RQ. Usa sucursales ficticias del seed `MID`/`MTY`; no representa ejecución con los usuarios reales de Cancún/Circuito. Los catálogos exclusivos, movimientos, saldos, asignaciones, configuraciones y Outbox se limpian por IDs. Ningún catálogo compartido queda alterado tras la prueba.

`SucursalP6bTests` agrega 10 casos unitarios: bin y origen propio/ajeno/roto, movimientos sin líneas, destino N2, factura/CFDI ajenos y filtro antes de totales/paginación para recepción/salida/reorden. Los demás controles/validaciones/eventos y la lógica de negocio de P4/P7 se conservan.

Integración PostgreSQL desechable y rojo/verde completo: **Por confirmar**. El sandbox bloquea el socket Docker y VSTest; las integraciones se dejan escritas y compiladas para que Claude ejecute `tools/validate-integration-isolated.sh` completo. No equivale a aceptación Millet.

### Resultados locales de la adenda P7 · 09-oct-2026

Evidencia de esta ejecución: [resumen](P6b-P7-resumen.md) y [logs](evidencia-p6b-p7/archivos-cambiados.txt).

| Verificación | Resultado |
|---|---|
| Build completo de `Millet.sln` con `--no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false -p:NuGetAudit=false` | 0 errores, 0 advertencias; integra las pruebas P6/P6b/P7 y la migración nueva. |
| Almacén / Compras / CxP / Tesorería unitarias | 326 / 565 / 444 / 132 verdes. |
| API / Identidad / Compartido unitarias | 40 / 113 / 100 verdes. |
| Total unitarias ejecutadas | 1,720; 0 fallidas, 0 omitidas. |
| `npx tsc --noEmit -p tsconfig.json` y `npm run -s typecheck:test` | Ambos código de salida 0. |
| `npm run -s lint` | 0 errores, 10 advertencias existentes; salida 0. |
| `npx vitest run` | 364 archivos, 2,085 pruebas verdes; salida 0. |
| Modelo de Identidad respecto de la última migración | Sin cambios pendientes (`has-pending-model-changes`). |
| Cotejo literal CxP/Tesorería | 133 rutas; 0 ausencias en inventario actualizado. |
| `git diff --check` | Sin errores. |

Las unitarias se ejecutaron con xUnit en proceso (`XunitFrontController`, sin AppDomain), cargando dependencias y bibliotecas nativas desde cada suite. El ejecutor temporal se conserva como fuente en la evidencia; no se cambia el runner del repositorio. El primer intento con el runner anterior no resolvía las DLL de Compras/Almacén ni la biblioteca nativa de PDF; el ejecutor corregido pasó las suites completas. VSTest se anuló antes de ejecutar pruebas por `SocketException (13): Permission denied`. El descubrimiento de integración cargó las pruebas nuevas, **sin ejecutarlas ni abrir una base** (las teorías con `MemberData` quedan para ejecución).

No se hizo commit ni push en esta continuación. La bóveda Obsidian está fuera del alcance de escritura; el resumen local incluye el texto pendiente de trasladar a su ficha/Bitácora cuando Claude verifique PostgreSQL.
