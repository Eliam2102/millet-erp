/**
 * Códigos canónicos de permisos del sistema. **Mirror manual** de
 * <c>backend/src/Identidad/Domain/PermisosCanonicos.cs</c>. Mantener en
 * sincronía: si agregas un permiso allá, agrégalo aquí. Cuando se
 * implemente ADR-0017 (codegen TS desde OpenAPI), este archivo se vuelve
 * autogenerado.
 *
 * Uso: <c>useHasPermission(PermisosCanonicos.IdentidadUsuariosCrear)</c>.
 */
export const PermisosCanonicos = {
  // Infraestructura
  InfraHealthLeer: 'infra.health.leer',
  InfraAuditLogLeer: 'infra.audit_log.leer',

  // Identidad — gestión de usuarios y roles. Los granulares
  // (Usuarios/Roles/Permisos) llegaron con F-Admin-PR3.2/PR3.3 y conviven
  // con el grueso <c>identidad.roles.administrar</c> por compatibilidad
  // hasta que la UI termine de migrar.
  IdentidadUsuariosLeer: 'identidad.usuarios.leer',
  IdentidadUsuariosCrear: 'identidad.usuarios.crear',
  IdentidadUsuariosEditar: 'identidad.usuarios.editar',
  IdentidadUsuariosDesactivar: 'identidad.usuarios.desactivar',
  IdentidadRolesLeer: 'identidad.roles.leer',
  IdentidadRolesAdministrar: 'identidad.roles.administrar',
  IdentidadRolesCrear: 'identidad.roles.crear',
  IdentidadRolesEditar: 'identidad.roles.editar',
  IdentidadRolesEliminar: 'identidad.roles.eliminar',
  IdentidadRolesAsignarPermisos: 'identidad.roles.asignar-permisos',
  IdentidadRolesGruposEntraIdGestionar: 'identidad.roles.grupos-entra-id-gestionar',
  IdentidadPermisosLeer: 'identidad.permisos.leer',
  IdentidadAsignacionesLeer: 'identidad.asignaciones.leer',
  IdentidadAsignacionesAdministrar: 'identidad.asignaciones.administrar',

  // Compras — Requisiciones (mirror de PermisosCanonicos.cs §"Compras —
  // Requisiciones"). Nota: el doc 05 §10.2 lista "autorizar.nivel1" con
  // punto, pero el backend usa kebab-case ("autorizar-nivel1") — acá manda
  // el código del backend, no el doc.
  ComprasRequisicionesLeer: 'compras.requisiciones.leer',
  ComprasRequisicionesCrear: 'compras.requisiciones.crear',
  ComprasRequisicionesEditar: 'compras.requisiciones.editar',
  ComprasRequisicionesEliminar: 'compras.requisiciones.eliminar',
  ComprasRequisicionesCancelar: 'compras.requisiciones.cancelar',
  ComprasRequisicionesCerrarManual: 'compras.requisiciones.cerrar-manual',
  ComprasRequisicionesAutorizarNivel1: 'compras.requisiciones.autorizar-nivel1',
  ComprasRequisicionesAutorizarNivel2: 'compras.requisiciones.autorizar-nivel2',
  ComprasRequisicionesRechazar: 'compras.requisiciones.rechazar',
  ComprasRequisicionesEditarDeOtrosUsuarios:
    'compras.requisiciones.editar-de-otros-usuarios',
  ComprasRequisicionesSeleccionarRequisitante:
    'compras.requisiciones.seleccionar-requisitante',
  ComprasRequisicionesVerTodosDepartamentos:
    'compras.requisiciones.ver-todos-departamentos',

  // Compras — Administración de aprobadores (UF6-PR1)
  ComprasAprobadoresAdministrar: 'compras.aprobadores.administrar',

  // Compras — Órdenes de compra (mirror de PermisosCanonicos.cs §"Compras
  // — Órdenes de compra"). 10 permisos canónicos del submódulo OC,
  // incluyendo `crear-sin-rq` (FOC11). Backend usa kebab-case para las
  // partes compuestas — los strings literales acá los espejan
  // exactamente; el doc 05 §10.2 OC usaba notación con punto pero
  // backend manda. Para wireup ver UF0-PR1 y siguientes (UF1-UF8).
  ComprasOrdenesLeer: 'compras.ordenes.leer',
  ComprasOrdenesCrear: 'compras.ordenes.crear',
  ComprasOrdenesCrearSinRq: 'compras.ordenes.crear-sin-rq',
  ComprasOrdenesAdjuntar: 'compras.ordenes.adjuntar',
  ComprasOrdenesLogistica: 'compras.ordenes.logistica',
  ComprasOrdenesAutorizarNivel1: 'compras.ordenes.autorizar-nivel1',
  ComprasOrdenesAutorizarNivel2: 'compras.ordenes.autorizar-nivel2',
  ComprasOrdenesCancelar: 'compras.ordenes.cancelar',
  ComprasOrdenesCancelarDoble: 'compras.ordenes.cancelar-doble',
  ComprasOrdenesReportesPartidasAbiertas:
    'compras.ordenes.reportes-partidas-abiertas',

  // Compras — Configuración por empresa (PR-A 2026-05-13). El owner edita
  // settings como AutoGenerarOcAlAutorizar via PATCH /api/v1/compras/configuracion.
  ComprasConfiguracionLeer: 'compras.configuracion.leer',
  ComprasConfiguracionEditar: 'compras.configuracion.editar',

  // Compartido — catálogos cross-empresa. Solo `leer` se consume desde
  // Compras (selectores read-only de proveedores y artículos). El permiso
  // `compartido.catalogos.administrar` lo consume el módulo Datos
  // Maestros, que tendrá su propio manifiesto.
  CompartidoCatalogosLeer: 'compartido.catalogos.leer',
  CompartidoCatalogosAdministrar: 'compartido.catalogos.administrar',

  // Catálogos granulares (F-Admin-PR5.x). Reusan el namespace de
  // Compartido. Convención kebab-case 3-parte para tipos-cambio y
  // unidades-medida (el seed parser de IdentidadDbContext hace
  // Codigo.Split('.') y espera exactamente 3 segmentos).
  CatalogosMonedasGestionar: 'catalogos.monedas.gestionar',
  CatalogosTiposCambioGestionar: 'catalogos.tipos-cambio.gestionar',
  CatalogosCondicionesPagoGestionar: 'catalogos.condiciones-pago.gestionar',
  CatalogosIncotermsGestionar: 'catalogos.incoterms.gestionar',
  CatalogosTransportistasGestionar: 'catalogos.transportistas.gestionar',
  CatalogosUnidadesMedidaGestionar: 'catalogos.unidades-medida.gestionar',
  // <c>UsosPrincipales</c> aún no tiene permiso granular en backend
  // (PermisosCanonicos.cs); el endpoint POST/PATCH/desactivar usa
  // <c>compartido.catalogos.administrar</c>. Esta key se mantiene en
  // el archivo para que la card y la ruta de
  // <c>/admin/catalogos/usos-principales</c> apunten a un nombre
  // estable; cuando el backend agregue el granular, basta con cambiar
  // el string literal acá. Mientras tanto, el guard real es el grueso.

  // Datos Maestros granulares (F-Admin-PR4.5). Reemplazan al grueso
  // <c>compartido.catalogos.administrar</c> para los recursos de Datos
  // Maestros (Proveedores, Articulos).
  DatosMaestrosProveedoresGestionar: 'datos_maestros.proveedores.gestionar',
  DatosMaestrosArticulosGestionar: 'datos_maestros.articulos.gestionar',
  // Masters auto-provisionables de la ingesta A+W (ADR-0048). A
  // diferencia de Proveedores/Artículos, el backend usa este granular
  // también para las mutaciones (no el grueso
  // compartido.catalogos.administrar).
  DatosMaestrosClientesGestionar: 'datos_maestros.clientes.gestionar',
  DatosMaestrosProductosAwGestionar: 'datos_maestros.productos-aw.gestionar',

  // Administración — andamio mínimo del área /admin (F-Admin-PR1.2) y
  // CRUD de Empresas/Sucursales/Departamentos (F-Admin-PR2.3). Si el
  // usuario tiene alguno de los <c>admin.*</c>, el engrane del topbar
  // es visible y el landing /admin renderiza al menos una card.
  AdminEmpresasLeer: 'admin.empresas.leer',
  AdminEmpresasCrear: 'admin.empresas.crear',
  AdminEmpresasEditar: 'admin.empresas.editar',
  AdminEmpresasDesactivar: 'admin.empresas.desactivar',
  AdminEmpresasSucursalesGestionar: 'admin.empresas.sucursales-gestionar',
  AdminDepartamentosGestionar: 'admin.departamentos.gestionar',
  // PR-A1 backend / PR-A3 frontend: asignación N:M Sucursal ↔ Depto.
  // Gobierna el Sheet "Gestionar departamentos" del SucursalesPanel.
  AdminSucursalesDepartamentosGestionar:
    'admin.sucursales.departamentos-gestionar',
  AdminAuditoriaLeer: 'admin.auditoria.leer',
  // ADM-FE-PR1 (doc 10-catalogo-puestos-empleados): master de puestos y
  // empleados — habilitador de reglas de viáticos por puesto.
  AdminPuestosGestionar: 'admin.puestos.gestionar',
  AdminEmpleadosGestionar: 'admin.empleados.gestionar',
  AdminSeriesGestionar: 'admin.series.gestionar',
  AdminParametrosLeer: 'admin.parametros.leer',
  AdminParametrosEditar: 'admin.parametros.editar',

  // Integraciones.Fiscal — admin de la configuración del PAC + RFCs
  // receptores. Mirror de backend PermisosCanonicos.cs (sub-namespace
  // GUID 00000006-1xxx).
  IntegracionesFiscalLeer: 'integraciones.fiscal.leer',
  IntegracionesFiscalAdministrar: 'integraciones.fiscal.administrar',

  // Almacén (mirror del backend PermisosCanonicos.cs §"Almacén" —
  // namespace GUID 00000008-*). 30 permisos canónicos. Convención
  // kebab-case 3-parte: el backend valida con Codigo.Split('.') y
  // espera exactamente 3 segmentos modulo/recurso/accion.
  AlmacenAlmacenesRead: 'almacen.almacenes.leer',
  AlmacenAlmacenesAdministrar: 'almacen.almacenes.administrar',

  AlmacenEntradasLeer: 'almacen.entradas.leer',
  AlmacenEntradasCapturar: 'almacen.entradas.capturar',
  AlmacenEntradasRegistrar: 'almacen.entradas.registrar',
  AlmacenEntradasCancelarBorrador: 'almacen.entradas.cancelar-borrador',

  AlmacenSalidasLeerPropias: 'almacen.salidas.leer-propias',
  AlmacenSalidasLeerTodas: 'almacen.salidas.leer-todas',
  AlmacenSalidasCapturar: 'almacen.salidas.capturar',
  AlmacenSalidasRegistrar: 'almacen.salidas.registrar',
  AlmacenSalidasPorVale: 'almacen.salidas.por-vale',

  AlmacenDevolucionesInternasLeer: 'almacen.devoluciones-internas.leer',
  AlmacenDevolucionesInternasCapturar: 'almacen.devoluciones-internas.capturar',
  AlmacenDevolucionesProveedorIniciar: 'almacen.devoluciones-proveedor.iniciar',
  AlmacenDevolucionesProveedorAutorizar: 'almacen.devoluciones-proveedor.autorizar',
  AlmacenDevolucionesProveedorRegistrar: 'almacen.devoluciones-proveedor.registrar',

  AlmacenInventariosLeer: 'almacen.inventarios.leer',
  AlmacenInventariosCrear: 'almacen.inventarios.crear',
  AlmacenInventariosCapturar: 'almacen.inventarios.capturar',
  AlmacenInventariosAprobarNivel1: 'almacen.inventarios.aprobar-nivel1',
  AlmacenInventariosAprobarNivel2: 'almacen.inventarios.aprobar-nivel2',
  AlmacenInventariosAprobarNivel3: 'almacen.inventarios.aprobar-nivel3',

  AlmacenAjustesManual: 'almacen.ajustes.manual',
  AlmacenReportesAlfak: 'almacen.reportes.alfak',
  AlmacenReportesMpCnk: 'almacen.reportes.mp-cnk',
  AlmacenReportesMovimientos: 'almacen.reportes.movimientos',
  AlmacenCierreMesEjecutar: 'almacen.cierre-mes.ejecutar',
  AlmacenLecturaTotal: 'almacen.lectura.total',
  AlmacenReservasAdministrar: 'almacen.reservas.administrar',
  // Reabasto / reorden N1-N2 (ADR-0047 PR5.A). UI = "Reabasto", código = reorden.
  AlmacenReordenRead: 'almacen.reorden.leer',
  AlmacenReordenAdministrar: 'almacen.reorden.administrar',
  // Asignación artículo↔ubicación N4 (ADR-0047 PR3/PR C). UI = "Ubicación de artículos".
  AlmacenAsignacionesRead: 'almacen.asignaciones.leer',
  AlmacenAsignacionesAdministrar: 'almacen.asignaciones.administrar',
  // Ubicaciones físicas N4 (racks/pasillos, ADR-0047 PR C7.1). UI = "Ubicaciones".
  AlmacenUbicacionesRead: 'almacen.ubicaciones.leer',
  AlmacenUbicacionesAdministrar: 'almacen.ubicaciones.administrar',

  // Cuentas por Pagar — mirror manual de backend/src/Identidad/Domain/
  // PermisosCanonicos.cs §"Cuentas por Pagar". Módulo CxP en backend ya
  // mergeado (F0..F10 cerrados). Frontend arranca con FE-F0-PR1.
  CuentasPorPagarFacturasLeer: 'cuentas_por_pagar.facturas.leer',
  CuentasPorPagarFacturasCapturar: 'cuentas_por_pagar.facturas.capturar',
  CuentasPorPagarFacturasEditar: 'cuentas_por_pagar.facturas.editar',
  CuentasPorPagarFacturasCancelar: 'cuentas_por_pagar.facturas.cancelar',
  CuentasPorPagarFacturasEnviarRevision:
    'cuentas_por_pagar.facturas.enviar-revision',
  CuentasPorPagarFacturasLiberarRevision:
    'cuentas_por_pagar.facturas.liberar-revision',
  CuentasPorPagarFacturasAutorizar: 'cuentas_por_pagar.facturas.autorizar',

  CuentasPorPagarNotasCreditoLeer: 'cuentas_por_pagar.notas-credito.leer',
  CuentasPorPagarNotasCreditoCapturar:
    'cuentas_por_pagar.notas-credito.capturar',

  CuentasPorPagarNotasCargoLeer: 'cuentas_por_pagar.notas-cargo.leer',
  CuentasPorPagarNotasCargoCrear: 'cuentas_por_pagar.notas-cargo.crear',
  CuentasPorPagarNotasCargoAutorizar:
    'cuentas_por_pagar.notas-cargo.autorizar',
  CuentasPorPagarNotasCargoAplicar: 'cuentas_por_pagar.notas-cargo.aplicar',

  CuentasPorPagarAnticiposLeer: 'cuentas_por_pagar.anticipos.leer',
  CuentasPorPagarAnticiposCapturar: 'cuentas_por_pagar.anticipos.capturar',

  CuentasPorPagarComprobacionesLeer: 'cuentas_por_pagar.comprobaciones.leer',
  CuentasPorPagarComprobacionesCapturar:
    'cuentas_por_pagar.comprobaciones.capturar',
  CuentasPorPagarComprobacionesAprobarNivel1:
    'cuentas_por_pagar.comprobaciones.aprobar-nivel1',
  CuentasPorPagarComprobacionesAprobarNivel2:
    'cuentas_por_pagar.comprobaciones.aprobar-nivel2',

  CuentasPorPagarViaticosLeer: 'cuentas_por_pagar.viaticos.leer',
  CuentasPorPagarViaticosSolicitar: 'cuentas_por_pagar.viaticos.solicitar',
  CuentasPorPagarViaticosAutorizarJefe:
    'cuentas_por_pagar.viaticos.autorizar-jefe',
  CuentasPorPagarViaticosAutorizarDf:
    'cuentas_por_pagar.viaticos.autorizar-df',
  CuentasPorPagarViaticosMarcarPagado:
    'cuentas_por_pagar.viaticos.marcar-pagado',
  CuentasPorPagarViaticosCapturarComprobacion:
    'cuentas_por_pagar.viaticos.capturar-comprobacion',
  CuentasPorPagarViaticosLiberar: 'cuentas_por_pagar.viaticos.liberar',

  CuentasPorPagarCatalogosAprobadoresAdministrar:
    'cuentas_por_pagar.catalogos.aprobadores.administrar',
  CuentasPorPagarCatalogosPoliticasAdministrar:
    'cuentas_por_pagar.catalogos.politicas-viaticos.administrar',

  CuentasPorPagarTcLeer: 'cuentas_por_pagar.tc.leer',
  CuentasPorPagarTcRegistrarMovimiento:
    'cuentas_por_pagar.tc.registrar-movimiento',
  CuentasPorPagarTcCerrarEstadoCuenta:
    'cuentas_por_pagar.tc.cerrar-estado-cuenta',
  CuentasPorPagarTcAdministrar: 'cuentas_por_pagar.tc.administrar',
  CuentasPorPagarTcDisputar: 'cuentas_por_pagar.tc.disputar',

  CuentasPorPagarProveedoresPonerRevision:
    'cuentas_por_pagar.proveedores.poner-revision',
  CuentasPorPagarProveedoresLiberarRevision:
    'cuentas_por_pagar.proveedores.liberar-revision',
  CuentasPorPagarProveedoresAjustarTolerancia:
    'cuentas_por_pagar.proveedores.ajustar-tolerancia',

  CuentasPorPagarReportesCartera: 'cuentas_por_pagar.reportes.cartera',
  CuentasPorPagarReportesAntiguedad: 'cuentas_por_pagar.reportes.antiguedad',
  CuentasPorPagarReportesDiot: 'cuentas_por_pagar.reportes.diot',
  CuentasPorPagarReportesTc: 'cuentas_por_pagar.reportes.tc',

  CuentasPorPagarCfdisLeer: 'cuentas_por_pagar.cfdis.leer',
  CuentasPorPagarCfdisCargarManual: 'cuentas_por_pagar.cfdis.cargar-manual',
  CuentasPorPagarCfdisDescartar: 'cuentas_por_pagar.cfdis.descartar',

  CuentasPorPagarReposicionesLeer: 'cuentas_por_pagar.reposiciones.leer',
  CuentasPorPagarReposicionesAdministrar:
    'cuentas_por_pagar.reposiciones.administrar',

  // Cuentas por Cobrar — mirror manual de backend PermisosCanonicos.cs
  // §"Cuentas por Cobrar" (namespace GUID 0000000A-*). Los 8 permisos se
  // seedearon completos en CXC-PR1 (mismo criterio que Facturación F0-PR1)
  // para no generar una migration por cada PR del módulo. Frontend
  // arranca con CXC-FE-PR1.
  CuentasPorCobrarLineasCreditoLeer: 'cuentas_por_cobrar.lineas-credito.leer',
  CuentasPorCobrarLineasCreditoGestionar:
    'cuentas_por_cobrar.lineas-credito.gestionar',
  CuentasPorCobrarLiberacionDecidir: 'cuentas_por_cobrar.liberacion.decidir',
  CuentasPorCobrarLiberacionOverride: 'cuentas_por_cobrar.liberacion.override',
  CuentasPorCobrarCobranzaRegistrar: 'cuentas_por_cobrar.cobranza.registrar',
  CuentasPorCobrarCarteraLeer: 'cuentas_por_cobrar.cartera.leer',
  CuentasPorCobrarAplicacionPagoProponer:
    'cuentas_por_cobrar.aplicacion-pago.proponer',
  CuentasPorCobrarAplicacionPagoConfirmar:
    'cuentas_por_cobrar.aplicacion-pago.confirmar',

  // Tesorería / Bancos — mirror manual de backend PermisosCanonicos.cs
  // §"Tesorería" (namespace GUID 0000000b-*). Los 19 permisos se
  // seedearon en TES-PR1 (+ pasivos.ver en TES-PR3). Frontend arranca
  // con TES-FE-PR1.
  TesoreriaCuentasVer: 'tesoreria.cuentas.ver',
  // TES-7 revisada: CRUD del catálogo de cuentas en Tesorería.
  TesoreriaCuentasAdministrar: 'tesoreria.cuentas.administrar',
  TesoreriaMovimientosVer: 'tesoreria.movimientos.ver',
  TesoreriaMovimientosRegistrar: 'tesoreria.movimientos.registrar',
  TesoreriaMovimientosVerCuentaCompleta:
    'tesoreria.movimientos.ver-cuenta-completa',
  TesoreriaPagosAplicar: 'tesoreria.pagos.aplicar',
  TesoreriaPagosRevertir: 'tesoreria.pagos.revertir',
  TesoreriaPagosCuentaRegistrar: 'tesoreria.pagos-cuenta.registrar',
  TesoreriaPagosCuentaLigar: 'tesoreria.pagos-cuenta.ligar',
  TesoreriaCorridasCrear: 'tesoreria.corridas.crear',
  TesoreriaCorridasAutorizar: 'tesoreria.corridas.autorizar',
  TesoreriaCorridasEjecutar: 'tesoreria.corridas.ejecutar',
  TesoreriaDepositosConfirmar: 'tesoreria.depositos.confirmar',
  TesoreriaDepositosRechazar: 'tesoreria.depositos.rechazar',
  TesoreriaReppRegistrar: 'tesoreria.repp.registrar',
  TesoreriaConciliacionOperar: 'tesoreria.conciliacion.operar',
  TesoreriaConciliacionCerrar: 'tesoreria.conciliacion.cerrar',
  TesoreriaPasivosSolicitarCancelacion:
    'tesoreria.pasivos.solicitar-cancelacion',
  TesoreriaPasivosVer: 'tesoreria.pasivos.ver',
  TesoreriaReportesVer: 'tesoreria.reportes.ver',

  // Facturación — CFDI 4.0 emitido. Mirror manual de backend
  // PermisosCanonicos.cs §"Facturación" (namespace GUID 00000009-*). 24
  // permisos canónicos. Convención kebab-case para partes compuestas
  // (`excepciones-resolver`, `bonificacion-emitir`, `carta-porte`); el
  // backend valida con Codigo.Split('.') esperando 3 segmentos. Frontend
  // arranca con FE-F0-PR1.
  FacturacionPedidosImportar: 'facturacion.pedidos.importar',
  FacturacionPedidosCapturar: 'facturacion.pedidos.capturar',
  FacturacionPedidosExcepcionesResolver:
    'facturacion.pedidos.excepciones-resolver',
  FacturacionFacturasEmitir: 'facturacion.facturas.emitir',
  FacturacionFacturasLeer: 'facturacion.facturas.leer',
  // CTA "Corregir en catálogo" del receptor en la emisión (detallado
  // pt. 2); los datos fiscales del receptor son siempre de solo lectura.
  FacturacionFacturasEditarReceptor: 'facturacion.facturas.editar-receptor',
  // Ídem para el artículo por posición (detallado pt. 3): CTA a Datos
  // Maestros → Productos A+W; permiso independiente del de receptor.
  FacturacionFacturasEditarArticulo: 'facturacion.facturas.editar-articulo',
  FacturacionAnticiposEmitir: 'facturacion.anticipos.emitir',
  FacturacionAnticiposVincular: 'facturacion.anticipos.vincular',
  FacturacionAnticiposLeer: 'facturacion.anticipos.leer',
  FacturacionNotasCreditoBonificacion:
    'facturacion.notas-credito.bonificacion-emitir',
  FacturacionNotasCreditoLeer: 'facturacion.notas-credito.leer',
  FacturacionCartaPorteEmitir: 'facturacion.carta-porte.emitir',
  FacturacionCartaPorteLeer: 'facturacion.carta-porte.leer',
  FacturacionReppEmitir: 'facturacion.repp.emitir',
  FacturacionCancelacionesSolicitar: 'facturacion.cancelaciones.solicitar',
  FacturacionCancelacionesConsultar: 'facturacion.cancelaciones.consultar',
  FacturacionActivosAutorizar: 'facturacion.activos.autorizar',
  FacturacionCajaLiquidar: 'facturacion.caja.liquidar',
  // Cajas (backend CAJAS-PR1, 12-cajas.md §8; FE desde cajas-pr5).
  FacturacionCajaAdministrar: 'facturacion.caja.administrar',
  FacturacionCajaOperar: 'facturacion.caja.operar',
  FacturacionCajaSupervisar: 'facturacion.caja.supervisar',
  FacturacionCajaLeerTodas: 'facturacion.caja.leer-todas',
  FacturacionReportesLeer: 'facturacion.reportes.leer',
  FacturacionComprobantesReintentarTimbrado:
    'facturacion.comprobantes.reintentar-timbrado',
  FacturacionComprobantesDescartar: 'facturacion.comprobantes.descartar',

  // Centros de Costo (backend CECO-PR1, renombre dim3.leer-todos en
  // CECO-PR4; FE desde CECO-FE-PR1). OJO: namespace snake
  // (centros_costo.*) — desviación aceptada de PR-1; kebab solo en el
  // sufijo leer-todos.
  CentrosCostoCatalogoLeer: 'centros_costo.catalogo.leer',
  CentrosCostoCatalogoAdministrar: 'centros_costo.catalogo.administrar',
  CentrosCostoAsignacionesAdministrar: 'centros_costo.asignaciones.administrar',
  CentrosCostoDim3LeerTodos: 'centros_costo.dim3.leer-todos',
} as const;

/** Tipo unión de todos los códigos de permiso conocidos (autocompletado en IDE). */
export type PermisoCode =
  (typeof PermisosCanonicos)[keyof typeof PermisosCanonicos];
