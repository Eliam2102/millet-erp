# Auditoría técnica de las 104 funcionalidades parciales o no existentes

## Propósito y límite

Este documento complementa [10-auditoria-35-existentes.md](10-auditoria-35-existentes.md). Las 35 filas declaradas como existentes se auditan allí; este documento revisa las 104 restantes.

La clasificación se basa en archivos exactos de dominio/aplicación, rutas/pantallas y pruebas localizadas. El gate global de la línea base compiló y pasó 2,768 pruebas backend y 1,567 frontend. Eso demuestra que los candidatos compilan y que las pruebas localizadas participaron en el gate, pero **no equivale a ejecutar individualmente los 104 recorridos ni a UAT**.

Estados usados:

- **Núcleo directo localizado:** existen componentes específicos en código, pantalla y/o pruebas; todavía debe ejecutarse el criterio completo.
- **Parcial real:** existe una parte relevante, pero faltan reglas, integración, cobertura o recorrido integral descrito por la matriz.
- **Pieza vecina solamente:** hay infraestructura reutilizable, pero no la función pedida.
- **No localizado:** no se encontró implementación dedicada en este corte; requiere construcción o una decisión documentada.

## Corte 1 · Administración y usuarios

| ID | Evidencia exacta | Pantalla/ruta | Pruebas directas | Resultado auditado | Falta para cerrar |
|---|---|---|---|---|---|
| F1-ADM-02 | `backend/src/Identidad/Application/Usuarios`; comandos de roles y usuarios | `frontend/src/routes/_app/admin/usuarios`; `admin/roles` | `backend/tests/Api.IntegrationTests/Identidad/UsuariosEndpointsTests.cs`; `RolesEndpointsTests.cs` | Núcleo directo localizado; **parcial real** | Matriz final por empresa/acción, Entra ID real y prueba negativa API/UI |
| F1-ADM-04 | `backend/src/Catalogos`; catálogos compartidos en `Compartido` | `frontend/src/routes/_app/admin/catalogos/*` | `backend/tests/Catalogos.UnitTests`; `backend/tests/Api.IntegrationTests/Catalogos` | Núcleo directo localizado; **parcial real** | Catálogo canónico, vigencias, responsables y regresión en consumidores |
| F1-ADM-05 | `backend/src/DatosMaestros/Domain/Proveedor.cs`; persistencia compartida de proveedores | `frontend/src/routes/_app/admin/datos-maestros/proveedores` | `backend/tests/SharedKernel.UnitTests/Domain/ProveedorTests.cs`; `ProveedorDetalle.smoke.test.tsx` | Núcleo directo localizado; **parcial real** | Duplicados, vigencia fiscal, cambio bancario auditado y maestro real Millet |
| F1-ADM-06 | `backend/src/DatosMaestros/Domain/Cliente.cs`; `Integraciones.Aw/Infrastructure/Pedidos/AwMasterProvisioningAdapter.cs` | `frontend/src/routes/_app/admin/datos-maestros/clientes` | `AwMasterProvisioningAdapterTests.cs`; `ClienteDetalle.smoke.test.tsx` | Núcleo directo localizado; **parcial condicionado** | Contrato y muestra A+W, prioridad de origen, conflicto y conciliación real |
| F1-ADM-07 | `backend/src/DatosMaestros/Domain/ProductoAw.cs`; lectores/maestros de A+W | `frontend/src/routes/_app/admin/datos-maestros/productos-aw` | `ProductoAwTests.cs`; `ProductoAwDatosForm.idempotency.test.tsx`; smoke de detalle | Núcleo directo localizado; **parcial condicionado** | Mapeo A+W–ERP–SAT, variantes, unidades, bajas y muestra real |
| F1-ADM-08 | `backend/src/CentrosCosto`; catálogo y jerarquía | `frontend/src/routes/_app/centros-costo` y `centros-costo/configuracion` | `backend/tests/Api.IntegrationTests/CentrosCosto/JerarquiaTests.cs`; listas/búsqueda | Núcleo directo localizado; **parcial real** | Jerarquía definitiva, vigencias, empresa y consumo obligatorio en módulos |
| F1-ADM-09 | `backend/src/Integraciones.Fiscal/Domain/ConfiguracionPac.cs`; resolver y cifrado | `frontend/src/routes/_app/admin/integraciones/fiscal/index.tsx` | `ConfiguracionPacTests.cs`; `GuardarConfiguracionPacHandlerTests.cs`; `ConfiguracionPacForm.test.tsx` | Núcleo directo localizado; **parcial condicionado** | Parámetros, series, certificados y prueba sandbox por empresa; secretos por canal seguro |
| F1-ADM-12 | `CurrentEmpresaContext.cs`; `EmpresaContextSaveChangesInterceptor.cs`; base contexts con empresa | Comportamiento transversal, no una sola pantalla | `CurrentEmpresaContextTests.cs`; pruebas de persistencia/empresa en integración | Infraestructura directa localizada; **parcial transversal** | Probar aislamiento completo en los módulos de Ola 1A y acceso cruzado negativo |

Conclusión de Administración: las ocho filas no están vacías. Todas tienen base reutilizable, pero ninguna puede elevarse a completa sin los recorridos y dependencias indicados. ADM-09 depende de sandbox/credenciales; ADM-06/07 dependen del contrato A+W; ADM-12 necesita una prueba transversal, no sólo la existencia del interceptor.

## Corte 2 · Compras

Las seis filas marcadas como existentes se conservan en la auditoría de las 35. Aquí se revisan las siete parciales o no existentes.

| ID | Evidencia exacta | Pantalla/ruta | Pruebas directas | Resultado auditado | Falta para cerrar |
|---|---|---|---|---|---|
| F1-COM-06 | `OrdenCompra.cs`; `SubEstadoRecepcion.cs`; `SubEstadoFacturacion.cs`; `SubEstadoPago.cs`; recálculo | Bandeja y detalle de OC; `SubEstadosBar.tsx` | `OrdenCompraSubEstadosTests.cs`; `OrdenesCompraEndpointsTests.cs` | Núcleo directo localizado; **parcial real** | Recorrido recepción–factura–pago y cierre automático con eventos reales |
| F1-COM-07 | Compromiso de OC en `Requisicion.cs`; canal de entrega y puertos de recepción | Detalle de requisición/OC | `RequisicionCompromisoOcTests.cs`; pruebas `EntregaCanalEndpointsTests.cs` | **Pieza vecina solamente / parcial débil** | Incidencia de proveedor, compromiso por entrega parcial, reprogramación y evidencia |
| F1-COM-08 | `CancelarOrdenCompraCommand/Handler`; cancelación con recepciones y liberación de RQ | `AccionesOC.tsx`; `ModalMotivoOC.tsx`; hooks de cancelación | `CancelarEndpointsTests.cs`; `OrdenCompraCancelarConRecepcionesTests.cs` | Núcleo directo localizado; **parcial real** | Probar autorización, motivo, segunda firma cuando aplique y trazabilidad integral |
| F1-COM-09 | `IGenerarSolicitudCompraPort` es un contrato interno; no se localizó transmisión específica de compra MP hacia A+W | Sin ruta dedicada | Sin prueba directa localizada | **No localizado** | Diseñar contrato, autenticación, idempotencia, reintento, error y conciliación A+W |
| F1-COM-11 | No se localizó confirmación/acuse del proveedor como estado propio | Sin ruta dedicada | Sin prueba directa localizada | **No localizado** | Definir canal de acuse, usuario/fecha, rechazo, recordatorio y evidencia |
| F1-COM-12 | `ObtenerArbolDocumentosService.cs`; nodos/proveedores de trazabilidad | `frontend/src/routes/_app/compras/trazabilidad/oc/$id.tsx`; `TrazabilidadOc.tsx` | Cobertura indirecta en OC y cancelación; sin caso completo RQ–OC–recepción–factura–pago localizado | Núcleo directo localizado; **parcial condicionado** | Incorporar nodos faltantes de CxP/Tesorería y ejecutar cadena completa |
| F1-COM-13 | Partidas abiertas, pendientes de autorización e historial de compras por artículo | rutas `ordenes/partidas-abiertas`, `ordenes/pendientes-autorizacion`, `articulos/$id/historial-compras` | pruebas de bandejas, pendientes e historial/hooks | **Parcial real** | Métricas de cumplimiento por proveedor, saldos conciliados y criterio de corte |

Conclusión de Compras: COM-06 y COM-08 tienen implementación específica considerable; COM-12 y COM-13 tienen vistas útiles pero no acreditan el resultado integral; COM-07 sólo tiene piezas vecinas; COM-09 y COM-11 requieren construcción o una fuente adicional que demuestre código fuera del repositorio.

## Corte 3 · Almacén de insumos

Las siete filas marcadas como existentes se conservan en la auditoría de las 35. Aquí se revisan ocho parciales o no existentes.

| ID | Evidencia exacta | Pantalla/ruta | Pruebas directas | Resultado auditado | Falta para cerrar |
|---|---|---|---|---|---|
| F1-ALM-04 | Recepción parcial y diferencias se apoyan en comandos de recepción; no se localizó estado explícito de cuarentena | `RecepcionesPage.tsx`; `NuevaRecepcionSheet.tsx` | pruebas de recepción con factura/packing list y multi-subalmacén | **Parcial real** | Cuarentena, liberación/rechazo, motivo, responsable y recorrido de diferencia |
| F1-ALM-05 | Tolerancia por artículo en `IArticuloReadPort`; validación en ambos comandos de recepción | `NuevaRecepcionSheet.tsx`; schema de recepción | validadores y pruebas de recepción | Núcleo directo localizado; **parcial real** | Contrastar tolerancia independiente de cantidad con OC/factura real y errores por línea |
| F1-ALM-07 | `RegistrarSalidaPorValeCommand.cs`; worker de regularización SLA; blob de vale | salidas y detalle; captura/consulta de vale | `ValeValidatorsTests.cs`; pruebas de evento y salida | Núcleo directo localizado; **parcial real** | Requisición posterior, vencimiento/escalamiento y conciliación completa del vale |
| F1-ALM-11 | `PeriodoCerrado.cs`; `EjecutarCierreMensualCommand.cs`; configuración/persistencia | `frontend/src/routes/_app/almacen/cierre-mes.tsx` | `CierreMesPage.smoke.test.tsx`; pruebas de comandos consumidores de periodo | Núcleo directo localizado; **parcial real** | Regla definitiva de reapertura, autorización y prueba de bloqueo sobre todos los movimientos |
| F1-ALM-12 | No se localizó entidad, comando, ruta ni prueba de merma por etapa/motivo | Sin ruta dedicada | Sin prueba directa localizada | **No localizado** | Construir registro de merma, unidad, etapa, motivo, valor, autorización y póliza futura |
| F1-ALM-13 | Recepción toma costo de OC y existe handler de diferencia de precio; no se localizó motor integral de valuación/revaluación | Sin pantalla dedicada de valuación | Sin prueba directa de valuación integral localizada | **Pieza vecina solamente / parcial débil** | Método de valuación, capas/saldos, revaluación, efecto contable y conciliación |
| F1-ALM-14 | No se localizó contrato específico de sincronización de existencias/movimientos Almacén↔A+W | Sin ruta dedicada | Sin prueba de integración directa localizada | **No localizado** | Definir eventos, direcciones, campos, idempotencia, reintentos y conciliación A+W |
| F1-ALM-15 | Saldos, movimientos y reportes están implementados; reportes MP-CNK y Alfak tienen rutas | `almacen/saldos`; `almacen/reportes/*`; detalles de movimiento | `ReportesContratoTests.cs`; `ReportesEnriquecimientoTests.cs`; endpoints MP-CNK | Núcleo directo localizado; **parcial real** | Kardex consolidado, excepciones, filtros/cortes y conciliación contra saldo |

Conclusión de Almacén: ALM-05, ALM-07 y ALM-11 tienen núcleos directos; ALM-04 y ALM-15 son parciales reales; ALM-13 sólo tiene piezas de costo; ALM-12 y ALM-14 no se localizaron.

## Corte 4 · Materia prima

`F1-MP-04` permanece en la auditoría de las 35 existentes. Aquí se revisan las ocho filas parciales o no existentes.

| ID | Evidencia exacta | Pantalla/ruta | Pruebas directas | Resultado auditado | Falta para cerrar |
|---|---|---|---|---|---|
| F1-MP-01 | `Compartido/Domain/ProductoAw.cs`; maestros A+W; puertos de artículo de Almacén/Compras | `admin/datos-maestros/productos-aw` | `ProductoAwTests.cs`; `AwMasterProvisioningAdapterTests.cs`; smoke/idempotencia del detalle | Núcleo directo localizado; **parcial condicionado** | Clasificación de materia prima, variantes, equivalencias, unidades y gobierno real de claves A+W |
| F1-MP-02 | `Integraciones.Aw` contiene lectores de pedidos/solicitudes, pero no un contrato dedicado de necesidades de materia prima | Sin ruta dedicada | Sin prueba directa de necesidades de producción localizada | **No localizado** | Contrato vigente A+W, campos, frecuencia, idempotencia, errores y conciliación de necesidades |
| F1-MP-03 | Compras soporta requisiciones/OC y saldos no surtidos; no se localizó conversión desde una necesidad A+W | Pantallas generales de requisiciones y órdenes | Pruebas de requisición, OC y saldo no surtido; sin caso A+W→RQ→OC | **Pieza vecina solamente / parcial débil** | Regla de agrupación, aprobación, proveedor, cantidades y trazabilidad desde la necesidad original |
| F1-MP-05 | `SaldoInventario`, `Ubicacion`, `AsignacionArticuloUbicacion` y movimientos por ubicación | `almacen/saldos`, `almacen/saldos-jerarquia`, `almacen/ubicaciones` | `ListarSaldosHandlerTests.cs`; `UbicacionAggregateTests.cs`; smoke de saldos/ubicaciones | Núcleo directo localizado; **parcial real** | Lote como entidad, reserva vigente por planta/orden A+W y disponibilidad comprometida; una migración posterior elimina las reservas anteriores |
| F1-MP-06 | Recepción conserva costo de OC y existe diferencia de precio; no se localizó valuación/revaluación de remanente | Sin ruta dedicada de valuación | Sin prueba integral de capas y revaluación localizada | **Pieza vecina solamente / parcial débil** | Método de valuación, saldo/capas, regla de revaluación, diferencia y efecto contable |
| F1-MP-07 | `TipoMovimiento.SalidaConsumo` y salidas de Almacén; no se localizó recepción del consumo productivo desde A+W | Pantallas generales de salidas | `MovimientoInventarioTests.cs`; sin caso A+W→reserva→consumo | **Pieza vecina solamente / parcial débil** | Contrato A+W, reserva previa, descuento idempotente, reversa, error y conciliación |
| F1-MP-08 | No se localizó agregado/comando específico de merma por kilos, motivo y valor | Sin ruta dedicada | Sin prueba directa localizada | **No localizado** | Construir merma con unidad/peso, etapa, motivo, autorización, valor y salida contable futura |
| F1-MP-09 | Hay saldos/movimientos y árbol documental de Compras; no cubre en una consulta compra–lote–consumo–merma–saldo | Vistas separadas de movimientos/saldos y trazabilidad de OC | Pruebas parciales de saldos, movimientos y árbol de OC | **Pieza vecina solamente / parcial débil** | Identificador común, lote, nodos A+W/merma y consulta cronológica conciliada |

Conclusión de Materia prima: MP-01 y MP-05 tienen bases directas; MP-03, MP-06, MP-07 y MP-09 sólo pueden reutilizar piezas vecinas; MP-02 y MP-08 no se localizaron. No debe confundirse el lector actual de pedidos A+W con el contrato de necesidades y consumos de producción.

## Corte 5 · Cuentas por pagar

Las cuatro filas marcadas como existentes se conservan en la auditoría de las 35. Aquí se revisan nueve filas parciales o no existentes.

| ID | Evidencia exacta | Pantalla/ruta | Pruebas directas | Resultado auditado | Falta para cerrar |
|---|---|---|---|---|---|
| F1-CXP-01 | `CapturarFacturaConOcCommand/Handler`; `FacturaProveedor`; adaptadores de OC/recepción | `cxp/facturas` y detalle; `CapturarFacturaSheet.tsx` | `FacturaProveedorAggregateTests.cs`; `ComprasOcReadPortAdapterTests.cs`; pruebas del formulario | Núcleo directo localizado; **parcial real** | Validación uniforme de parciales y cantidad, recorrido con recepción real y evidencia de bloqueo/excepción |
| F1-CXP-02 | `Tolerancia.cs` y rechazo por tolerancia en el handler | Mismo flujo de captura de factura | `ToleranciaTests.cs`; `LineaOcPertenenciaGuardTests.cs` | Núcleo directo localizado; **parcial real** | La implementación compara importe total; falta tolerancia independiente de cantidad, excepción autorizada y no sobrescritura |
| F1-CXP-05 | `CfdiRecibido`, parser XML, descarga SAT, mailbox Graph/no-op y carga manual | `cxp/cfdis`; detalle, carga y descarga XML | pruebas de agregado/parser, `FiscalCfdiReceiverAdapterTests.cs`, mailbox y smoke de bandeja | Núcleo directo localizado; **parcial condicionado** | Credenciales/configuración real, validación de vigencia SAT, recorrido de descarga y asociación automática con factura/proveedor |
| F1-CXP-08 | Factura/nota conservan impuestos y pagos aplicados; no se localizó cuenta de IVA pendiente→pagado por abono | Detalles de factura/nota, sin control fiscal dedicado | Pruebas de pagos y notas; sin prueba de traslado fiscal por abono | **Pieza vecina solamente / parcial débil** | Cálculo proporcional por pago, saldos fiscales, póliza y conciliación contra REPP/pago |
| F1-CXP-09 | Facturas/comprobaciones capturan retenciones; no se localizó matriz fiscal ni cálculo completo de neto | Formularios/detalles de CxP | Pruebas de agregados con montos; sin matriz fiscal/retención por supuesto | **Pieza vecina solamente / parcial débil** | Reglas por proveedor/concepto, tasas, redondeos, desglose y monto neto verificable |
| F1-CXP-10 | No se localizaron consulta, layout, validaciones o reporte DIOT | Sin ruta dedicada | Sin prueba directa localizada | **No localizado** | Clasificación de terceros/operaciones, IVA acreditable/pagado, validaciones, exportación y conciliación |
| F1-CXP-11 | Consulta y pantalla de antigüedad de saldos; no hay cierre/reapertura de periodo de CxP ni conciliación con mayor | `cxp/reportes.antiguedad` | `BucketsAntiguedadTests.cs`; smoke de `ReporteAntiguedadSaldosPage` | Núcleo de reporte localizado; **parcial real** | Fecha de corte reproducible, cierre/reapertura autorizado, bloqueo transaccional y conciliación contable |
| F1-CXP-12 | Existe puerto `ITipoCambioReadPort`, pero el adaptador actual es `NoOpTipoCambioReadPort`; no se localizó revaluación de saldos | Sin ruta dedicada | Sin prueba directa de revaluación localizada | **Pieza vecina solamente / parcial débil** | Fuente de TC, valuación al corte, diferencia cambiaria, reversa y póliza |
| F1-CXP-13 | Hay eventos de pasivo/pago, pero no módulo contable que genere pólizas ni auxiliar de proveedores | Sin ruta dedicada | Sin prueba de póliza/auxiliar localizada | **No localizado** | Contrato contable, reglas de póliza, cuentas/dimensiones, auxiliar y conciliación |

Conclusión de Cuentas por pagar: CXP-01, CXP-02, CXP-05 y el reporte de CXP-11 tienen implementación específica; CXP-08, CXP-09 y CXP-12 conservan datos o contratos útiles pero no el resultado funcional; CXP-10 y CXP-13 no se localizaron. CXP-05 no puede considerarse operativo en canales reales hasta configurar y probar SAT/mailbox.

## Corte 6 · Comercio exterior

No existe un bounded context dedicado de Comercio Exterior. La evidencia reutilizable está dispersa entre Compras, CxP, Facturación y datos maestros.

| ID | Evidencia exacta | Pantalla/ruta | Pruebas directas | Resultado auditado | Falta para cerrar |
|---|---|---|---|---|---|
| F1-CE-01 | OC conserva `InformacionImportacion`; CxP tiene comprobación aduanal; Facturación tiene CCE | Pantallas separadas de OC, comprobaciones y factura | Pruebas separadas de importación, aduanales y CCE | **Pieza vecina solamente / parcial débil** | Expediente único, folio, estado, documentos, participantes, seguridad y navegación integral |
| F1-CE-02 | `InformacionImportacion` conserva semana de embarque, ruta y contenedor | Detalle de OC | `OrdenCompraLogisticaImportacionTests.cs` | **Pieza vecina solamente / parcial débil** | Hitos completos, fechas plan/real, responsables, alertas y entrega final |
| F1-CE-03 | No se localizó checklist documental ni motor de alertas propio de comercio exterior | Sin ruta dedicada | Sin prueba directa localizada | **No localizado** | Documentos obligatorios por operación, vigencia, responsable, alerta y excepción |
| F1-CE-04 | OC permite información de importación y pedimento; recepción admite packing list y adjuntos | Detalle de OC y recepción | `OrdenCompraLogisticaImportacionTests.cs`; pruebas de recepción con packing list | Núcleo directo localizado; **parcial real** | Modelo único y validaciones entre pedimento, contenedor, packing list, órdenes, facturas y recepciones |
| F1-CE-05 | No se localizó entidad/flujo OTR ni tratamiento completo de proveedor extranjero | Sin ruta dedicada | Sin prueba directa localizada | **No localizado** | Definir OTR actual de Millet, estados, moneda/incoterm, proveedor, autorización y relación con OC/CxP |
| F1-CE-06 | `CrearComprobacionAduanalesCommand` agrupa facturas por pedimento y exige doble autorización | `NuevaComprobacionAduanalesSheet.tsx` y detalle de comprobación | `ComprobacionGastosAduanalesTests.cs` | Núcleo directo localizado; **parcial condicionado** | Bases de distribución, costo puesto por material/lote, cierre, reapertura y conciliación contable |
| F1-CE-07 | `ComplementoCce`/líneas, comportamiento `ExportacionConCce`, captura de pedimento e interfaz de emisión | Emisión y detalle de factura de exportación | `CceTests.cs`; `PedimentoTests.cs`; pruebas del formulario/selectores CCE | Núcleo directo localizado; **parcial condicionado** | Catálogos/datos reales, integración PAC/SAT, XML timbrado CCE, cancelación y evidencia fiscal |

Conclusión de Comercio exterior: CE-04, CE-06 y CE-07 tienen núcleos reutilizables en otros módulos; CE-01 y CE-02 son piezas dispersas; CE-03 y CE-05 no se localizaron. La evidencia no acredita todavía un módulo integral de Comercio Exterior.

## Corte 7 · Facturación y fiscal

Las cinco filas marcadas como existentes se conservan en la auditoría de las 35. Aquí se revisan once filas parciales o no existentes.

| ID | Evidencia exacta | Pantalla/ruta | Pruebas directas | Resultado auditado | Falta para cerrar |
|---|---|---|---|---|---|
| F1-FAC-01 | `ProcesarSolicitudAwCommand/Handler`, `AwSolicitudesWorker`, `PedidoFacturable` e ingesta idempotente | Bandeja/detalle de pedidos y excepciones | pruebas de ingesta A+W, handlers y páginas de pedidos | Núcleo directo localizado; **parcial condicionado** | Contrato y estados reales A+W, elegibilidad, rechazo, write-back y prueba con muestras reales |
| F1-FAC-03 | No se localizó agregado, comando, PDF o estado de proforma previo al timbrado | Sin ruta dedicada | Sin prueba directa localizada | **No localizado** | Construir proforma versionada, autorización, conversión a factura y trazabilidad |
| F1-FAC-08 | `SolicitudCancelacion`, comandos/consulta y `CancelacionSatPollerWorker` | Acción y detalle de cancelación de comprobante | pruebas de cancelación, estados y adaptador fiscal | Núcleo directo localizado; **parcial condicionado** | Motivos definitivos, aceptación SAT real, sustitución, sincronización y efecto en CxC |
| F1-FAC-09 | Folio/serie en comprobantes y configuración fiscal; existe puerto de periodo contable | Configuración/operación de facturas, sin conciliador integral | pruebas de folios/series; sin cierre y conciliación completa | Núcleo directo localizado; **parcial real** | Cierre/reapertura, bloqueo por empresa, huecos/duplicados y conciliación contra PAC/SAT |
| F1-FAC-10 | `Integraciones.Fiscal` soporta descarga/consulta y estados; no se localizó una bandeja masiva completa de diferencias | Configuración fiscal y bandejas de comprobantes | pruebas de descarga/refresh/adaptadores fiscales | Núcleo técnico localizado; **parcial condicionado** | Consulta masiva real SAT, comparación, clasificación de diferencias, resolución y evidencia |
| F1-FAC-11 | `ISalidasPedimentosReader`, `PedimentoSalidasWorker` y aplicación de pedimento; el reader actual es stub | Detalle de factura/pedimento | `PedimentoTests.cs`; `PedimentoWorkerTests.cs` | **Pieza vecina solamente / parcial condicionado** | Fuente real de salida 71, entrega, parciales, vínculo por pieza/pedido y conciliación A+W |
| F1-FAC-12 | `ComportamientoFiscal`, datos fiscales, catálogos SAT y validaciones del emisor/receptor | Formulario de emisión | pruebas del builder, validadores y formulario | Núcleo directo localizado; **parcial real** | Matriz aprobada cliente/producto/operación, precedencia, excepciones y casos de frontera |
| F1-FAC-13 | Bitácora de intentos, comando de reintento y `TimbradoPendienteWorker` | Panel de intentos en detalle de comprobante | pruebas de reintento, worker y ejecutor de timbrado | Núcleo directo localizado; **parcial condicionado** | Límites/backoff definitivos, cola/alertas operativas, salud PAC visible y prueba de falla real |
| F1-FAC-14 | Adaptadores de clientes/productos y `IMasterProvisioningPort`; el aprovisionamiento/write-back tiene rutas stub/no-op | Selectores y datos maestros usados en emisión | pruebas de maestros e ingesta, sin reconciliación completa | **Pieza vecina solamente / parcial condicionado** | Comparación de campos fiscales A+W–ERP–SAT, conflicto, responsable y corrección comprobada |
| F1-FAC-15 | Existe `IContabilidadAsientoPort`, pero la infraestructura registra `NoOpContabilidadAsientoPort` | Sin ruta dedicada | Sin prueba de póliza contable real localizada | **No localizado** | Reglas de ingresos/impuestos/cancelación, cuentas, dimensiones, póliza, reversa y auxiliar |
| F1-FAC-16 | `ConfiguracionPac`, guardado cifrado y prueba de conexión por empresa | `admin/integraciones/fiscal` | `ConfiguracionPacTests.cs`; `GuardarConfiguracionPacHandlerTests.cs`; formulario | Núcleo directo localizado; **parcial condicionado** | Credenciales/certificados productivos por canal seguro y emisión/cancelación controlada por empresa |

Conclusión de Facturación y fiscal: hay una base amplia y específica. FAC-03 y FAC-15 no se localizaron; FAC-11 y FAC-14 dependen de adaptadores todavía incompletos; el resto requiere cierre funcional o validación real con A+W/PAC/SAT. Ningún stub acredita integración productiva.

## Corte 8 · Crédito y cobranza

Las cuatro filas marcadas como existentes se conservan en la auditoría de las 35. Aquí se revisan ocho filas parciales o no existentes.

| ID | Evidencia exacta | Pantalla/ruta | Pruebas directas | Resultado auditado | Falta para cerrar |
|---|---|---|---|---|---|
| F1-CXC-04 | `PropuestaAplicacionPago` calcula diferencia y tolerancia no fiscal | Nueva propuesta y detalle de aplicación | `PropuestaAplicacionTests.cs`; pruebas de hooks/páginas | Núcleo directo localizado; **parcial real** | Límite configurable, autorización, tratamiento de excedente y asiento de diferencia/comisión |
| F1-CXC-06 | `CreditoDisponibleQuery` y `DecidirLiberacionCommand`; el liberado no facturado A+W permanece provisional en cero | Tarjeta de crédito disponible y bandeja de liberaciones | `CreditoDisponibleQueryTests.cs`; `DecidirLiberacionHandlerTests.cs` | Núcleo directo localizado; **parcial condicionado** | Exposición A+W no facturada, contrato real, regla definitiva y decisión previa a producción |
| F1-CXC-07 | `AutorizacionCredito`, comandos, vigencia/consumo y UI de autorización | Bandeja de liberaciones y diálogo de autorización | `AutorizacionCreditoAggregateTests.cs`; pruebas de liberación/UI | Núcleo directo localizado; **parcial real** | Límites y doble autorización aprobados, caducidad operativa, segregación y evidencia |
| F1-CXC-08 | `DecisionLiberacion` registra el resultado, pero el propio dominio deja el write-back A+W como pendiente | Bandeja de liberaciones | pruebas de decisión; sin prueba de escritura/acuse A+W | **Pieza vecina solamente / parcial débil** | Evento/tabla puente real, confirmación, reintento, idempotencia y conciliación A+W |
| F1-CXC-09 | `EstadoCuentaClienteQuery` y pantalla exportable de estado de cuenta | `cxc/estado-cuenta` | `EstadoCuentaClienteTests.cs`; smoke de `EstadoCuentaPage` | Núcleo directo localizado; **parcial real** | Cortes/formato aprobados, documentos fiscales completos, totales de control y conciliación |
| F1-CXC-10 | Seguimientos/promesas, alertas y worker diario están implementados; la notificación externa no está acreditada | `cxc/cobranza` y `cxc/alertas` | `SeguimientoCobranzaTests.cs`; `AlertasCarteraTests.cs`; smoke de ambas páginas | Núcleo directo localizado; **parcial condicionado** | Reglas/responsables definitivos, correo real, escalamiento y seguimiento de promesa vencida |
| F1-CXC-11 | Reglas/buckets y series contemplan escenarios nacional/internacional, pero existen seeds provisionales | Cartera, línea de crédito y liberaciones | pruebas de antigüedad/líneas/liberación | **Pieza vecina solamente / parcial condicionado** | Catálogo vigente por cliente/país, precedencia, series A+W y excepciones validadas |
| F1-CXC-12 | No se localizó generación real de pólizas para cobro, aplicación o diferencia | Sin ruta dedicada | Sin prueba de póliza contable localizada | **No localizado** | Contrato contable, cuentas/dimensiones, pólizas, reversas y conciliación con Tesorería |

Conclusión de Crédito y cobranza: CXC-04, CXC-06, CXC-07, CXC-09 y CXC-10 tienen núcleos directos; CXC-08 y CXC-11 sólo están parcialmente conectadas; CXC-12 no se localizó. El cálculo de crédito sigue incompleto mientras A+W no aporte el liberado no facturado.

## Corte 9 · Producto terminado

Las nueve funcionalidades están declaradas como no existentes. No se localizó un bounded context de Producto Terminado; sólo existen piezas reutilizables de Almacén, Facturación y A+W.

| ID | Evidencia exacta | Pantalla/ruta | Pruebas directas | Resultado auditado | Falta para cerrar |
|---|---|---|---|---|---|
| F1-PT-01 | El lector A+W actual trabaja solicitudes/pedidos; no se localizó evento o escaneo de pieza terminada | Sin ruta dedicada | Sin prueba directa localizada | **No localizado** | Contrato/escaneo, identidad de pieza, pedido/posición, idempotencia, error y conciliación |
| F1-PT-02 | No se localizó regla o lector específico del estado A+W 69 | Sin ruta dedicada | Sin prueba directa localizada | **No localizado** | Fuente del estado, criterio de pedido completo, parciales, excepción y bloqueo |
| F1-PT-03 | Almacén tiene movimientos/entradas genéricas, pero no entrada por pieza terminada correlacionada con A+W | Sin ruta dedicada de PT | Pruebas generales de movimientos/entradas | **Pieza vecina solamente / parcial débil** | Agregado de pieza, almacén/subalmacén, costo, lote/serie, evento A+W y evidencia |
| F1-PT-04 | Existe subalmacén `Transitorio` y movimientos genéricos; no se localizó transferencia PT a tránsito | Sin ruta dedicada | Pruebas generales de subalmacén/movimiento | **Pieza vecina solamente / parcial débil** | Transferencia origen–tránsito, transporte, guía, responsable, estado y conciliación |
| F1-PT-05 | No se localizó confirmación específica de recepción de PT en sucursal destino | Sin ruta dedicada | Sin prueba directa localizada | **No localizado** | Recepción por pieza, diferencias, daño/faltante, usuario/fecha y actualización de existencia |
| F1-PT-06 | Hay devoluciones internas/proveedor de Almacén, no devolución/rechazo de producto terminado por pieza | Pantallas generales de devoluciones | Pruebas generales de devoluciones | **Pieza vecina solamente / parcial débil** | Daño/rechazo/devolución PT, evidencia, custodia, efecto en cartera/facturación y disposición |
| F1-PT-07 | Facturación conserva comprobantes; no se localizó entrega al cliente con evidencia de PT | Sin ruta dedicada | Sin prueba directa localizada | **No localizado** | Confirmación por pieza/pedido, receptor, fecha, firma/foto/documento y excepciones |
| F1-PT-08 | Facturación tiene puerto/worker de salidas-pedimentos, pero usa un reader stub y no se localizó salida A+W 71 | Sin ruta dedicada de PT | Pruebas de pedimento con stub; sin salida 71 real | **Pieza vecina solamente / parcial condicionado** | Contrato salida 71, correlación por pieza, descuento idempotente, reversa y conciliación |
| F1-PT-09 | Almacén publica `EntradaInventarioValoradaIntegrationEvent`; no existe módulo contable conectado para PT | Sin ruta dedicada | Sin prueba de valuación PT e interfaz contable localizada | **Pieza vecina solamente / parcial débil** | Método/costo por pieza, movimientos, cuentas/dimensiones, póliza, reversa y conciliación |

Conclusión de Producto terminado: PT-01, PT-02, PT-05 y PT-07 no se localizaron; PT-03, PT-04, PT-06, PT-08 y PT-09 sólo tienen infraestructura vecina. Este módulo requiere construcción significativa y contratos A+W explícitos.

## Corte 10 · Tesorería

Las cuatro filas marcadas como existentes se conservan en la auditoría de las 35. Aquí se revisan siete filas parciales o no existentes.

| ID | Evidencia exacta | Pantalla/ruta | Pruebas directas | Resultado auditado | Falta para cerrar |
|---|---|---|---|---|---|
| F1-TES-04 | `CorridaPago` y `CorridaPagoLinea` existen y persisten; no se localizaron comandos/endpoints operativos de corrida | Sólo tipos/placeholder; sin pantalla de corrida completa | Prueba de enum/placeholder; sin prueba de construcción/autorización de corrida | **Pieza vecina solamente / parcial débil** | Selección de pasivos, bloqueos, autorización, ejecución, rechazo/cancelación y trazabilidad |
| F1-TES-05 | No se localizó generador/importador de layout bancario ni procesamiento de respuesta | Sin ruta dedicada | Sin prueba directa localizada | **No localizado** | Banco/formato vigente, validación, cifrado/firma si aplica, envío, respuesta y conciliación |
| F1-TES-06 | Hay movimientos y auxiliar bancario, pero no motor de conciliación con extracto; la UI sólo anuncia el frente | Sin pantalla operativa de conciliación | Sin prueba de matching/conciliación bancaria localizada | **Pieza vecina solamente / parcial débil** | Importar extracto, parser por banco, matching, partidas abiertas, ajuste, cierre y evidencia |
| F1-TES-07 | Comandos/consulta de pagos a cuenta y UI para registrar/ligar | `tesoreria/pagos-cuenta` | `PagoACuentaDomainTests.cs`; pruebas de componentes/hook | Núcleo directo localizado; **parcial real** | Vencimiento/alerta, autorización de remanente, aplicación completa, periodo real y conciliación |
| F1-TES-09 | `PasivoPendientePago` conserva tipo de cambio; no se localizó cálculo de diferencia cambiaria ni asiento | Bandeja de pasivos/pagos, sin cálculo dedicado | Sin prueba de diferencia cambiaria localizada | **Pieza vecina solamente / parcial débil** | Fuente TC, regla fecha pago/corte, ganancia/pérdida, asiento y conciliación |
| F1-TES-10 | El pasivo proyectado conserva saldo neto y datos de pago; el desglose fiscal proviene de CxP y no está completo en Tesorería | Bandeja/registro de pagos | pruebas de proyección de pasivo y pago | Núcleo directo localizado; **parcial condicionado** | Retenciones/impuestos por concepto, total neto verificable y contrato definitivo CxP→Tesorería |
| F1-TES-11 | `FlujoEfectivoReporteQuery`, `AuxiliarBancosReporteQuery` y páginas exportables | `tesoreria/reportes/flujo-efectivo` y auxiliar bancos | `ReportesQueriesTests.cs`; pruebas del adaptador/páginas | Núcleo de reporte localizado; **parcial real** | Posición consolidada, saldos iniciales bancarios, pendientes completos y conciliación con extractos |

Conclusión de Tesorería: TES-07, TES-10 y TES-11 tienen núcleos reutilizables; TES-04, TES-06 y TES-09 son estructuras parciales; TES-05 no se localizó. El puerto de periodo contable sigue conectado a `NoOpPeriodoContablePort`, por lo que no acredita bloqueo real de periodos.

## Corte 11 · Contabilidad

Las trece funcionalidades están declaradas como no existentes. No se localizó un bounded context contable; los puertos de periodo/asiento observados en otros módulos están implementados como `NoOp`.

| ID | Evidencia exacta | Pantalla/ruta | Pruebas directas | Resultado auditado | Falta para cerrar |
|---|---|---|---|---|---|
| F1-CON-01 | No se localizó catálogo de cuentas contables por empresa | Sin ruta dedicada | Sin prueba directa localizada | **No localizado** | Modelo jerárquico, naturaleza, moneda, vigencia, cuenta control y gobierno por empresa |
| F1-CON-02 | Bounded context `CentrosCosto` con Dim1/Dim2/Dim3, jerarquía, asignaciones y alcance | `centros-costo/configuracion` y asignaciones | integración CRUD, jerarquía, alcance, selector; pruebas de UI | Núcleo directo localizado; **parcial real** | Dimensiones definitivas Millet, vínculo con catálogo contable y consumo obligatorio en pólizas/módulos |
| F1-CON-03 | Otros módulos definen `IPeriodoContablePort`, pero lo conectan a implementaciones `NoOp` que siempre abren | Sin ruta contable | Sin prueba de calendario/cierre contable real | **No localizado** | Calendario, abrir/cerrar/reabrir, autorización, bloqueo transversal y auditoría |
| F1-CON-04 | No se localizó agregado/comando de póliza manual | Sin ruta dedicada | Sin prueba directa localizada | **No localizado** | Captura de encabezado/partidas, cuadratura, autorización, reversa y evidencia |
| F1-CON-05 | Módulos publican eventos o llaman puertos de asiento; Facturación usa `NoOpContabilidadAsientoPort` | Sin ruta contable | Sin prueba de póliza automática persistida | **Pieza vecina solamente / parcial débil** | Consumidores, reglas por evento, idempotencia, cuentas/dimensiones, póliza y errores |
| F1-CON-06 | No se localizaron libro diario ni mayor | Sin ruta dedicada | Sin prueba directa localizada | **No localizado** | Consultas por cuenta/periodo/documento, saldos inicial/final, detalle y exportación |
| F1-CON-07 | No se localizó balanza de comprobación | Sin ruta dedicada | Sin prueba directa localizada | **No localizado** | Saldos/cargos/abonos por nivel, cuadratura, periodo, moneda y exportación |
| F1-CON-08 | No se localizaron estado de resultados ni balance general | Sin ruta dedicada | Sin prueba directa localizada | **No localizado** | Agrupadores, fórmulas, comparativos, periodos, dimensiones y totales de control |
| F1-CON-09 | Existen reportes auxiliares en módulos, pero no conciliador contra cuentas de control | Sin ruta contable | Sin prueba de conciliación submayor–mayor | **Pieza vecina solamente / parcial débil** | Saldos por módulo/cuenta, diferencias, investigación, ajuste y aprobación |
| F1-CON-10 | CxP/Facturación conservan impuestos parciales; no existe mayor fiscal contable | Sin ruta contable | Sin prueba integral localizada | **Pieza vecina solamente / parcial débil** | IVA pendiente/pagado, retenciones, movimientos, pólizas y conciliación fiscal |
| F1-CON-11 | CxP/Tesorería conservan moneda/TC parcial; no existe cálculo/asiento contable de diferencia | Sin ruta contable | Sin prueba directa localizada | **Pieza vecina solamente / parcial débil** | Fuente TC, valuación, realización/no realización, reversa y póliza |
| F1-CON-12 | Almacén tiene cierre mensual propio; no existe cierre contable secuencial global | Sin ruta contable | Sin prueba directa localizada | **Pieza vecina solamente / parcial débil** | Checklist, dependencias, bloqueos, reejecución, responsables y cierre de cada submódulo |
| F1-CON-13 | No se localizó importador/exportador contable controlado | Sin ruta dedicada | Sin prueba directa localizada | **No localizado** | Layout, validación, staging, totales, rechazo, bitácora, reversa y exportación |

Conclusión de Contabilidad: CON-02 es una base real de dimensiones/centros de costo, pero no sustituye Contabilidad. CON-05, CON-09, CON-10, CON-11 y CON-12 sólo tienen eventos o datos vecinos; las demás funciones no se localizaron. El módulo contable requiere construcción sustancial.

## Corte 12 · Activos fijos

No se localizó un bounded context de Activos Fijos. La única implementación específica está dentro de Facturación para solicitar/autorizar una venta; su catálogo depende de un puerto `NoOp`.

| ID | Evidencia exacta | Pantalla/ruta | Pruebas directas | Resultado auditado | Falta para cerrar |
|---|---|---|---|---|---|
| F1-AF-01 | No se localizaron clases, cuentas ni reglas de activos | Sin ruta dedicada | Sin prueba directa localizada | **No localizado** | Catálogos por clase, vida útil, método/tasa, cuentas y vigencia |
| F1-AF-02 | No se localizó maestro de activos; `IActivosFijosReadPort` usa `NoOpActivosFijosReadPort` | Sin ruta de maestro | Sin prueba de maestro localizada | **No localizado** | Alta, identificación/etiqueta, costo, fecha, responsable, ubicación, empresa y estado |
| F1-AF-03 | No se localizó capitalización desde Compras/CxP | Sin ruta dedicada | Sin prueba directa localizada | **No localizado** | Criterio capitalizable, origen documental, mejoras, fecha en servicio y póliza |
| F1-AF-04 | No se localizó motor de depreciación ni contabilización | Sin ruta dedicada | Sin prueba directa localizada | **No localizado** | Calendario, método, vida útil, residual, cálculo, cierre, póliza y reversa |
| F1-AF-05 | No se localizaron transferencias de activo | Sin ruta dedicada | Sin prueba directa localizada | **No localizado** | Cambio de responsable/ubicación/empresa, autorización, custodia e historial |
| F1-AF-06 | Existe un servicio transversal de adjuntos, pero no documentos/garantías/mantenimiento asociados a un activo | Sin ruta de activo | Sin prueba directa de garantía/mantenimiento | **Pieza vecina solamente / parcial débil** | Relación al maestro, vencimientos, proveedor, plan/orden de mantenimiento y alertas |
| F1-AF-07 | `AutorizacionVentaActivo`, comando/handler/endpoints y pantalla de autorizaciones en Facturación | `facturacion/activos` | `ActivosTests.cs`; smoke/hook de la página | Núcleo directo localizado; **parcial condicionado** | Maestro real, valor neto, baja/disposición, factura completa, autorización final y póliza |
| F1-AF-08 | No se localizó inventario físico ni etiquetado de activos | Sin ruta dedicada | Sin prueba directa localizada | **No localizado** | Campaña, escaneo/etiqueta, ubicación, responsable, diferencia, evidencia y ajuste |
| F1-AF-09 | No se localizó auxiliar de activos ni conciliación con contabilidad | Sin ruta dedicada | Sin prueba directa localizada | **No localizado** | Saldos costo/depreciación/neto, cuenta control, diferencias, ajustes y aprobación |

Conclusión de Activos fijos: AF-07 es una pieza parcial en Facturación y AF-06 puede reutilizar adjuntos; las otras siete funcionalidades no se localizaron. No debe presentarse la pantalla de autorización de venta como un módulo de Activos Fijos construido.

## Avance de esta auditoría

- Funcionalidades parciales/no existentes auditadas: **104 de 104**.
- Pendientes de auditoría específica: **0**.
- Cobertura total de Fase 1: **139 de 139** al combinar este documento con la auditoría de las 35 existentes.

La auditoría técnica de código queda completa a nivel de trazabilidad. Esto no sustituye la ejecución individual de cada flujo, QA/regresión, prueba con datos/servicios reales ni UAT; esos resultados siguen pendientes por fila en el inventario.
