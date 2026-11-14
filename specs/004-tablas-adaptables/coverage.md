# Cobertura de tablas

Inventario estático de todos los archivos de tabla del frontend (excluye tests). Una fila puede contener varias tablas; no equivale a rutas ni a vistas aceptadas en QA. Las fichas, tablas de captura y estructuras agrupadas mantienen sus filas completas; las colecciones que ya tenían paginador o se marcan como consulta usan la cantidad persistida. La verificación de navegador cubre familias compartidas, no cada ruta autenticada.

- Ficha/captura/selección: 69 archivos.
- Reporte/agrupación: 11 archivos.
- Base compartida: 1 archivos.
- Paginador uniforme: 96 archivos.
- Consulta paginada: 43 archivos.
- Referencia: 2 archivos.

| Archivo en frontend/src | Tratamiento | Criterio |
|---|---|---|
| components/erp/collaboration/ConflictResolutionDialog.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| components/erp/reportes/ReporteShell.tsx | Reporte/agrupación | Conserva filas, agrupaciones y exportación completa; distribución común. |
| components/ui/table.tsx | Base compartida | Contención, columnas, cabecera y modo de consulta opcional. |
| features/activos-fijos/pages/ActivosPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/activos-fijos/pages/ClasesPage.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| features/activos-fijos/pages/ConteosPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/activos-fijos/pages/PorVencerPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/almacen/components/ActasCierreSection.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| features/almacen/components/ActivosRecibidosOc.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/almacen/components/CorreccionesConsumoSeccion.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| features/almacen/components/NuevaRecepcionSheet.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/almacen/components/PosicionesTraslado.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/almacen/components/PropuestaReposicion.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/almacen/components/ReporteAlmacen.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/almacen/components/RoturasAppPorAutorizar.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| features/almacen/pages/AlmacenesPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/almacen/pages/AprobacionConteoPage.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/almacen/pages/AsignacionesPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/almacen/pages/CapturaConteoPage.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/almacen/pages/ConciliacionDiariaPage.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/almacen/pages/ConsumosRevisionPage.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| features/almacen/pages/DevolucionProveedorDetallePage.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/almacen/pages/DevolucionesPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/almacen/pages/InventariosPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/almacen/pages/MaterialEnRevisionPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/almacen/pages/MensajesAppPage.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| features/almacen/pages/MermasProcesoPage.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| features/almacen/pages/NormasConsumoPage.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| features/almacen/pages/OrigenCapturaMpPage.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| features/almacen/pages/RecepcionDetallePage.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/almacen/pages/RecepcionesPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/almacen/pages/ReordenPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/almacen/pages/SaldosPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/almacen/pages/SalidaDetallePage.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/almacen/pages/SalidasPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/almacen/pages/SalidasRoturaPage.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| features/almacen/pages/SubAlmacenesPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/almacen/pages/TallerCierrePage.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/almacen/pages/TamboresLineaPage.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| features/almacen/pages/UbicacionesPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/cargas/pages/LoteCargaPage.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| features/centros-costo/components/GruposManagerDialog.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/comercio-exterior/components/ContenedoresPanel.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/comercio-exterior/components/EmbarqueCeResumen.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/comercio-exterior/components/FinanzasPanel.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/comercio-exterior/components/OtrPanel.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/comercio-exterior/components/RelacionIvaReporte.tsx | Reporte/agrupación | Conserva filas, agrupaciones y exportación completa; distribución común. |
| features/comercio-exterior/pages/ExpedienteDetallePage.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/comercio-exterior/pages/ExpedientesPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/comercio-exterior/pages/OtrBandejaPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/compras/components/EditorLineas.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/compras/components/ListaLineas.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/compras/components/NecesidadRqAppPanel.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/compras/components/UmbralesAprobacionSection.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/compras/integraciones/pages/TableroEnviosAlmacenes.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| features/compras/ordenes/components/EditorLineas.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/compras/ordenes/components/HistorialIncidenciasProveedor.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| features/compras/ordenes/components/RespuestaProveedorPanel.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/compras/ordenes/components/SelectorRequisicionesConsolidacion.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/compras/ordenes/pages/BandejaOrdenesCompra.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/compras/ordenes/pages/BandejaPendientesOc.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/compras/ordenes/pages/HistorialComprasMaterial.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/compras/ordenes/pages/PartidasAbiertas.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/compras/pages/AdminAprobadores.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/compras/pages/Ayuda.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/compras/pages/BandejaPendientes.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/compras/pages/BandejaRequisiciones.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/compras/pages/ComparativoCotizaciones.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/compras/reportes/pages/DrillOrdenesSheet.tsx | Reporte/agrupación | Conserva filas, agrupaciones y exportación completa; distribución común. |
| features/contabilidad/components/PerfilReporte.tsx | Reporte/agrupación | Conserva filas, agrupaciones y exportación completa; distribución común. |
| features/contabilidad/components/SolicitudesCatalogo.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/contabilidad/components/VistaPreviaTabla.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/contabilidad/components/dimensiones/ReglasTab.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/contabilidad/components/dimensiones/TiposDocumentoTab.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/contabilidad/components/dimensiones/UbicacionesTab.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/contabilidad/components/iva/PapelTrabajoIvaSeccion.tsx | Reporte/agrupación | Conserva filas, agrupaciones y exportación completa; distribución común. |
| features/contabilidad/components/iva/RecalcularIvaDocumento.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/contabilidad/components/motor/PlantillasMotor.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| features/contabilidad/components/motor/RechazosMotor.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/contabilidad/components/motor/RolesMotor.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/contabilidad/components/polizas/SolicitudesAnulacionPanel.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| features/contabilidad/pages/CatalogoPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/contabilidad/pages/ContabilidadElectronicaPage.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/contabilidad/pages/ImportarNominaPage.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/contabilidad/pages/IncidenciasPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/contabilidad/pages/LiquidacionIvaPage.tsx | Reporte/agrupación | Conserva filas, agrupaciones y exportación completa; distribución común. |
| features/contabilidad/pages/MovimientosPruebaPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/contabilidad/pages/PeriodosPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/contabilidad/pages/PlantillaRecurrenteDetallePage.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/contabilidad/pages/PolizaDetallePage.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/contabilidad/pages/PolizasPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/contabilidad/pages/PolizasRecurrentesPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/contabilidad/pages/ReporteCuentaPuentePage.tsx | Reporte/agrupación | Conserva filas, agrupaciones y exportación completa; distribución común. |
| features/contabilidad/pages/SaldosPeriodoPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/cxc/components/DepositosSinIdentificar.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| features/cxc/components/EnviosEstadoCuentaTabla.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| features/cxc/components/MatchingAplicacionTable.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/cxc/components/PanelBloqueoEstadoCuenta.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| features/cxc/components/PanelCorreccionesCartera.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| features/cxc/components/PanelFichasCobranza.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| features/cxc/components/ReporteCxc.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/cxc/components/TimelineCobranza.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/cxc/pages/AlertasPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/cxc/pages/AnticiposCxcPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/cxc/pages/BandejaAplicaciones.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/cxc/pages/BandejaLiberaciones.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/cxc/pages/BandejaLineasCredito.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/cxc/pages/ConciliacionCarteraPage.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/cxc/pages/ConciliacionPolizasPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/cxc/pages/DetalleAplicacion.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/cxc/pages/ReglasSeriePage.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| features/cxp/components/BuzonRevisionPanel.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| features/cxp/components/CfdiDetalleSheet.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/cxp/pages/AnticiposPage.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| features/cxp/pages/AprobadoresLimitesPage.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| features/cxp/pages/CfdisPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/cxp/pages/ComprobacionDetallePage.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/cxp/pages/ComprobacionesPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/cxp/pages/EstadosCuentaTcPage.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/cxp/pages/FacturaDetallePage.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/cxp/pages/FacturasPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/cxp/pages/MovimientosTcPage.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| features/cxp/pages/NotasCargoPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/cxp/pages/NotasCreditoPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/cxp/pages/PoliticasViaticosPage.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| features/cxp/pages/ReposicionesCajaPage.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| features/cxp/pages/RetencionesPage.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| features/cxp/pages/RevaluacionCierrePage.tsx | Reporte/agrupación | Conserva filas, agrupaciones y exportación completa; distribución común. |
| features/cxp/pages/RevisionPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/cxp/pages/TarjetasPage.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| features/cxp/pages/ViaticoDetallePage.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/cxp/pages/ViaticosPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/facturacion/components/AdoptarCfdiDialog.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/facturacion/components/FacturaBancoTerminalSection.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/facturacion/components/IntentosTimbradoPanel.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/facturacion/components/PorCobrarCaja.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/facturacion/components/RegistrarSalidaDialog.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/facturacion/components/ReporteFacturacion.tsx | Reporte/agrupación | Conserva filas, agrupaciones y exportación completa; distribución común. |
| features/facturacion/components/SalidasPedido.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| features/facturacion/components/TerminalTarjetaCard.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/facturacion/components/emitir-factura/DiferenciasProformaDialog.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/facturacion/pages/Activos.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/facturacion/pages/BandejaCajas.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/facturacion/pages/BandejaCancelaciones.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| features/facturacion/pages/BandejaCartaPorte.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/facturacion/pages/BandejaContingencias.tsx | Reporte/agrupación | Conserva filas, agrupaciones y exportación completa; distribución común. |
| features/facturacion/pages/BandejaExcepciones.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/facturacion/pages/BandejaFacturas.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/facturacion/pages/BandejaFacturasAnticipo.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/facturacion/pages/BandejaPedidos.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/facturacion/pages/BandejaRepp.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/facturacion/pages/ConciliacionSat.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/facturacion/pages/DetalleCaja.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/facturacion/pages/DetalleCartaPorte.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/facturacion/pages/DetalleFactura.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/facturacion/pages/DetalleFacturaAnticipo.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/facturacion/pages/DetallePedido.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/facturacion/pages/DetalleRepp.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/facturacion/pages/EstadoCuentaAnticipos.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/facturacion/pages/MiCaja.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/facturacion/pages/NotasCreditoPorAutorizar.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/facturacion/pages/PendientesRepp.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/facturacion/pages/ReporteCorteCaja.tsx | Reporte/agrupación | Conserva filas, agrupaciones y exportación completa; distribución común. |
| features/incidencias/pages/MisIncidenciasPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/integraciones/pages/TableroIntegraciones.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/integraciones-fiscal/components/CertificadosCsdSection.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/portal-proveedores/PortalProveedoresPage.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/producto-terminado/pages/ExcepcionesPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/producto-terminado/pages/HojasSalidaPage.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| features/producto-terminado/pages/LiquidacionRutaPage.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/producto-terminado/pages/PiezasPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/producto-terminado/pages/SalidasPendientesPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/producto-terminado/pages/SolicitudesParcialesPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/producto-terminado/pages/TraspasosPage.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| features/sistema-diseno/Plantillas.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/sistema-diseno/SistemaDisenoPage.tsx | Referencia | Ejemplo/documentación del componente, no listado operativo. |
| features/sistema-diseno/ejemplos/Table.tsx | Referencia | Ejemplo/documentación del componente, no listado operativo. |
| features/tesoreria/components/FoliosTransferenciaTabla.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/tesoreria/components/RegistrarPagoSheet.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/tesoreria/components/SolicitudesCuenta.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| features/tesoreria/pages/BandejaChequesRecibidos.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| features/tesoreria/pages/BandejaConciliaciones.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/tesoreria/pages/BandejaCorridas.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/tesoreria/pages/BandejaCuentas.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/tesoreria/pages/BandejaDepositos.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/tesoreria/pages/BandejaDevoluciones.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/tesoreria/pages/BandejaDiferenciasCorte.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| features/tesoreria/pages/BandejaMovimientos.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/tesoreria/pages/BandejaPagos.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/tesoreria/pages/BandejaPagosACuenta.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/tesoreria/pages/BandejaPagosComercioExterior.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| features/tesoreria/pages/BandejaReppPendientes.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| features/tesoreria/pages/DetalleCorrida.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/tesoreria/pages/DetalleMovimiento.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/tesoreria/pages/NuevaCorrida.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| features/tramos/TramosPage.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| modules/administracion/components/AuditoriaDetalleDrawer.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| modules/administracion/components/AuditoriaPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| modules/administracion/components/CanalesVentaPage.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| modules/administracion/components/DepartamentosPanel.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| modules/administracion/components/EmpleadosPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| modules/administracion/components/MiEmpresaContenido.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| modules/administracion/components/PuestosPage.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| modules/administracion/components/SeriesPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| modules/administracion/components/SucursalesPanel.tsx | Consulta paginada | Pagina la colección recibida, sin modificar su fuente ni simular un total remoto. |
| modules/catalogos/components/CatalogoEditableTable.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| modules/catalogos/components/CatalogoSatReadOnlyTable.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| modules/catalogos/components/HistoricoTiposCambioPanel.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| modules/catalogos/components/ImpuestosPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| modules/catalogos/components/ListaMonedasCompacta.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| modules/datos-maestros/components/CambiosAwHistorial.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| modules/datos-maestros/components/CargaMaestrosDialog.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| modules/datos-maestros/components/ClienteOrigenAwSection.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| modules/datos-maestros/components/ClienteSaldoGrupoSection.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| modules/datos-maestros/components/CorridaAwDetalle.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| modules/datos-maestros/components/DetalleOrigenAw.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| modules/datos-maestros/components/ExpedienteTiposPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| modules/datos-maestros/components/F1AClientesPanel.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| modules/datos-maestros/components/F1AFiscalPanel.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |
| modules/datos-maestros/components/ProductoAwOrigenSection.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| modules/datos-maestros/components/ProveedorCuentasBancariasSection.tsx | Ficha/captura/selección | No se corta automáticamente esta estructura: conserva edición, selección y todas las partidas; estilos comunes. |
| modules/datos-maestros/components/SincronizacionAwPage.tsx | Paginador uniforme | Cantidad/control conectados al estado local o a la consulta; registro por vista. |

## Otros listados y bitácoras
Los listados basados en rejilla, históricos de documentos/mantenimiento y bitácora de roles también se migraron a PaginacionTabla. La conservación de APIs con carga incremental y límites explícitos permanece; seleccionar una cantidad no descarga automáticamente todo el servidor. Piezas de un pedido conservan la vista conjunta requerida para solicitar una salida parcial.

## Excepciones de estructura
Fichas de datos de origen, comparativos, conciliaciones, estados financieros/tributarios, editores de partidas, selectores de conjuntos y pantallas de captura conservan todos los registros de su estructura. No se aplica un corte automático que cambie totales o desmonte campos; el componente común mejora columnas/contención y el scroll existente. No se declara prueba funcional de cada una por la existencia de esta matriz.
