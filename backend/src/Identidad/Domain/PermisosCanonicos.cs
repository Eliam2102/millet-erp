namespace Millet.Identidad.Domain;

/// <summary>
/// Manifest de los permisos que el sistema define como canónicos. Cada
/// constante es el código del permiso (formato <c>modulo.recurso.accion</c>);
/// el seed de la migration inserta una fila en <c>identidad.permiso</c> por
/// cada entry de <see cref="Todos"/>.
///
/// GUIDs deterministas por módulo (namespace <c>00000002</c> para Identidad,
/// <c>00000003</c> para Compras, etc.) para que las migrations sean
/// idempotentes: re-ejecutar el seed no duplica filas. Ver ADR-0007.
///
/// Los permisos de cada módulo se agregan cuando llegue ese módulo.
/// </summary>
public static class PermisosCanonicos
{
    // ----- Infraestructura (transversal) -----
    public const string InfraHealthLeer = "infra.health.leer";
    public const string InfraAuditLogLeer = "infra.audit_log.leer";

    // ----- Identidad — gestión de usuarios y roles -----
    public const string IdentidadUsuariosLeer = "identidad.usuarios.leer";
    public const string IdentidadUsuariosCrear = "identidad.usuarios.crear";
    public const string IdentidadUsuariosEditar = "identidad.usuarios.editar";
    public const string IdentidadUsuariosDesactivar = "identidad.usuarios.desactivar";
    public const string IdentidadRolesLeer = "identidad.roles.leer";
    // <c>IdentidadRolesAdministrar</c> es el permiso grueso heredado de F0
    // (cubre crear+editar+desactivar+asignar permisos en un solo bit). En
    // F-Admin-PR3.2 se desglosa en granulares (Crear/Editar/Eliminar/
    // AsignarPermisos/GruposEntraIdGestionar) — pero se mantiene por
    // compatibilidad: roles que ya lo tenían siguen funcionando hasta
    // que se haga la migración fina por empresa.
    public const string IdentidadRolesAdministrar = "identidad.roles.administrar";
    public const string IdentidadRolesCrear = "identidad.roles.crear";
    public const string IdentidadRolesEditar = "identidad.roles.editar";
    public const string IdentidadRolesEliminar = "identidad.roles.eliminar";
    public const string IdentidadRolesAsignarPermisos = "identidad.roles.asignar-permisos";
    public const string IdentidadRolesGruposEntraIdGestionar = "identidad.roles.grupos-entra-id-gestionar";
    public const string IdentidadPermisosLeer = "identidad.permisos.leer";
    public const string IdentidadAsignacionesLeer = "identidad.asignaciones.leer";
    public const string IdentidadAsignacionesAdministrar = "identidad.asignaciones.administrar";

    // ----- Compras — Requisiciones -----
    // Formato: kebab-case en la última parte cuando hay calificadores
    // (autorizar-nivel1, ver-todos-departamentos, etc.) para mantener
    // la convención de 3 partes que el seed parser asume. El diseño §8.3
    // usa ":" y "autorizar:nivel1"; aquí alineamos con el formato del repo
    // (".") y kebab — anotado como hallazgo a actualizar en el diseño.
    public const string ComprasRequisicionesLeer                       = "compras.requisiciones.leer";
    public const string ComprasRequisicionesCrear                      = "compras.requisiciones.crear";
    public const string ComprasRequisicionesEditar                     = "compras.requisiciones.editar";
    public const string ComprasRequisicionesEliminar                   = "compras.requisiciones.eliminar";
    public const string ComprasRequisicionesCancelar                   = "compras.requisiciones.cancelar";
    public const string ComprasRequisicionesCerrarManual               = "compras.requisiciones.cerrar-manual";
    public const string ComprasRequisicionesAutorizarNivel1            = "compras.requisiciones.autorizar-nivel1";
    public const string ComprasRequisicionesAutorizarNivel2            = "compras.requisiciones.autorizar-nivel2";
    public const string ComprasRequisicionesRechazar                   = "compras.requisiciones.rechazar";
    public const string ComprasRequisicionesEditarDeOtrosUsuarios      = "compras.requisiciones.editar-de-otros-usuarios";
    public const string ComprasRequisicionesSeleccionarRequisitante    = "compras.requisiciones.seleccionar-requisitante";
    public const string ComprasRequisicionesVerTodosDepartamentos      = "compras.requisiciones.ver-todos-departamentos";

    // ----- Compras — Administración de aprobadores (F9-PR1) -----
    public const string ComprasAprobadoresAdministrar                  = "compras.aprobadores.administrar";

    // ----- Compras — Órdenes de Compra (OC F0-PR1) -----
    // Namespace GUID 00000003-0003-* (Compras módulo, recurso 0003).
    // El 01-diseño §9 listaba "namespace 00000004-*" pero ese ya está
    // ocupado por Compartido (ver más abajo); 00000003-0003-* mantiene
    // OC dentro de la familia Compras (Requisiciones=0001,
    // Aprobadores=0002, OrdenesCompra=0003). Anotar como hallazgo de
    // implementación cuando se actualice el diseño.
    //
    // Códigos en kebab-case 3 partes para ser compatibles con el seed
    // parser de IdentidadDbContext (Split('.') asume 3 partes para
    // alimentar columnas modulo/recurso/accion). Doc original usaba
    // "compras.ordenes.autorizar.nivel1" (4 partes), reescrito a
    // "autorizar-nivel1" siguiendo la convención ya establecida por RQ.
    public const string ComprasOrdenesLeer                             = "compras.ordenes.leer";
    public const string ComprasOrdenesCrear                            = "compras.ordenes.crear";
    public const string ComprasOrdenesCrearSinRq                       = "compras.ordenes.crear-sin-rq";
    public const string ComprasOrdenesAdjuntar                         = "compras.ordenes.adjuntar";
    public const string ComprasOrdenesLogistica                        = "compras.ordenes.logistica";
    public const string ComprasOrdenesAutorizarNivel1                  = "compras.ordenes.autorizar-nivel1";
    public const string ComprasOrdenesAutorizarNivel2                  = "compras.ordenes.autorizar-nivel2";
    public const string ComprasOrdenesCancelar                         = "compras.ordenes.cancelar";
    public const string ComprasOrdenesCancelarDoble                    = "compras.ordenes.cancelar-doble";
    public const string ComprasOrdenesReportesPartidasAbiertas         = "compras.ordenes.reportes-partidas-abiertas";
    public const string ComprasOrdenesCerrarManual                     = "compras.ordenes.cerrar-manual";

    // ----- Configuración del módulo Compras (Settings, decisión 2026-05-13) -----
    public const string ComprasConfiguracionLeer                       = "compras.configuracion.leer";
    public const string ComprasConfiguracionEditar                     = "compras.configuracion.editar";

    // ----- Compartido — catálogos cross-empresa (F7-PR1, F9-PR1) -----
    public const string CompartidoCatalogosLeer                        = "compartido.catalogos.leer";
    public const string CompartidoCatalogosAdministrar                 = "compartido.catalogos.administrar";

    // ----- Catálogos granulares (F-Admin-PR5.x: Monedas/TiposCambio/CondicionesPago/Incoterms/Transportistas/UnidadesMedida) -----
    // Reusan namespace GUID 00000004-* (Compartido). Convención kebab-case
    // 3-parte para tipos-cambio y unidades-medida (el seed parser de
    // IdentidadDbContext hace Codigo.Split('.') y espera exactamente 3
    // segmentos modulo/recurso/accion).
    public const string CatalogosMonedasGestionar                      = "catalogos.monedas.gestionar";
    public const string CatalogosTiposCambioGestionar                  = "catalogos.tipos-cambio.gestionar";
    public const string CatalogosCondicionesPagoGestionar              = "catalogos.condiciones-pago.gestionar";
    public const string CatalogosIncotermsGestionar                    = "catalogos.incoterms.gestionar";
    public const string CatalogosTransportistasGestionar               = "catalogos.transportistas.gestionar";
    public const string CatalogosUnidadesMedidaGestionar               = "catalogos.unidades-medida.gestionar";

    // ----- Datos Maestros granulares (F-Admin-PR4.5: Proveedores/Articulos) -----
    // Reemplaza al grueso `compartido.catalogos.administrar` para los
    // recursos de Datos Maestros (Proveedores, Articulos). El grueso sigue
    // existiendo por compatibilidad; cuando UI migre a granulares, el
    // grueso quedará para reclasificar-naturaleza bulk.
    public const string DatosMaestrosProveedoresGestionar              = "datos_maestros.proveedores.gestionar";
    public const string DatosMaestrosArticulosGestionar                = "datos_maestros.articulos.gestionar";
    // ADR-0048: masters nuevos para la ingesta de pedidos A+W → Facturación.
    // Clientes (D6) y ProductoAw (D5, master de venta separado de articulos).
    public const string DatosMaestrosClientesGestionar                 = "datos_maestros.clientes.gestionar";
    public const string DatosMaestrosProductosAwGestionar              = "datos_maestros.productos-aw.gestionar";

    // ----- Administración — andamio mínimo del área /admin (F-Admin-PR1.2) -----
    // Namespace GUID 00000005-* reservado para el módulo Administración.
    // Los permisos completos del área (sucursales, departamentos, series,
    // parámetros, etc.) llegan con cada feature PR. Aquí solo los mínimos
    // que el shell del área necesita: si el usuario tiene alguno de estos,
    // el engrane del topbar es visible y el landing /admin renderiza al
    // menos una card. Ver ADR-0034 y 01-diseno §6.3 de Administración.
    public const string AdminEmpresasLeer                              = "admin.empresas.leer";
    public const string AdminAuditoriaLeer                             = "admin.auditoria.leer";

    // ----- Administración — Empresas + Sucursales + Departamentos (F-Admin-PR2.3) -----
    // CRUD completo del catálogo organizacional. Convención kebab-case
    // 3-parte para sucursales-gestionar y departamentos.gestionar (el
    // seed parser de IdentidadDbContext hace Codigo.Split('.') y espera
    // exactamente 3 segmentos modulo/recurso/accion).
    public const string AdminEmpresasCrear                             = "admin.empresas.crear";
    public const string AdminEmpresasEditar                            = "admin.empresas.editar";
    public const string AdminEmpresasDesactivar                        = "admin.empresas.desactivar";
    public const string AdminEmpresasSucursalesGestionar               = "admin.empresas.sucursales-gestionar";
    public const string AdminDepartamentosLeer                         = "admin.departamentos.leer";
    public const string AdminDepartamentosGestionar                    = "admin.departamentos.gestionar";
    // PR-A1: asignación N:M Sucursal ↔ Departamento. Gobierna qué deptos
    // operan en qué sucursal; consumido por Compras en PR-A2 para validar
    // captura de RQs. Sub-namespace 00000005-0006-* reservado para
    // "Admin Sucursales" como recurso top-level (la CRUD de Sucursal hoy
    // vive bajo admin.empresas.* por legacy de F-Admin-PR2.3).
    public const string AdminSucursalesDepartamentosGestionar          = "admin.sucursales.departamentos-gestionar";
    // F1-ADM-01 Fase 2: análogos para las asignaciones N:M Sucursal ↔
    // Puesto y Usuario ↔ Sucursal. Mismo sub-namespace 00000005-0006-*
    // (recurso "Admin Sucursales"), secuencias 2 y 3. La lectura del
    // N:M reusa `compartido.catalogos.leer` (no hay -leer específico),
    // igual que AdminSucursalesDepartamentosGestionar. Estos mismos
    // permisos son el "bypass admin" del guard de pertenencia a
    // sucursal (SucursalScopeGuard, ver Compartido/Administracion/Abstractions).
    public const string AdminSucursalesPuestosGestionar                = "admin.sucursales.puestos-gestionar";
    public const string AdminSucursalesUsuariosGestionar               = "admin.sucursales.usuarios-gestionar";

    // ----- Administración — Puestos y Empleados (ADM-PR1) -----
    // Master organizacional para reglas de negocio por persona (doc
    // 10-catalogo-puestos-empleados): políticas de viáticos por puesto,
    // jefe directo como autorizador N1. Sub-namespaces 00000005-0007-*
    // (puestos) y 00000005-0008-* (empleados).
    public const string AdminPuestosGestionar                          = "admin.puestos.gestionar";
    public const string AdminPuestosLeer                                = "admin.puestos.leer";
    public const string AdminEmpleadosGestionar                        = "admin.empleados.gestionar";
    public const string AdminEmpleadosLeerTodasSucursales              = "admin.empleados.leer-todas-sucursales";
    public const string AdminEmpleadosGestionarTodasSucursales         = "admin.empleados.gestionar-todas-sucursales";

    // ----- Administración — Series y Folios (F-Admin-PR6.1) -----
    // Permiso transversal para todo el CRUD de series; la reserva del
    // folio (POST /api/v1/admin/series/reservar) requiere solo
    // autenticación porque es invocada por handlers internos (Compras
    // OC, futuras facturación/contabilidad).
    public const string AdminSeriesGestionar                           = "admin.series.gestionar";

    // ----- Parámetros globales del sistema (F-Admin-PR7.1) -----
    public const string AdminParametrosLeer                            = "admin.parametros.leer";
    public const string AdminParametrosEditar                          = "admin.parametros.editar";

    // ----- Almacén (F0-PR1) -----
    // Namespace GUID 00000008-* reservado para el módulo Almacén.
    // (00000007-* lo toma CxP en su F0-PR1, mergeado en paralelo.)
    // Sub-namespaces por recurso: -0001 almacenes, -0002 entradas,
    // -0003 salidas, -0004 devoluciones-internas, -0005 devoluciones-proveedor,
    // -0006 inventarios, -0007 ajustes, -0008 reportes, -0009 cierre-mes,
    // -000a lectura (auditor externo), -000b reservas (A19).
    // Convención kebab-case 3-parte (el seed parser de IdentidadDbContext
    // hace Codigo.Split('.') y espera exactamente 3 segmentos
    // modulo/recurso/accion). El doc 01-diseno §10 listaba algunos con
    // 4 partes (`almacen.salidas.read.propias`) y otros con underscore;
    // aquí se normalizan a 3-parte con kebab.
    public const string AlmacenAlmacenesRead                           = "almacen.almacenes.leer";
    public const string AlmacenAlmacenesAdministrar                    = "almacen.almacenes.administrar";

    public const string AlmacenEntradasLeer                            = "almacen.entradas.leer";
    public const string AlmacenEntradasCapturar                        = "almacen.entradas.capturar";
    public const string AlmacenEntradasRegistrar                       = "almacen.entradas.registrar";
    public const string AlmacenEntradasCancelarBorrador                = "almacen.entradas.cancelar-borrador";

    public const string AlmacenSalidasLeerPropias                      = "almacen.salidas.leer-propias";
    public const string AlmacenSalidasLeerTodas                        = "almacen.salidas.leer-todas";
    public const string AlmacenSalidasCapturar                         = "almacen.salidas.capturar";
    public const string AlmacenSalidasRegistrar                        = "almacen.salidas.registrar";
    public const string AlmacenSalidasPorVale                          = "almacen.salidas.por-vale";

    public const string AlmacenDevolucionesInternasLeer                = "almacen.devoluciones-internas.leer";
    public const string AlmacenDevolucionesInternasCapturar            = "almacen.devoluciones-internas.capturar";

    public const string AlmacenDevolucionesProveedorIniciar            = "almacen.devoluciones-proveedor.iniciar";
    public const string AlmacenDevolucionesProveedorAutorizar          = "almacen.devoluciones-proveedor.autorizar";
    public const string AlmacenDevolucionesProveedorRegistrar          = "almacen.devoluciones-proveedor.registrar";

    public const string AlmacenInventariosLeer                         = "almacen.inventarios.leer";
    public const string AlmacenInventariosCrear                        = "almacen.inventarios.crear";
    public const string AlmacenInventariosCapturar                     = "almacen.inventarios.capturar";
    public const string AlmacenInventariosAprobarNivel1                = "almacen.inventarios.aprobar-nivel1";
    public const string AlmacenInventariosAprobarNivel2                = "almacen.inventarios.aprobar-nivel2";
    public const string AlmacenInventariosAprobarNivel3                = "almacen.inventarios.aprobar-nivel3";

    public const string AlmacenAjustesManual                           = "almacen.ajustes.manual";

    public const string AlmacenReportesAlfak                           = "almacen.reportes.alfak";
    public const string AlmacenReportesMpCnk                           = "almacen.reportes.mp-cnk";
    public const string AlmacenReportesMovimientos                     = "almacen.reportes.movimientos";

    public const string AlmacenCierreMesEjecutar                       = "almacen.cierre-mes.ejecutar";

    public const string AlmacenLecturaTotal                            = "almacen.lectura.total";

    public const string AlmacenReservasAdministrar                     = "almacen.reservas.administrar";

    // -000c asignaciones artículo→ubicación (OITW, ADR-0047 PR3).
    public const string AlmacenAsignacionesRead                        = "almacen.asignaciones.leer";
    public const string AlmacenAsignacionesAdministrar                 = "almacen.asignaciones.administrar";

    // -000d configuración de reorden N1/N2 (ADR-0047 PR5.A).
    public const string AlmacenReordenRead                             = "almacen.reorden.leer";
    public const string AlmacenReordenAdministrar                      = "almacen.reorden.administrar";

    // -000e ubicaciones físicas N4 (racks/pasillos, ADR-0047 PR C7.1).
    public const string AlmacenUbicacionesRead                         = "almacen.ubicaciones.leer";
    public const string AlmacenUbicacionesAdministrar                  = "almacen.ubicaciones.administrar";

    // ----- Cuentas por Pagar (F0-PR1) -----
    // Namespace GUID 00000007-* reservado para el módulo Cuentas por Pagar.
    // Sub-namespaces por recurso: -0001 facturas, -0002 notas-credito,
    // -0003 notas-cargo, -0004 anticipos, -0005 comprobaciones, -0006 tc,
    // -0007 proveedores, -0008 reportes, -0009 cfdis.
    // Convención kebab-case 3-parte (el seed parser de IdentidadDbContext
    // hace Codigo.Split('.') y espera exactamente 3 segmentos
    // modulo/recurso/accion). El "modulo" se llama `cuentas_por_pagar`
    // (con underscore) — alineado a `datos_maestros.*`.
    public const string CuentasPorPagarFacturasLeer                    = "cuentas_por_pagar.facturas.leer";
    public const string CuentasPorPagarFacturasCapturar                = "cuentas_por_pagar.facturas.capturar";
    public const string CuentasPorPagarFacturasEditar                  = "cuentas_por_pagar.facturas.editar";
    public const string CuentasPorPagarFacturasCancelar                = "cuentas_por_pagar.facturas.cancelar";
    public const string CuentasPorPagarFacturasEnviarRevision          = "cuentas_por_pagar.facturas.enviar-revision";
    public const string CuentasPorPagarFacturasLiberarRevision         = "cuentas_por_pagar.facturas.liberar-revision";
    public const string CuentasPorPagarFacturasAutorizar               = "cuentas_por_pagar.facturas.autorizar";

    public const string CuentasPorPagarNotasCreditoLeer                = "cuentas_por_pagar.notas-credito.leer";
    public const string CuentasPorPagarNotasCreditoCapturar            = "cuentas_por_pagar.notas-credito.capturar";

    public const string CuentasPorPagarNotasCargoLeer                  = "cuentas_por_pagar.notas-cargo.leer";
    public const string CuentasPorPagarNotasCargoCrear                 = "cuentas_por_pagar.notas-cargo.crear";
    public const string CuentasPorPagarNotasCargoAutorizar             = "cuentas_por_pagar.notas-cargo.autorizar";
    public const string CuentasPorPagarNotasCargoAplicar               = "cuentas_por_pagar.notas-cargo.aplicar";

    public const string CuentasPorPagarAnticiposLeer                   = "cuentas_por_pagar.anticipos.leer";
    public const string CuentasPorPagarAnticiposCapturar               = "cuentas_por_pagar.anticipos.capturar";

    public const string CuentasPorPagarComprobacionesLeer              = "cuentas_por_pagar.comprobaciones.leer";
    public const string CuentasPorPagarComprobacionesCapturar          = "cuentas_por_pagar.comprobaciones.capturar";
    public const string CuentasPorPagarComprobacionesAprobarNivel1     = "cuentas_por_pagar.comprobaciones.aprobar-nivel1";
    public const string CuentasPorPagarComprobacionesAprobarNivel2     = "cuentas_por_pagar.comprobaciones.aprobar-nivel2";

    // ----- F7-PR3: Viáticos electrónicos + catálogos (aprobadores_limites, politicas_viaticos) -----
    public const string CuentasPorPagarViaticosLeer                    = "cuentas_por_pagar.viaticos.leer";
    public const string CuentasPorPagarViaticosSolicitar               = "cuentas_por_pagar.viaticos.solicitar";
    public const string CuentasPorPagarViaticosAutorizarJefe           = "cuentas_por_pagar.viaticos.autorizar-jefe";
    public const string CuentasPorPagarViaticosAutorizarDf             = "cuentas_por_pagar.viaticos.autorizar-df";
    public const string CuentasPorPagarViaticosMarcarPagado            = "cuentas_por_pagar.viaticos.marcar-pagado";
    public const string CuentasPorPagarViaticosCapturarComprobacion    = "cuentas_por_pagar.viaticos.capturar-comprobacion";
    public const string CuentasPorPagarViaticosLiberar                 = "cuentas_por_pagar.viaticos.liberar";

    public const string CuentasPorPagarCatalogosAprobadoresAdministrar = "cuentas_por_pagar.catalogos.aprobadores.administrar";
    public const string CuentasPorPagarCatalogosPoliticasAdministrar   = "cuentas_por_pagar.catalogos.politicas-viaticos.administrar";

    public const string CuentasPorPagarTcLeer                          = "cuentas_por_pagar.tc.leer";
    public const string CuentasPorPagarTcRegistrarMovimiento           = "cuentas_por_pagar.tc.registrar-movimiento";
    public const string CuentasPorPagarTcCerrarEstadoCuenta            = "cuentas_por_pagar.tc.cerrar-estado-cuenta";
    public const string CuentasPorPagarTcAdministrar                   = "cuentas_por_pagar.tc.administrar"; // F7-PR4: CRUD tarjetas + usuarios
    public const string CuentasPorPagarTcDisputar                      = "cuentas_por_pagar.tc.disputar";   // F7-PR6: marcar/resolver disputa

    public const string CuentasPorPagarProveedoresPonerRevision        = "cuentas_por_pagar.proveedores.poner-revision";
    public const string CuentasPorPagarProveedoresLiberarRevision      = "cuentas_por_pagar.proveedores.liberar-revision";
    public const string CuentasPorPagarProveedoresAjustarTolerancia    = "cuentas_por_pagar.proveedores.ajustar-tolerancia";

    public const string CuentasPorPagarReportesCartera                 = "cuentas_por_pagar.reportes.cartera";
    public const string CuentasPorPagarReportesAntiguedad              = "cuentas_por_pagar.reportes.antiguedad";
    public const string CuentasPorPagarReportesDiot                    = "cuentas_por_pagar.reportes.diot";
    public const string CuentasPorPagarReportesTc                      = "cuentas_por_pagar.reportes.tc";

    public const string CuentasPorPagarCfdisLeer                       = "cuentas_por_pagar.cfdis.leer";
    public const string CuentasPorPagarCfdisCargarManual               = "cuentas_por_pagar.cfdis.cargar-manual";
    public const string CuentasPorPagarCfdisDescartar                  = "cuentas_por_pagar.cfdis.descartar";

    public const string CuentasPorPagarReposicionesLeer                = "cuentas_por_pagar.reposiciones.leer";
    public const string CuentasPorPagarReposicionesAdministrar         = "cuentas_por_pagar.reposiciones.administrar";

    // ----- Integración A+W (PR A — usuario de servicio + Glass Agent SPN) -----
    // Namespace GUID 00000006-* reservado para el módulo Integraciones.Aw.
    // Sub-namespaces: -0001 cotizaciones, -0002 pedidos, -0003 clientes,
    // -0004 articulos, -0005 inventario, -0006 administracion del módulo.
    // Estas son las 9 claves canónicas listadas en
    // docs/integration/01-api-contract.md §3.6. Glass Agent en fase 1
    // recibe únicamente CotizacionesCrear + CotizacionesConsultar; las
    // demás están definidas para que cuando aparezcan los endpoints en
    // PR D no requieran nueva migration. Convención **flat** alineada al
    // resto del archivo (el doc espejo proponía sub-clase nested; se
    // descarta para mantener una sola convención de naming en el repo).
    public const string IntegracionesAwCotizacionesCrear               = "integraciones.aw.cotizaciones.crear";
    public const string IntegracionesAwCotizacionesConsultar           = "integraciones.aw.cotizaciones.consultar";
    public const string IntegracionesAwCotizacionesReintentar          = "integraciones.aw.cotizaciones.reintentar";
    public const string IntegracionesAwPedidosConsultar                = "integraciones.aw.pedidos.consultar";
    public const string IntegracionesAwClientesConsultar               = "integraciones.aw.clientes.consultar";
    public const string IntegracionesAwArticulosConsultar              = "integraciones.aw.articulos.consultar";
    public const string IntegracionesAwInventarioConsultar             = "integraciones.aw.inventario.consultar";
    public const string IntegracionesAwAdministracionServicios         = "integraciones.aw.administracion.servicios";
    public const string IntegracionesAwAdministracionConfiguracion     = "integraciones.aw.administracion.configuracion";

    // Integraciones.Fiscal (PR-2 foundation). Cubre configuración del PAC
    // (FiscalAPI) + RFCs receptores. Cuando arranque Facturación se agregan
    // permisos para timbrado / cancelación / CSD (ver levantamiento §3 fase 2).
    public const string IntegracionesFiscalLeer                        = "integraciones.fiscal.leer";
    public const string IntegracionesFiscalAdministrar                 = "integraciones.fiscal.administrar";

    // ----- Facturación (F0-PR1) -----
    // Namespace GUID 00000009-* reservado para el módulo Facturación (CFDI 4.0).
    // Sub-namespaces por recurso: -0001 pedidos, -0002 facturas, -0003 anticipos,
    // -0004 notas-credito, -0005 carta-porte, -0006 repp, -0007 cancelaciones,
    // -0008 activos, -0009 caja, -000a reportes.
    // Convención kebab-case 3-parte (el seed parser de IdentidadDbContext hace
    // Codigo.Split('.') y espera exactamente 3 segmentos modulo/recurso/accion).
    // El 01-diseño §10 listaba algunos con 4 partes
    // (`facturacion.pedidos.excepciones.resolver`,
    // `facturacion.notas_credito.bonificacion.emitir`); aquí se normalizan a
    // 3-parte con kebab y recurso `notas-credito` (alineado a CxP). Anotar como
    // hallazgo de implementación cuando se actualice el diseño.
    public const string FacturacionPedidosImportar             = "facturacion.pedidos.importar";
    public const string FacturacionPedidosCapturar             = "facturacion.pedidos.capturar";
    public const string FacturacionPedidosExcepcionesResolver  = "facturacion.pedidos.excepciones-resolver";
    public const string FacturacionFacturasEmitir              = "facturacion.facturas.emitir";
    public const string FacturacionFacturasLeer                = "facturacion.facturas.leer";
    // Detallado facturación pt. 2: el receptor viene fijo del master de
    // clientes y es de solo lectura en la emisión; este permiso controla
    // el CTA "Corregir en catálogo" hacia Datos Maestros → Clientes.
    public const string FacturacionFacturasEditarReceptor      = "facturacion.facturas.editar-receptor";
    // Detallado facturación pt. 3: los datos fiscales del artículo por
    // posición (claves SAT, objeto imp., tasas) también son de solo
    // lectura; este permiso controla el CTA hacia Datos Maestros →
    // Productos A+W. Es independiente del de receptor a propósito.
    public const string FacturacionFacturasEditarArticulo      = "facturacion.facturas.editar-articulo";
    public const string FacturacionAnticiposEmitir             = "facturacion.anticipos.emitir";
    public const string FacturacionAnticiposVincular           = "facturacion.anticipos.vincular";
    public const string FacturacionAnticiposLeer               = "facturacion.anticipos.leer";
    public const string FacturacionNotasCreditoBonificacion    = "facturacion.notas-credito.bonificacion-emitir";
    public const string FacturacionNotasCreditoLeer            = "facturacion.notas-credito.leer";
    public const string FacturacionCartaPorteEmitir            = "facturacion.carta-porte.emitir";
    public const string FacturacionCartaPorteLeer              = "facturacion.carta-porte.leer";
    public const string FacturacionReppEmitir                  = "facturacion.repp.emitir";
    public const string FacturacionCancelacionesSolicitar      = "facturacion.cancelaciones.solicitar";
    public const string FacturacionCancelacionesConsultar      = "facturacion.cancelaciones.consultar";
    public const string FacturacionActivosAutorizar            = "facturacion.activos.autorizar";
    public const string FacturacionCajaLiquidar                = "facturacion.caja.liquidar";
    // CAJAS-PR1 (12-cajas.md §8): resto del sub-namespace -0009 caja.
    // `liquidar` (existente) queda remapeado al cierre de sesión con arqueo.
    public const string FacturacionCajaAdministrar             = "facturacion.caja.administrar";
    public const string FacturacionCajaOperar                  = "facturacion.caja.operar";
    public const string FacturacionCajaSupervisar              = "facturacion.caja.supervisar";
    public const string FacturacionCajaLeerTodas               = "facturacion.caja.leer-todas";
    public const string FacturacionReportesLeer                = "facturacion.reportes.leer";
    public const string FacturacionComprobantesReintentarTimbrado = "facturacion.comprobantes.reintentar-timbrado";
    public const string FacturacionComprobantesDescartar        = "facturacion.comprobantes.descartar";

    // ----- Cuentas por Cobrar (CXC-PR1) -----
    // Namespace GUID 0000000a-* reservado para el módulo Cuentas por Cobrar.
    // Sub-namespaces por recurso (01-diseño §10): -0001 lineas-credito,
    // -0002 liberacion, -0003 cobranza, -0004 cartera/reportes,
    // -0005 aplicacion-pago. Se seedea el set completo del diseño en PR-1
    // (mismo criterio que Facturación F0-PR1) para no generar una migration
    // de IdentidadDbContext por cada PR del módulo.
    public const string CuentasPorCobrarLineasCreditoLeer       = "cuentas_por_cobrar.lineas-credito.leer";
    public const string CuentasPorCobrarLineasCreditoGestionar  = "cuentas_por_cobrar.lineas-credito.gestionar";
    public const string CuentasPorCobrarLiberacionDecidir       = "cuentas_por_cobrar.liberacion.decidir";
    public const string CuentasPorCobrarLiberacionOverride      = "cuentas_por_cobrar.liberacion.override";
    public const string CuentasPorCobrarCobranzaRegistrar       = "cuentas_por_cobrar.cobranza.registrar";
    public const string CuentasPorCobrarCarteraLeer             = "cuentas_por_cobrar.cartera.leer";
    public const string CuentasPorCobrarAplicacionPagoProponer  = "cuentas_por_cobrar.aplicacion-pago.proponer";
    public const string CuentasPorCobrarAplicacionPagoConfirmar = "cuentas_por_cobrar.aplicacion-pago.confirmar";

    // ----- Tesorería / Bancos (TES-PR1) -----
    // Namespace GUID 0000000b-* reservado para el módulo Tesorería.
    // Sub-namespaces por recurso (01-diseño §10): -0001 cuentas,
    // -0002 movimientos, -0003 pagos, -0004 pagos-cuenta, -0005 corridas,
    // -0006 depositos, -0007 repp, -0008 conciliacion, -0009 pasivos,
    // -000a reportes. Se seedea el set completo del diseño en PR-1 (mismo
    // criterio que Facturación F0-PR1 y CxC CXC-PR1) para no generar una
    // migration de IdentidadDbContext por cada PR del módulo. Convención
    // kebab-case 3-parte (el seed parser hace Codigo.Split('.')).
    public const string TesoreriaCuentasVer                     = "tesoreria.cuentas.ver";
    // TES-7 revisada (cuentas-crud): el CRUD del catálogo de cuentas dejó
    // de estar diferido a Administración; alta/edición/toggle en Tesorería.
    public const string TesoreriaCuentasAdministrar             = "tesoreria.cuentas.administrar";
    public const string TesoreriaMovimientosVer                 = "tesoreria.movimientos.ver";
    public const string TesoreriaMovimientosRegistrar           = "tesoreria.movimientos.registrar";
    public const string TesoreriaMovimientosVerCuentaCompleta   = "tesoreria.movimientos.ver-cuenta-completa";
    public const string TesoreriaPagosAplicar                   = "tesoreria.pagos.aplicar";
    public const string TesoreriaPagosRevertir                  = "tesoreria.pagos.revertir";
    public const string TesoreriaPagosCuentaRegistrar           = "tesoreria.pagos-cuenta.registrar";
    public const string TesoreriaPagosCuentaLigar               = "tesoreria.pagos-cuenta.ligar";
    public const string TesoreriaCorridasCrear                  = "tesoreria.corridas.crear";
    public const string TesoreriaCorridasAutorizar              = "tesoreria.corridas.autorizar";
    public const string TesoreriaCorridasEjecutar               = "tesoreria.corridas.ejecutar";
    public const string TesoreriaDepositosConfirmar             = "tesoreria.depositos.confirmar";
    public const string TesoreriaDepositosRechazar              = "tesoreria.depositos.rechazar";
    public const string TesoreriaReppRegistrar                  = "tesoreria.repp.registrar";
    public const string TesoreriaConciliacionOperar             = "tesoreria.conciliacion.operar";
    public const string TesoreriaConciliacionCerrar             = "tesoreria.conciliacion.cerrar";
    public const string TesoreriaPasivosSolicitarCancelacion    = "tesoreria.pasivos.solicitar-cancelacion";
    // TES-PR3: lectura de la bandeja de pasivos pendientes. El 01-diseño §10
    // no lo listaba (hallazgo de implementación): consultar la bandeja no
    // debe exigir el permiso de ejecución `tesoreria.pagos.aplicar`.
    public const string TesoreriaPasivosVer                     = "tesoreria.pasivos.ver";
    public const string TesoreriaReportesVer                    = "tesoreria.reportes.ver";

    // ----- Centros de Costo (CECO-A1; modelo Dim desde CECO-PR4) -----
    // Namespace GUID 0000000c-* reservado para el módulo Centros de Costo.
    // Sub-namespaces por recurso: -0001 catalogo, -0002 asignaciones,
    // -0003 dim3. `dim3.leer-todos` es el bypass de alcance (Contabilidad
    // ve todas las máquinas sin asignación), mismo rol que
    // `facturacion.caja.leer-todas`. Renombrado de `equipos.leer-todos` en
    // CECO-PR4 vía UpdateData — el GUID quedó INTACTO (las asignaciones de
    // rol sobreviven).
    public const string CentrosCostoCatalogoLeer                = "centros_costo.catalogo.leer";
    public const string CentrosCostoCatalogoAdministrar         = "centros_costo.catalogo.administrar";
    public const string CentrosCostoAsignacionesAdministrar     = "centros_costo.asignaciones.administrar";
    public const string CentrosCostoDim3LeerTodos               = "centros_costo.dim3.leer-todos";

    /// <summary>
    /// Catálogo completo: <c>(Id determinista, Codigo, Descripcion)</c>.
    /// Usado por la migration de seed y por el bootstrap del SuperAdmin
    /// para asignarle todos los permisos automáticamente.
    /// </summary>
    public static IReadOnlyList<(Guid Id, string Codigo, string Descripcion)> Todos { get; } = new[]
    {
        (Guid.Parse("00000002-0001-0000-0000-000000000001"), InfraHealthLeer,                              "Leer health checks del sistema"),
        (Guid.Parse("00000002-0001-0000-0000-000000000002"), InfraAuditLogLeer,                            "Leer el log de auditoría de cualquier módulo"),
        (Guid.Parse("00000002-0002-0000-0000-000000000001"), IdentidadUsuariosLeer,                        "Listar y consultar usuarios"),
        (Guid.Parse("00000002-0002-0000-0000-000000000002"), IdentidadUsuariosCrear,                       "Crear nuevos usuarios"),
        (Guid.Parse("00000002-0002-0000-0000-000000000003"), IdentidadUsuariosEditar,                      "Editar perfil y estado de usuarios"),
        (Guid.Parse("00000002-0002-0000-0000-00000000000d"), IdentidadUsuariosDesactivar,                  "Desactivar y reactivar usuarios (soft-delete)"),
        (Guid.Parse("00000002-0002-0000-0000-000000000004"), IdentidadRolesLeer,                           "Listar y consultar roles"),
        (Guid.Parse("00000002-0002-0000-0000-000000000005"), IdentidadRolesAdministrar,                    "Crear, editar y desactivar roles"),
        (Guid.Parse("00000002-0002-0000-0000-000000000006"), IdentidadAsignacionesLeer,                    "Consultar asignaciones de roles a usuarios por empresa"),
        (Guid.Parse("00000002-0002-0000-0000-000000000007"), IdentidadAsignacionesAdministrar,             "Asignar y revocar roles a usuarios por empresa"),
        (Guid.Parse("00000002-0002-0000-0000-000000000008"), IdentidadRolesCrear,                          "Crear roles nuevos (granular, reemplaza al grueso 'administrar')"),
        (Guid.Parse("00000002-0002-0000-0000-000000000009"), IdentidadRolesEditar,                         "Editar metadatos de roles (nombre, descripción)"),
        (Guid.Parse("00000002-0002-0000-0000-00000000000a"), IdentidadRolesEliminar,                       "Desactivar roles (soft-delete, no aplica a roles del sistema)"),
        (Guid.Parse("00000002-0002-0000-0000-00000000000b"), IdentidadRolesAsignarPermisos,                "Asignar la matriz de permisos a un rol (batch atómico)"),
        (Guid.Parse("00000002-0002-0000-0000-00000000000c"), IdentidadRolesGruposEntraIdGestionar,         "Asociar y desasociar grupos de Microsoft Entra ID a un rol"),
        (Guid.Parse("00000002-0003-0000-0000-000000000001"), IdentidadPermisosLeer,                        "Leer el catálogo canónico de permisos del sistema"),
        (Guid.Parse("00000003-0001-0000-0000-000000000001"), ComprasRequisicionesLeer,                     "Consultar requisiciones (detalle y listados)"),
        (Guid.Parse("00000003-0001-0000-0000-000000000002"), ComprasRequisicionesCrear,                    "Crear requisiciones nuevas"),
        (Guid.Parse("00000003-0001-0000-0000-000000000003"), ComprasRequisicionesEditar,                   "Editar requisiciones propias en estado Borrador"),
        (Guid.Parse("00000003-0001-0000-0000-000000000004"), ComprasRequisicionesEliminar,                 "Eliminar requisiciones pre-autorización"),
        (Guid.Parse("00000003-0001-0000-0000-000000000005"), ComprasRequisicionesCancelar,                 "Cancelar requisiciones post-autorización"),
        (Guid.Parse("00000003-0001-0000-0000-000000000006"), ComprasRequisicionesAutorizarNivel1,          "Autorizar requisiciones en nivel 1 (cubre inicial y saldo)"),
        (Guid.Parse("00000003-0001-0000-0000-000000000007"), ComprasRequisicionesAutorizarNivel2,          "Autorizar requisiciones en nivel 2 (cubre inicial y saldo)"),
        (Guid.Parse("00000003-0001-0000-0000-000000000008"), ComprasRequisicionesRechazar,                 "Rechazar requisiciones desde el flujo de autorización"),
        (Guid.Parse("00000003-0001-0000-0000-000000000009"), ComprasRequisicionesEditarDeOtrosUsuarios,    "Editar requisiciones creadas por otros usuarios"),
        (Guid.Parse("00000003-0001-0000-0000-00000000000a"), ComprasRequisicionesSeleccionarRequisitante,  "Crear requisiciones a nombre de otro usuario (delegación)"),
        (Guid.Parse("00000003-0001-0000-0000-00000000000b"), ComprasRequisicionesVerTodosDepartamentos,    "Consultar requisiciones de cualquier departamento (sin restricción)"),
        (Guid.Parse("00000003-0001-0000-0000-00000000000c"), ComprasRequisicionesCerrarManual,             "Cerrar manualmente requisiciones no surtidas o surtidas parcialmente (ADR-0043; jefe de almacén / almacenista)"),
        (Guid.Parse("00000003-0002-0000-0000-000000000001"), ComprasAprobadoresAdministrar,                "Designar y revocar aprobadores por departamento (JefeDpto, JefeAlmacen, AutorizadorN2)"),
        (Guid.Parse("00000003-0003-0000-0000-000000000001"), ComprasOrdenesLeer,                           "Consultar órdenes de compra (detalle y listados)"),
        (Guid.Parse("00000003-0003-0000-0000-000000000002"), ComprasOrdenesCrear,                          "Crear órdenes de compra (desde RQ o consolidación)"),
        (Guid.Parse("00000003-0003-0000-0000-000000000003"), ComprasOrdenesCrearSinRq,                     "Crear órdenes de compra sin requisición previa (FOC11)"),
        (Guid.Parse("00000003-0003-0000-0000-000000000004"), ComprasOrdenesAdjuntar,                       "Adjuntar y remover documentos de una orden de compra"),
        (Guid.Parse("00000003-0003-0000-0000-000000000005"), ComprasOrdenesLogistica,                      "Editar información logística (transportista, guía, contenedor) post-autorización"),
        (Guid.Parse("00000003-0003-0000-0000-000000000006"), ComprasOrdenesAutorizarNivel1,                "Autorizar órdenes de compra en nivel 1 (Jefe de Compras)"),
        (Guid.Parse("00000003-0003-0000-0000-000000000007"), ComprasOrdenesAutorizarNivel2,                "Autorizar órdenes de compra en nivel 2 (Dirección)"),
        (Guid.Parse("00000003-0003-0000-0000-000000000008"), ComprasOrdenesCancelar,                       "Cancelar órdenes de compra sin recepciones"),
        (Guid.Parse("00000003-0003-0000-0000-000000000009"), ComprasOrdenesCancelarDoble,                  "Cancelar órdenes de compra con recepciones parciales (doble firma)"),
        (Guid.Parse("00000003-0003-0000-0000-00000000000a"), ComprasOrdenesReportesPartidasAbiertas,       "Consultar reporte de partidas abiertas de órdenes de compra"),
        (Guid.Parse("00000003-0003-0000-0000-00000000000b"), ComprasOrdenesCerrarManual,                   "Cerrar órdenes de compra manualmente (servicios/residuales)"),
        (Guid.Parse("00000003-0004-0000-0000-000000000001"), ComprasConfiguracionLeer,                     "Leer la configuración del módulo Compras de la empresa actual"),
        (Guid.Parse("00000003-0004-0000-0000-000000000002"), ComprasConfiguracionEditar,                   "Editar la configuración del módulo Compras de la empresa actual"),
        (Guid.Parse("00000004-0001-0000-0000-000000000001"), CompartidoCatalogosLeer,                      "Consultar catálogos cross-empresa (proveedores, artículos)"),
        (Guid.Parse("00000004-0002-0000-0000-000000000001"), CompartidoCatalogosAdministrar,               "Administrar catálogos cross-empresa (reclasificar naturaleza, etc)"),
        (Guid.Parse("00000005-0001-0000-0000-000000000001"), AdminEmpresasLeer,                            "Listar empresas y sus datos generales (mínimo del andamio /admin)"),
        (Guid.Parse("00000005-0001-0000-0000-000000000002"), AdminEmpresasCrear,                           "Crear nuevas empresas en el catálogo organizacional"),
        (Guid.Parse("00000005-0001-0000-0000-000000000003"), AdminEmpresasEditar,                          "Editar datos generales de empresas existentes"),
        (Guid.Parse("00000005-0001-0000-0000-000000000004"), AdminEmpresasDesactivar,                      "Desactivar empresas (sin sucursales activas)"),
        (Guid.Parse("00000005-0001-0000-0000-000000000005"), AdminEmpresasSucursalesGestionar,             "Crear, editar y desactivar sucursales del catálogo organizacional"),
        (Guid.Parse("00000005-0002-0000-0000-000000000001"), AdminAuditoriaLeer,                           "Consultar la bitácora consolidada de auditoría (mínimo del andamio /admin)"),
        (Guid.Parse("00000005-0003-0000-0000-000000000001"), AdminDepartamentosGestionar,                  "Crear, editar y desactivar departamentos del catálogo organizacional"),
        (Guid.Parse("00000005-0003-0000-0000-000000000002"), AdminDepartamentosLeer,                       "Consultar departamentos del catálogo organizacional"),
        (Guid.Parse("00000005-0004-0000-0000-000000000001"), AdminSeriesGestionar,                         "Crear, editar y desactivar series de folios cross-módulo (OC, CFDI, póliza)"),
        (Guid.Parse("00000005-0005-0000-0000-000000000001"), AdminParametrosLeer,                          "Leer parámetros globales del sistema"),
        (Guid.Parse("00000005-0005-0000-0000-000000000002"), AdminParametrosEditar,                        "Editar parámetros globales del sistema (TZ, formato fecha, redondeo)"),
        (Guid.Parse("00000005-0006-0000-0000-000000000001"), AdminSucursalesDepartamentosGestionar,        "Asignar, desactivar y reactivar departamentos por sucursal (N:M)"),
        (Guid.Parse("00000005-0006-0000-0000-000000000002"), AdminSucursalesPuestosGestionar,              "Asignar, desactivar y reactivar puestos por sucursal (N:M)"),
        (Guid.Parse("00000005-0006-0000-0000-000000000003"), AdminSucursalesUsuariosGestionar,             "Asignar, desactivar y reactivar usuarios por sucursal (N:M)"),
        (Guid.Parse("00000005-0007-0000-0000-000000000001"), AdminPuestosGestionar,                        "Crear, editar y desactivar puestos del catálogo organizacional"),
        (Guid.Parse("00000005-0007-0000-0000-000000000002"), AdminPuestosLeer,                              "Consultar puestos del catálogo organizacional"),
        (Guid.Parse("00000005-0008-0000-0000-000000000001"), AdminEmpleadosGestionar,                      "Crear, editar y desactivar empleados del catálogo organizacional"),
        (Guid.Parse("00000005-0008-0000-0000-000000000002"), AdminEmpleadosLeerTodasSucursales,            "Consultar empleados de todas las sucursales de la empresa"),
        (Guid.Parse("00000005-0008-0000-0000-000000000003"), AdminEmpleadosGestionarTodasSucursales,       "Crear, editar, dar de baja y dar acceso a empleados de todas las sucursales de la empresa"),
        // Catálogos granulares (F-Admin-PR5.x). Reusan namespace 00000004-* (Compartido).
        (Guid.Parse("00000004-0003-0000-0000-000000000001"), CatalogosMonedasGestionar,                    "Crear, editar y desactivar monedas del catálogo cross-empresa"),
        (Guid.Parse("00000004-0004-0000-0000-000000000001"), CatalogosTiposCambioGestionar,                "Registrar tipos de cambio por moneda y fecha"),
        (Guid.Parse("00000004-0005-0000-0000-000000000001"), CatalogosCondicionesPagoGestionar,            "Crear, editar y desactivar condiciones de pago"),
        (Guid.Parse("00000004-0006-0000-0000-000000000001"), CatalogosIncotermsGestionar,                  "Crear, editar y desactivar Incoterms"),
        (Guid.Parse("00000004-0007-0000-0000-000000000001"), CatalogosTransportistasGestionar,             "Crear, editar y desactivar transportistas"),
        (Guid.Parse("00000004-0008-0000-0000-000000000001"), CatalogosUnidadesMedidaGestionar,             "Crear, editar y desactivar unidades de medida"),
        // Datos Maestros granulares (F-Admin-PR4.5).
        (Guid.Parse("00000004-0009-0000-0000-000000000001"), DatosMaestrosProveedoresGestionar,            "Crear, editar y desactivar proveedores del catálogo cross-empresa"),
        (Guid.Parse("00000004-0010-0000-0000-000000000001"), DatosMaestrosArticulosGestionar,              "Crear, editar y desactivar artículos del catálogo cross-empresa"),
        (Guid.Parse("00000004-0011-0000-0000-000000000001"), DatosMaestrosClientesGestionar,               "Crear, editar y desactivar clientes del master cross-empresa (ADR-0048)"),
        (Guid.Parse("00000004-0012-0000-0000-000000000001"), DatosMaestrosProductosAwGestionar,            "Crear, editar y desactivar productos de venta A+W del master cross-empresa (ADR-0048)"),
        // Cuentas por Pagar (F0-PR1). Namespace 00000007-*.
        (Guid.Parse("00000007-0001-0000-0000-000000000001"), CuentasPorPagarFacturasLeer,                 "Consultar facturas de proveedor (detalle y listados)"),
        (Guid.Parse("00000007-0001-0000-0000-000000000002"), CuentasPorPagarFacturasCapturar,             "Capturar facturas de proveedor (con y sin OC)"),
        (Guid.Parse("00000007-0001-0000-0000-000000000003"), CuentasPorPagarFacturasEditar,               "Editar facturas pre-autorización"),
        (Guid.Parse("00000007-0001-0000-0000-000000000004"), CuentasPorPagarFacturasCancelar,             "Cancelar facturas con motivo"),
        (Guid.Parse("00000007-0001-0000-0000-000000000005"), CuentasPorPagarFacturasEnviarRevision,       "Enviar factura a revisión por dependencia"),
        (Guid.Parse("00000007-0001-0000-0000-000000000006"), CuentasPorPagarFacturasLiberarRevision,      "Liberar factura desde revisión (responsable del área)"),
        (Guid.Parse("00000007-0001-0000-0000-000000000007"), CuentasPorPagarFacturasAutorizar,            "Autorizar factura manualmente (override, raro)"),
        (Guid.Parse("00000007-0002-0000-0000-000000000001"), CuentasPorPagarNotasCreditoLeer,             "Consultar notas de crédito del proveedor"),
        (Guid.Parse("00000007-0002-0000-0000-000000000002"), CuentasPorPagarNotasCreditoCapturar,         "Capturar notas de crédito del proveedor"),
        (Guid.Parse("00000007-0003-0000-0000-000000000001"), CuentasPorPagarNotasCargoLeer,               "Consultar notas de cargo internas"),
        (Guid.Parse("00000007-0003-0000-0000-000000000002"), CuentasPorPagarNotasCargoCrear,              "Crear notas de cargo internas"),
        (Guid.Parse("00000007-0003-0000-0000-000000000003"), CuentasPorPagarNotasCargoAutorizar,          "Autorizar notas de cargo (Dirección)"),
        (Guid.Parse("00000007-0003-0000-0000-000000000004"), CuentasPorPagarNotasCargoAplicar,            "Aplicar nota de cargo al saldo del proveedor"),
        (Guid.Parse("00000007-0004-0000-0000-000000000001"), CuentasPorPagarAnticiposLeer,                "Consultar anticipos a proveedores"),
        (Guid.Parse("00000007-0004-0000-0000-000000000002"), CuentasPorPagarAnticiposCapturar,            "Capturar anticipos a proveedores (CFDI serie FANT)"),
        (Guid.Parse("00000007-0005-0000-0000-000000000001"), CuentasPorPagarComprobacionesLeer,           "Consultar comprobaciones de gastos"),
        (Guid.Parse("00000007-0005-0000-0000-000000000002"), CuentasPorPagarComprobacionesCapturar,       "Capturar comprobaciones de gastos (caja chica, viáticos, TC)"),
        (Guid.Parse("00000007-0005-0000-0000-000000000003"), CuentasPorPagarComprobacionesAprobarNivel1,  "Aprobar comprobaciones nivel 1 (Jefe/Comercio Exterior)"),
        (Guid.Parse("00000007-0005-0000-0000-000000000004"), CuentasPorPagarComprobacionesAprobarNivel2,  "Aprobar comprobaciones nivel 2 (Dirección de Finanzas)"),

        // F7-PR3: Viáticos electrónicos (sub-namespace 000a, reservado a Viáticos)
        (Guid.Parse("00000007-000a-0000-0000-000000000001"), CuentasPorPagarViaticosLeer,                 "Consultar solicitudes de viáticos"),
        (Guid.Parse("00000007-000a-0000-0000-000000000002"), CuentasPorPagarViaticosSolicitar,            "Solicitar anticipo de viáticos (empleado)"),
        (Guid.Parse("00000007-000a-0000-0000-000000000003"), CuentasPorPagarViaticosAutorizarJefe,        "Autorizar solicitud de viáticos (Jefe directo)"),
        (Guid.Parse("00000007-000a-0000-0000-000000000004"), CuentasPorPagarViaticosAutorizarDf,          "Autorizar viáticos que exceden política (Dirección de Finanzas)"),
        (Guid.Parse("00000007-000a-0000-0000-000000000005"), CuentasPorPagarViaticosMarcarPagado,         "Marcar préstamo de viáticos pagado (proxy Tesorería)"),
        (Guid.Parse("00000007-000a-0000-0000-000000000006"), CuentasPorPagarViaticosCapturarComprobacion, "Capturar comprobación de viáticos al regreso (empleado)"),
        (Guid.Parse("00000007-000a-0000-0000-000000000007"), CuentasPorPagarViaticosLiberar,              "Liberar comprobación de viáticos (Auxiliar CxP)"),

        // F7-PR3: catálogos locales de CxP (sub-namespace 000b)
        (Guid.Parse("00000007-000b-0000-0000-000000000001"), CuentasPorPagarCatalogosAprobadoresAdministrar, "Administrar catálogo de aprobadores con límite (RH / Responsable CxP)"),
        (Guid.Parse("00000007-000b-0000-0000-000000000002"), CuentasPorPagarCatalogosPoliticasAdministrar,   "Administrar políticas de viáticos por puesto y destino (RH + Dirección)"),

        (Guid.Parse("00000007-0006-0000-0000-000000000001"), CuentasPorPagarTcLeer,                       "Consultar tarjetas de crédito, movimientos y estados de cuenta"),
        (Guid.Parse("00000007-0006-0000-0000-000000000002"), CuentasPorPagarTcRegistrarMovimiento,        "Registrar movimientos de tarjeta de crédito empresarial"),
        (Guid.Parse("00000007-0006-0000-0000-000000000003"), CuentasPorPagarTcCerrarEstadoCuenta,         "Cerrar estado de cuenta de TC y generar pasivo agregado contra el banco"),
        (Guid.Parse("00000007-0006-0000-0000-000000000004"), CuentasPorPagarTcAdministrar,                "Administrar tarjetas corporativas y usuarios autorizados (F7-PR4)"),
        (Guid.Parse("00000007-0006-0000-0000-000000000005"), CuentasPorPagarTcDisputar,                   "Marcar y resolver disputas sobre movimientos de TC (F7-PR6)"),
        (Guid.Parse("00000007-0007-0000-0000-000000000001"), CuentasPorPagarProveedoresPonerRevision,     "Poner proveedor en revisión global"),
        (Guid.Parse("00000007-0007-0000-0000-000000000002"), CuentasPorPagarProveedoresLiberarRevision,   "Liberar proveedor de revisión global"),
        (Guid.Parse("00000007-0007-0000-0000-000000000003"), CuentasPorPagarProveedoresAjustarTolerancia, "Ajustar tolerancia de conciliación por proveedor (restringido)"),
        (Guid.Parse("00000007-0008-0000-0000-000000000001"), CuentasPorPagarReportesCartera,              "Consultar reporte de cartera por categoría × revisión"),
        (Guid.Parse("00000007-0008-0000-0000-000000000002"), CuentasPorPagarReportesAntiguedad,           "Consultar reporte de antigüedad de saldos y anticipos"),
        (Guid.Parse("00000007-0008-0000-0000-000000000003"), CuentasPorPagarReportesDiot,                 "Consultar reporte DIOT"),
        (Guid.Parse("00000007-0008-0000-0000-000000000004"), CuentasPorPagarReportesTc,                   "Consultar reportes de tarjetas de crédito empresariales"),
        (Guid.Parse("00000007-0009-0000-0000-000000000001"), CuentasPorPagarCfdisLeer,                    "Consultar CFDIs recibidos"),
        (Guid.Parse("00000007-0009-0000-0000-000000000002"), CuentasPorPagarCfdisCargarManual,            "Cargar CFDI manualmente (canal de respaldo)"),
        (Guid.Parse("00000007-0009-0000-0000-000000000003"), CuentasPorPagarCfdisDescartar,               "Descartar CFDI por duplicado o error"),
        (Guid.Parse("00000007-0010-0000-0000-000000000001"), CuentasPorPagarReposicionesLeer,             "Consultar reposiciones de caja chica y saldos por reponer"),
        (Guid.Parse("00000007-0010-0000-0000-000000000002"), CuentasPorPagarReposicionesAdministrar,      "Configurar mínimo de reposición por sucursal y emitir cortes manuales"),
        // Integración A+W (PR A). Namespace 00000006-*.
        (Guid.Parse("00000006-0001-0000-0000-000000000001"), IntegracionesAwCotizacionesCrear,             "Enviar cotizaciones EDI desde Glass Agent al ERP para correlación con A+W"),
        (Guid.Parse("00000006-0001-0000-0000-000000000002"), IntegracionesAwCotizacionesConsultar,         "Consultar el estado de correlación de una cotización enviada"),
        (Guid.Parse("00000006-0001-0000-0000-000000000003"), IntegracionesAwCotizacionesReintentar,        "Forzar el reintento de drop o de correlación de una cotización"),
        (Guid.Parse("00000006-0002-0000-0000-000000000001"), IntegracionesAwPedidosConsultar,              "Consultar pedidos espejo de A+W vía Hybrid Connection (read-only)"),
        (Guid.Parse("00000006-0003-0000-0000-000000000001"), IntegracionesAwClientesConsultar,             "Consultar el catálogo de clientes de A+W (read-only)"),
        (Guid.Parse("00000006-0004-0000-0000-000000000001"), IntegracionesAwArticulosConsultar,            "Consultar el catálogo de artículos de A+W (read-only)"),
        (Guid.Parse("00000006-0005-0000-0000-000000000001"), IntegracionesAwInventarioConsultar,           "Consultar inventario de A+W (read-only) para validación de stock"),
        (Guid.Parse("00000006-0006-0000-0000-000000000001"), IntegracionesAwAdministracionServicios,       "Administrar el catálogo de service principals (UsuarioServicio)"),
        (Guid.Parse("00000006-0006-0000-0000-000000000002"), IntegracionesAwAdministracionConfiguracion,   "Administrar la configuración del módulo Integraciones.Aw"),
        // Integraciones.Fiscal (PR-2 foundation). Sub-namespace 00000006-1xxx
        // reservado para Integraciones.Fiscal (vs 00000006-0xxx de Aw).
        (Guid.Parse("00000006-1001-0000-0000-000000000001"), IntegracionesFiscalLeer,                      "Consultar la configuración del PAC y los RFCs receptores"),
        (Guid.Parse("00000006-1001-0000-0000-000000000002"), IntegracionesFiscalAdministrar,               "Administrar la configuración del PAC (FiscalAPI) y los RFCs receptores por empresa"),
        // Almacén (F0-PR1). Namespace 00000008-*.
        (Guid.Parse("00000008-0001-0000-0000-000000000001"), AlmacenAlmacenesRead,                        "Consultar el catálogo de almacenes y sub-almacenes"),
        (Guid.Parse("00000008-0001-0000-0000-000000000002"), AlmacenAlmacenesAdministrar,                 "Crear, editar y desactivar almacenes y sub-almacenes"),
        (Guid.Parse("00000008-0002-0000-0000-000000000001"), AlmacenEntradasLeer,                         "Consultar entradas (recepciones) — bandeja y detalle"),
        (Guid.Parse("00000008-0002-0000-0000-000000000002"), AlmacenEntradasCapturar,                     "Capturar entradas en borrador (Variante A factura y Variante B packing list)"),
        (Guid.Parse("00000008-0002-0000-0000-000000000003"), AlmacenEntradasRegistrar,                    "Firmar entradas (pasar de Borrador a Registrado, dispara saldo y eventos)"),
        (Guid.Parse("00000008-0002-0000-0000-000000000004"), AlmacenEntradasCancelarBorrador,             "Cancelar entradas en estado Borrador (no aplica a registradas)"),
        (Guid.Parse("00000008-0003-0000-0000-000000000001"), AlmacenSalidasLeerPropias,                   "Consultar las salidas donde el usuario es destinatario/responsable"),
        (Guid.Parse("00000008-0003-0000-0000-000000000002"), AlmacenSalidasLeerTodas,                     "Consultar todas las salidas (sin restricción de destinatario)"),
        (Guid.Parse("00000008-0003-0000-0000-000000000003"), AlmacenSalidasCapturar,                      "Capturar salidas en borrador (Variante A RQ y Variante B Vale)"),
        (Guid.Parse("00000008-0003-0000-0000-000000000004"), AlmacenSalidasRegistrar,                     "Firmar salidas (pasar a Registrado, dispara consumo de saldo y eventos)"),
        (Guid.Parse("00000008-0003-0000-0000-000000000005"), AlmacenSalidasPorVale,                       "Registrar salidas urgentes por vale (auditable, regularización en 48h)"),
        (Guid.Parse("00000008-0004-0000-0000-000000000001"), AlmacenDevolucionesInternasLeer,             "Consultar devoluciones internas (sub-flujo 8.A)"),
        (Guid.Parse("00000008-0004-0000-0000-000000000002"), AlmacenDevolucionesInternasCapturar,         "Capturar y aplicar devoluciones internas (sub-flujo 8.A)"),
        (Guid.Parse("00000008-0005-0000-0000-000000000001"), AlmacenDevolucionesProveedorIniciar,         "Iniciar devoluciones a proveedor (sub-flujo 8.B)"),
        (Guid.Parse("00000008-0005-0000-0000-000000000002"), AlmacenDevolucionesProveedorAutorizar,       "Autorizar devoluciones a proveedor (Dirección, compartido con cxp.notas-cargo.autorizar)"),
        (Guid.Parse("00000008-0005-0000-0000-000000000003"), AlmacenDevolucionesProveedorRegistrar,       "Registrar la salida física de la devolución a proveedor"),
        (Guid.Parse("00000008-0006-0000-0000-000000000001"), AlmacenInventariosLeer,                      "Consultar conteos de inventario físico"),
        (Guid.Parse("00000008-0006-0000-0000-000000000002"), AlmacenInventariosCrear,                     "Crear y planificar conteos de inventario (rotativo y anual)"),
        (Guid.Parse("00000008-0006-0000-0000-000000000003"), AlmacenInventariosCapturar,                  "Capturar conteo sin ver cantidad teórica (captura sin sesgo, A6)"),
        (Guid.Parse("00000008-0006-0000-0000-000000000004"), AlmacenInventariosAprobarNivel1,             "Aprobar ajustes de inventario nivel 1 (<$1K, Almacenista)"),
        (Guid.Parse("00000008-0006-0000-0000-000000000005"), AlmacenInventariosAprobarNivel2,             "Aprobar ajustes de inventario nivel 2 ($1K–$10K, Supervisor)"),
        (Guid.Parse("00000008-0006-0000-0000-000000000006"), AlmacenInventariosAprobarNivel3,             "Aprobar ajustes de inventario nivel 3 (>$10K, Jefe Almacén + Finanzas)"),
        (Guid.Parse("00000008-0007-0000-0000-000000000001"), AlmacenAjustesManual,                        "Registrar ajustes manuales de inventario (restringido, auditable)"),
        (Guid.Parse("00000008-0008-0000-0000-000000000001"), AlmacenReportesAlfak,                        "Consultar reporte ALFAK-HISTORIAL-ALMACEN (cierre de mes)"),
        (Guid.Parse("00000008-0008-0000-0000-000000000002"), AlmacenReportesMpCnk,                        "Consultar reporte SAP-REPORTE-EXISTENCIA-MP-CNK (inventario diario MP)"),
        (Guid.Parse("00000008-0008-0000-0000-000000000003"), AlmacenReportesMovimientos,                  "Consultar reporte de movimientos por artículo (vNext)"),
        (Guid.Parse("00000008-0009-0000-0000-000000000001"), AlmacenCierreMesEjecutar,                    "Ejecutar el cierre de mes del módulo Almacén (Jefe Almacén)"),
        (Guid.Parse("00000008-000a-0000-0000-000000000001"), AlmacenLecturaTotal,                         "Consultar todos los movimientos y saldos del módulo (Auditor Externo, A17)"),
        (Guid.Parse("00000008-000b-0000-0000-000000000001"), AlmacenReservasAdministrar,                  "Reservar y liberar stock contra documentos de origen (A19)"),
        (Guid.Parse("00000008-000c-0000-0000-000000000001"), AlmacenAsignacionesRead,                     "Consultar asignaciones artículo→ubicación (OITW) y su política de reposición"),
        (Guid.Parse("00000008-000c-0000-0000-000000000002"), AlmacenAsignacionesAdministrar,              "Crear, editar y desasignar asignaciones artículo→ubicación (min/máx/reorden/reabasto)"),
        (Guid.Parse("00000008-000d-0000-0000-000000000001"), AlmacenReordenRead,                          "Consultar configuraciones de reorden N1/N2 (min/máx/reorden/reabasto por sucursal/almacén)"),
        (Guid.Parse("00000008-000d-0000-0000-000000000002"), AlmacenReordenAdministrar,                   "Crear, editar y desactivar configuraciones de reorden N1/N2"),
        (Guid.Parse("00000008-000e-0000-0000-000000000001"), AlmacenUbicacionesRead,                      "Consultar el catálogo de ubicaciones físicas N4 (racks/pasillos) por sub-almacén"),
        (Guid.Parse("00000008-000e-0000-0000-000000000002"), AlmacenUbicacionesAdministrar,               "Crear, editar y desactivar ubicaciones físicas N4 (racks/pasillos)"),
        // Facturación (F0-PR1). Namespace 00000009-*.
        (Guid.Parse("00000009-0001-0000-0000-000000000001"), FacturacionPedidosImportar,             "Importar/procesar pedidos facturables desde A+W y Planta Pintura"),
        (Guid.Parse("00000009-0001-0000-0000-000000000002"), FacturacionPedidosCapturar,             "Capturar y editar pedidos facturables manuales (origen Manual)"),
        (Guid.Parse("00000009-0001-0000-0000-000000000003"), FacturacionPedidosExcepcionesResolver,  "Resolver excepciones de la bandeja de importación de pedidos"),
        (Guid.Parse("00000009-0002-0000-0000-000000000001"), FacturacionFacturasEmitir,              "Emitir (sellar + timbrar) facturas de venta CFDI"),
        (Guid.Parse("00000009-0002-0000-0000-000000000002"), FacturacionFacturasLeer,                "Consultar facturas y la cadena de relaciones CFDI"),
        (Guid.Parse("00000009-0002-0000-0000-000000000003"), FacturacionFacturasEditarReceptor,      "Corregir los datos fiscales del receptor desde la emisión: muestra el acceso directo al catálogo de clientes (los datos son de solo lectura en la factura)"),
        (Guid.Parse("00000009-0002-0000-0000-000000000004"), FacturacionFacturasEditarArticulo,      "Corregir los datos fiscales del artículo desde la emisión: muestra el acceso directo al catálogo de productos A+W (los datos son de solo lectura en la factura)"),
        (Guid.Parse("00000009-0003-0000-0000-000000000001"), FacturacionAnticiposEmitir,             "Emitir facturas de anticipo (serie FANT)"),
        (Guid.Parse("00000009-0003-0000-0000-000000000002"), FacturacionAnticiposVincular,           "Vincular anticipos a la factura final (relación 07)"),
        (Guid.Parse("00000009-0003-0000-0000-000000000003"), FacturacionAnticiposLeer,               "Consultar el Control de Anticipos (saldos por cliente)"),
        (Guid.Parse("00000009-0004-0000-0000-000000000001"), FacturacionNotasCreditoBonificacion,    "Emitir notas de crédito por bonificación (relación 01)"),
        (Guid.Parse("00000009-0004-0000-0000-000000000002"), FacturacionNotasCreditoLeer,            "Consultar notas de crédito emitidas"),
        (Guid.Parse("00000009-0005-0000-0000-000000000001"), FacturacionCartaPorteEmitir,            "Emitir Carta Porte 3.1 (CFDI tipo T o I), incluyendo tramos siguientes"),
        (Guid.Parse("00000009-0005-0000-0000-000000000002"), FacturacionCartaPorteLeer,              "Consultar Carta Portes emitidas"),
        (Guid.Parse("00000009-0006-0000-0000-000000000001"), FacturacionReppEmitir,                  "Emitir complementos de pago (REPP / Pago 2.0)"),
        (Guid.Parse("00000009-0007-0000-0000-000000000001"), FacturacionCancelacionesSolicitar,      "Solicitar la cancelación SAT 4.0 de un comprobante"),
        (Guid.Parse("00000009-0007-0000-0000-000000000002"), FacturacionCancelacionesConsultar,      "Consultar el estatus de las solicitudes de cancelación"),
        (Guid.Parse("00000009-0008-0000-0000-000000000001"), FacturacionActivosAutorizar,            "Autorizar la venta de activos fijos antes de timbrar (Contador General)"),
        (Guid.Parse("00000009-0009-0000-0000-000000000001"), FacturacionCajaLiquidar,                "Cerrar sesiones de caja (arqueo y liquidación)"),
        (Guid.Parse("00000009-0009-0000-0000-000000000002"), FacturacionCajaAdministrar,             "Administrar cajas: CRUD, alcances (sucursales/canales/usuarios) y alcances administrativos"),
        (Guid.Parse("00000009-0009-0000-0000-000000000003"), FacturacionCajaOperar,                  "Abrir sesión de caja propia, registrar cobros y movimientos, iniciar arqueo"),
        (Guid.Parse("00000009-0009-0000-0000-000000000004"), FacturacionCajaSupervisar,              "Autorizar apertura de caja ajena, reabrir sesión en arqueo y cancelar cobros"),
        (Guid.Parse("00000009-0009-0000-0000-000000000005"), FacturacionCajaLeerTodas,               "Alcance administrativo total en Facturación, incluido el bucket Sin asignar"),
        (Guid.Parse("00000009-000a-0000-0000-000000000001"), FacturacionReportesLeer,                "Consultar los reportes del módulo Facturación"),
        (Guid.Parse("00000009-000b-0000-0000-000000000001"), FacturacionComprobantesReintentarTimbrado, "Reintentar el timbrado de un comprobante en TimbradoFallido (cualquier tipo)"),
        (Guid.Parse("00000009-000b-0000-0000-000000000002"), FacturacionComprobantesDescartar,        "Descartar un comprobante fallido que no se reintentará (quema el folio y libera el pedido)"),

        // Cuentas por Cobrar (CXC-PR1). Namespace 0000000a-*.
        (Guid.Parse("0000000a-0001-0000-0000-000000000001"), CuentasPorCobrarLineasCreditoLeer,       "Consultar líneas de crédito de clientes"),
        (Guid.Parse("0000000a-0001-0000-0000-000000000002"), CuentasPorCobrarLineasCreditoGestionar,  "Crear, editar, bloquear y desbloquear líneas de crédito"),
        (Guid.Parse("0000000a-0002-0000-0000-000000000001"), CuentasPorCobrarLiberacionDecidir,       "Decidir la liberación de pedidos por crédito"),
        (Guid.Parse("0000000a-0002-0000-0000-000000000002"), CuentasPorCobrarLiberacionOverride,      "Otorgar autorización consumible de override de crédito (gerente)"),
        (Guid.Parse("0000000a-0003-0000-0000-000000000001"), CuentasPorCobrarCobranzaRegistrar,       "Registrar gestiones de cobranza (llamadas, correos, promesas de pago)"),
        (Guid.Parse("0000000a-0004-0000-0000-000000000001"), CuentasPorCobrarCarteraLeer,             "Consultar cartera, antigüedad de saldos, estado de cuenta y alertas"),
        (Guid.Parse("0000000a-0005-0000-0000-000000000001"), CuentasPorCobrarAplicacionPagoProponer,  "Crear propuestas de aplicación de pago desde depósitos"),
        (Guid.Parse("0000000a-0005-0000-0000-000000000002"), CuentasPorCobrarAplicacionPagoConfirmar, "Confirmar o rechazar propuestas de aplicación de pago (Ingresos)"),

        // Tesorería / Bancos (TES-PR1). Namespace 0000000b-*.
        (Guid.Parse("0000000b-0001-0000-0000-000000000001"), TesoreriaCuentasVer,                   "Consultar el catálogo de cuentas bancarias propias y sus saldos"),
        (Guid.Parse("0000000b-0001-0000-0000-000000000002"), TesoreriaCuentasAdministrar,           "Crear, editar y activar/desactivar cuentas bancarias propias"),
        (Guid.Parse("0000000b-0002-0000-0000-000000000001"), TesoreriaMovimientosVer,               "Consultar el libro de movimientos bancarios"),
        (Guid.Parse("0000000b-0002-0000-0000-000000000002"), TesoreriaMovimientosRegistrar,         "Registrar movimientos bancarios de ingreso (alta manual)"),
        (Guid.Parse("0000000b-0002-0000-0000-000000000003"), TesoreriaMovimientosVerCuentaCompleta, "Des-enmascarar CLABE y número de cuenta (PII, ADR-0018)"),
        (Guid.Parse("0000000b-0003-0000-0000-000000000001"), TesoreriaPagosAplicar,                 "Registrar pagos a proveedor contra pasivos autorizados por CxP"),
        (Guid.Parse("0000000b-0003-0000-0000-000000000002"), TesoreriaPagosRevertir,                "Revertir pagos aplicados (contramovimiento, RN-10)"),
        (Guid.Parse("0000000b-0004-0000-0000-000000000001"), TesoreriaPagosCuentaRegistrar,         "Registrar pagos a cuenta (egreso sin documento, gate RN-2)"),
        (Guid.Parse("0000000b-0004-0000-0000-000000000002"), TesoreriaPagosCuentaLigar,             "Ligar tardíamente un pago a cuenta al pasivo provisionado"),
        (Guid.Parse("0000000b-0005-0000-0000-000000000001"), TesoreriaCorridasCrear,                "Crear y armar corridas de pago desde la bandeja de pasivos"),
        (Guid.Parse("0000000b-0005-0000-0000-000000000002"), TesoreriaCorridasAutorizar,            "Autorizar corridas de pago (pasa además por la matriz de autorización)"),
        (Guid.Parse("0000000b-0005-0000-0000-000000000003"), TesoreriaCorridasEjecutar,             "Ejecutar líneas de corridas autorizadas (RN-5)"),
        (Guid.Parse("0000000b-0006-0000-0000-000000000001"), TesoreriaDepositosConfirmar,           "Confirmar depósitos de cliente contra propuestas de CxC (RN-6)"),
        (Guid.Parse("0000000b-0006-0000-0000-000000000002"), TesoreriaDepositosRechazar,            "Rechazar propuestas de aplicación de depósito con motivo"),
        (Guid.Parse("0000000b-0007-0000-0000-000000000001"), TesoreriaReppRegistrar,                "Registrar REPP recibidos de proveedor (libera FALTA_REPP en CxP)"),
        (Guid.Parse("0000000b-0008-0000-0000-000000000001"), TesoreriaConciliacionOperar,           "Cargar extractos y operar el matching de conciliación bancaria"),
        (Guid.Parse("0000000b-0008-0000-0000-000000000002"), TesoreriaConciliacionCerrar,           "Cerrar conciliaciones con saldo cuadrado y generar acta (RN-7)"),
        (Guid.Parse("0000000b-0009-0000-0000-000000000001"), TesoreriaPasivosSolicitarCancelacion,  "Solicitar a CxP la cancelación de un pasivo desde la bandeja"),
        (Guid.Parse("0000000b-0009-0000-0000-000000000002"), TesoreriaPasivosVer,                   "Consultar la bandeja de pasivos autorizados pendientes de pago"),
        (Guid.Parse("0000000b-000a-0000-0000-000000000001"), TesoreriaReportesVer,                  "Consultar los reportes del módulo Tesorería (flujo de efectivo, auxiliares)"),

        // Centros de Costo (CECO-A1; modelo Dim desde CECO-PR4). Namespace 0000000c-*.
        (Guid.Parse("0000000c-0001-0000-0000-000000000001"), CentrosCostoCatalogoLeer,                "Consultar el catálogo de centros de costo (árboles, niveles y grupos)"),
        (Guid.Parse("0000000c-0001-0000-0000-000000000002"), CentrosCostoCatalogoAdministrar,         "Crear, editar y desactivar niveles y grupos del catálogo de centros de costo"),
        (Guid.Parse("0000000c-0002-0000-0000-000000000001"), CentrosCostoAsignacionesAdministrar,     "Asignar y revocar alcance de centros de costo a usuarios (marcado por nivel o grupo, congelado en máquinas)"),
        (Guid.Parse("0000000c-0003-0000-0000-000000000001"), CentrosCostoDim3LeerTodos,               "Alcance total en centros de costo: ver todas las máquinas (Dim3) sin restricción de asignación"),
    };
}
