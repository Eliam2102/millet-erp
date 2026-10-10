import {
  AlertTriangle,
  ArrowRightLeft,
  Banknote,
  BarChart3,
  Bell,
  BookOpen,
  Boxes,
  Building2,
  CalendarRange,
  CheckSquare,
  ClipboardCheck,
  ClipboardList,
  CreditCard,
  FileBadge,
  FileCheck2,
  FileText,
  Gauge,
  HandCoins,
  Inbox,
  Landmark,
  FlaskConical,
  Layers,
  ListTree,
  Lock,
  MapPin,
  PackageMinus,
  PackagePlus,
  PhoneCall,
  Plane,
  PieChart,
  Receipt,
  ReceiptText,
  RotateCcw,
  Scale,
  ShoppingCart,
  Sliders,
  Truck,
  Unlock,
  Upload,
  Users,
  Warehouse,
  Wallet,
  type LucideIcon,
} from 'lucide-react';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { datosMaestrosAdminCards } from '@/modules/datos-maestros/admin';

/** Catálogo único de pantallas y permisos del menú (ADR-0032, revisión 10-oct).
 * nav.ts expone sus gates, búsqueda y contexto; Administración reutiliza
 * las mismas entradas de Configuración sin duplicar permisos.
 */
export interface NavCard {
  label: string;
  description: string;
  to: string;
  icon: LucideIcon;
  /** Permiso requerido (any-of de uno). */
  permission?: string;
  /** Cualquiera de estos permisos basta (any-of de varios). */
  permissionsAny?: readonly string[];
}

export interface NavSeccion {
  /** <c>Operación</c> / <c>Configuración</c> / nombre de submódulo. */
  label: string;
  cards: readonly NavCard[];
}

export interface NavModulo {
  /** Stable identifier; se usa como key del modal y en tests. */
  moduloId: string;
  label: string;
  icon: LucideIcon;
  /** Si <c>true</c>, el item aparece en sidebar pero no abre modal. */
  disabled?: boolean;
  /** Si <c>secciones</c> está vacío o todas las cards están filtradas
   * por permisos, el modal renderiza un mensaje neutro. */
  secciones: readonly NavSeccion[];
}

/**
 * Item especial del sidebar — link directo, sin modal. Solo se usa
 * para "Inicio" (landing del ERP). El resto del sidebar son módulos.
 */
export interface NavLink {
  kind: 'link';
  label: string;
  to: string;
  icon: LucideIcon;
}

export type NavSidebarItem = (NavModulo & { kind: 'modulo' }) | NavLink;

// ============================================================================
// Módulos del back-office (ADR-0032 + CLAUDE.md §Módulos)
// ============================================================================

const moduloCompras: NavModulo = {
  moduloId: 'compras',
  label: 'Compras',
  icon: ShoppingCart,
  secciones: [
    {
      label: 'Operación',
      cards: [
        {
          label: 'Mis requisiciones',
          description: 'Bandeja general de requisiciones. Crear, ver, editar y dar seguimiento.',
          to: '/compras/requisiciones',
          icon: Inbox,
          permission: PermisosCanonicos.ComprasRequisicionesLeer,
        },
        {
          label: 'Pendientes de autorización',
          description: 'Requisiciones en espera de aprobación N1 o N2 según tu rol.',
          to: '/compras/pendientes',
          icon: CheckSquare,
          permissionsAny: [
            PermisosCanonicos.ComprasRequisicionesAutorizarNivel1,
            PermisosCanonicos.ComprasRequisicionesAutorizarNivel2,
          ],
        },
        {
          label: 'Órdenes de compra',
          description:
            'Bandeja general de OCs. Crear desde RQ, consolidar, dar seguimiento a recepciones, facturas y pagos.',
          to: '/compras/ordenes',
          icon: ShoppingCart,
          permission: PermisosCanonicos.ComprasOrdenesLeer,
        },
        {
          label: 'Órdenes por autorizar',
          description:
            'Inbox FIFO de OCs esperando firma N1 o N2. Click navega al detalle para aprobar / rechazar.',
          to: '/compras/ordenes/pendientes-autorizacion',
          icon: CheckSquare,
          permissionsAny: [
            PermisosCanonicos.ComprasOrdenesAutorizarNivel1,
            PermisosCanonicos.ComprasOrdenesAutorizarNivel2,
          ],
        },
        {
          label: 'Partidas abiertas (reporte)',
          description:
            'KPI cards + tabla densa de OCs no terminales con seguimiento de recepción/facturación/pago. Filtros sticky y badge de días atrasados.',
          to: '/compras/ordenes/partidas-abiertas',
          icon: BarChart3,
          permission: PermisosCanonicos.ComprasOrdenesReportesPartidasAbiertas,
        },
      ],
    },
    {
      label: 'Configuración',
      cards: [
        {
          label: 'Aprobadores',
          description:
            'Designar y revocar aprobadores por departamento y rol. Histórico de vigencias.',
          to: '/compras/admin/aprobadores',
          icon: Users,
          permission: PermisosCanonicos.ComprasAprobadoresAdministrar,
        },
      ],
    },
  ],
};

/** Helper para módulos no implementados aún — sin secciones, disabled. */
function placeholderModulo(moduloId: string, label: string, icon: LucideIcon): NavModulo {
  return { moduloId, label, icon, disabled: true, secciones: [] };
}

const moduloAlmacen: NavModulo = {
  moduloId: 'almacen',
  label: 'Almacén',
  icon: Warehouse,
  secciones: [
    {
      label: 'Operación',
      cards: [
        {
          label: 'Recepciones',
          description:
            'Bandeja de entradas. Captura recepciones Variante A (con factura) y Variante B (con packing list, factura pendiente).',
          to: '/almacen/recepciones',
          icon: PackagePlus,
          permission: PermisosCanonicos.AlmacenEntradasLeer,
        },
        {
          label: 'Salidas',
          description:
            'Surtido de requisiciones aprobadas y vales urgentes. Comprobante PDF descargable.',
          to: '/almacen/salidas',
          icon: PackageMinus,
          permissionsAny: [
            PermisosCanonicos.AlmacenSalidasLeerPropias,
            PermisosCanonicos.AlmacenSalidasLeerTodas,
          ],
        },
        {
          label: 'Devoluciones',
          description:
            'Devoluciones internas (sub-flujo 8.A) y a proveedor (8.B). Conciliación con NC fiscal.',
          to: '/almacen/devoluciones',
          icon: RotateCcw,
          permissionsAny: [
            PermisosCanonicos.AlmacenDevolucionesInternasLeer,
            PermisosCanonicos.AlmacenDevolucionesProveedorIniciar,
          ],
        },
        {
          label: 'Inventario físico',
          description:
            'Conteos rotativos y anuales. Captura sin sesgo, recuento, aprobación por monto, aplicación de ajustes.',
          to: '/almacen/inventarios',
          icon: ClipboardCheck,
          permission: PermisosCanonicos.AlmacenInventariosLeer,
        },
        {
          label: 'Saldos',
          description:
            'Stock vigente por sub-almacén y artículo. Costo promedio ponderado y valor de inventario.',
          to: '/almacen/saldos',
          icon: Boxes,
          permission: PermisosCanonicos.AlmacenAlmacenesRead,
        },
        {
          label: 'Consulta jerárquica',
          description:
            'Saldos por sucursal → almacén → sub-almacén → rack, con rollup por nivel y racks vacíos opcionales.',
          to: '/almacen/saldos-jerarquia',
          icon: ListTree,
          permission: PermisosCanonicos.AlmacenAlmacenesRead,
        },
      ],
    },
    {
      label: 'Cierre y reportes',
      cards: [
        {
          label: 'Cierre de mes',
          description:
            'Ejecutar cierre mensual del módulo Almacén. Valida que no haya conteos ni movimientos pendientes.',
          to: '/almacen/cierre-mes',
          icon: Lock,
          permission: PermisosCanonicos.AlmacenCierreMesEjecutar,
        },
        {
          label: 'Reportes',
          description:
            'ALFAK-HISTORIAL-ALMACEN (cierre de mes) y SAP-REPORTE-EXISTENCIA-MP-CNK (inventario diario MP).',
          to: '/almacen/reportes',
          icon: BarChart3,
          permissionsAny: [
            PermisosCanonicos.AlmacenReportesAlfak,
            PermisosCanonicos.AlmacenReportesMpCnk,
          ],
        },
      ],
    },
    {
      label: 'Configuración',
      cards: [
        {
          label: 'Almacenes',
          description:
            'Catálogo principal del módulo: alta/baja de almacenes por sucursal; cada uno agrupa sub-almacenes por tipo.',
          to: '/almacen/almacenes',
          icon: Warehouse,
          permission: PermisosCanonicos.AlmacenAlmacenesAdministrar,
        },
        {
          label: 'Sub-almacenes',
          description:
            'Subdivisión física de cada almacén por tipo de material (Insumos / Materiales directos / MAT-REV / Transitorio).',
          to: '/almacen/sub-almacenes',
          icon: Layers,
          // Mismo gate que su ruta (almacen.almacenes.leer): los sub-almacenes
          // son catálogo del módulo, patrón de la card de Ubicaciones
          // (card y ruta comparten permiso de lectura).
          permission: PermisosCanonicos.AlmacenAlmacenesRead,
        },
        {
          label: 'Reabasto',
          description:
            'Puntos de reabasto por artículo en una sucursal o almacén. El motor propone requisiciones al caer por debajo del objetivo.',
          to: '/almacen/reorden',
          icon: Gauge,
          permission: PermisosCanonicos.AlmacenReordenRead,
        },
        {
          label: 'Ubicación de artículos',
          description:
            'Define en qué ubicación vive cada artículo (asignación artículo↔ubicación).',
          to: '/almacen/asignaciones',
          icon: MapPin,
          permission: PermisosCanonicos.AlmacenAsignacionesRead,
        },
        {
          label: 'Ubicaciones',
          description:
            'Estructura física real dentro de cada sub-almacén: racks y pasillos con clave alfanumérica.',
          to: '/almacen/ubicaciones',
          icon: Boxes,
          permission: PermisosCanonicos.AlmacenUbicacionesRead,
        },
      ],
    },
  ],
};

const moduloCuentasPorPagar: NavModulo = {
  moduloId: 'cxp',
  label: 'Cuentas por pagar',
  icon: ReceiptText,
  secciones: [
    {
      label: 'Operación',
      cards: [
        {
          label: 'Comprobantes fiscales recibidos',
          description:
            'Bandeja de CFDIs recibidos (mailbox + carga manual). Marcar duplicados, descartar y abrir captura.',
          to: '/cxp/cfdis',
          icon: FileBadge,
          permission: PermisosCanonicos.CuentasPorPagarCfdisLeer,
        },
        {
          label: 'Facturas',
          description:
            'Bandeja general de facturas de proveedor — captura, conciliación, revisión y autorización.',
          to: '/cxp/facturas',
          icon: ReceiptText,
          permission: PermisosCanonicos.CuentasPorPagarFacturasLeer,
        },
        {
          label: 'Revisión por área',
          description:
            'Facturas asignadas al área del usuario en espera de liberación. Indicadores de SLA.',
          to: '/cxp/revision',
          icon: Inbox,
          permission: PermisosCanonicos.CuentasPorPagarFacturasLiberarRevision,
        },
        {
          label: 'Notas de crédito',
          description:
            'Notas de crédito recibidas del proveedor. Aplicación a facturas y monitoreo de NC en espera.',
          to: '/cxp/notas-credito',
          icon: Receipt,
          permission: PermisosCanonicos.CuentasPorPagarNotasCreditoLeer,
        },
        {
          label: 'Anticipos',
          description:
            'Anticipos pagados a proveedores (CFDI serie FANT). Captura y aplicación a facturas.',
          to: '/cxp/anticipos',
          icon: HandCoins,
          permission: PermisosCanonicos.CuentasPorPagarAnticiposLeer,
        },
        {
          label: 'Notas de cargo',
          description:
            'Cargos internos contra el proveedor (devoluciones, garantías, fletes). Autorización Dirección.',
          to: '/cxp/notas-cargo',
          icon: FileText,
          permission: PermisosCanonicos.CuentasPorPagarNotasCargoLeer,
        },
        {
          label: 'Comprobaciones',
          description:
            'Comprobaciones de gastos: Caja Chica + Aduanales con doble autorización (Comercio Exterior + DF).',
          to: '/cxp/comprobaciones',
          icon: ClipboardCheck,
          permission: PermisosCanonicos.CuentasPorPagarComprobacionesLeer,
        },
        {
          label: 'Viáticos',
          description:
            'Solicitudes electrónicas de viáticos: empleado solicita, jefe autoriza, CxP libera al regreso.',
          to: '/cxp/viaticos',
          icon: Plane,
          permission: PermisosCanonicos.CuentasPorPagarViaticosLeer,
        },
        {
          label: 'Tarjetas de crédito',
          description:
            'TC empresariales: tarjetas, movimientos, conciliación con estado de cuenta del banco y cierre.',
          to: '/cxp/tc',
          icon: CreditCard,
          permission: PermisosCanonicos.CuentasPorPagarTcLeer,
        },
      ],
    },
    {
      label: 'Reportes',
      cards: [
        {
          label: 'Auxiliar de proveedores',
          description: 'Saldo por proveedor y moneda a una fecha.',
          icon: BarChart3,
          to: '/cxp/reportes/auxiliar',
          permission: PermisosCanonicos.CuentasPorPagarReportesCartera,
        },
        {
          label: 'Antigüedad de saldos',
          description: 'Saldo a una fecha, por vencer y vencido, separado por proveedor y moneda.',
          to: '/cxp/reportes/antiguedad',
          icon: BarChart3,
          permission: PermisosCanonicos.CuentasPorPagarReportesAntiguedad,
        },
        {
          label: 'Cartera por proveedor',
          description: 'Cartera a una fecha por proveedor, moneda y estado de revisión.',
          to: '/cxp/reportes/cartera',
          icon: Wallet,
          permission: PermisosCanonicos.CuentasPorPagarReportesCartera,
        },
        {
          label: 'Estados de cuenta TC',
          description:
            'Reporte agregado de estados de cuenta de tarjetas de crédito empresariales con sus pasivos asociados.',
          to: '/cxp/reportes/tc',
          icon: CreditCard,
          permission: PermisosCanonicos.CuentasPorPagarReportesTc,
        },
      ],
    },
    {
      label: 'Configuración',
      cards: [
        {
          label: 'Retenciones por concepto',
          description: 'Propuestas SAT y ajustes de Fiscal.',
          icon: Sliders,
          to: '/cxp/admin/retenciones',
          permission: PermisosCanonicos.CuentasPorPagarRetencionesLeer,
        },
        {
          label: 'Aprobadores',
          description:
            'Catálogo de aprobadores con monto máximo por tipo de gasto (Caja chica / Viáticos / TC / Otros sin OC).',
          to: '/cxp/admin/aprobadores',
          icon: Users,
          permission: PermisosCanonicos.CuentasPorPagarCatalogosAprobadoresAdministrar,
        },
        {
          label: 'Políticas de viáticos',
          description:
            'Tabuladores por puesto y destino (Nacional / Internacional). El backend valida solicitudes contra estos topes.',
          to: '/cxp/admin/politicas-viaticos',
          icon: Sliders,
          permission: PermisosCanonicos.CuentasPorPagarCatalogosPoliticasAdministrar,
        },
        {
          label: 'Reposiciones de caja',
          description:
            'Saldos por reponer de caja chica, mínimo de acumulación por sucursal y corte manual hacia Tesorería.',
          to: '/cxp/admin/reposiciones',
          icon: Sliders,
          permission: PermisosCanonicos.CuentasPorPagarReposicionesLeer,
        },
        // PLATFORM-TODO(<CxpTolerancias>): backend no tiene endpoint de
        // tolerancias por proveedor en CxP; permission existe pero el
        // endpoint vivirá en Datos Maestros cuando se construya.
      ],
    },
  ],
};

const moduloTesoreria: NavModulo = {
  moduloId: 'tesoreria',
  label: 'Tesorería',
  icon: Landmark,
  secciones: [
    {
      label: 'Operación',
      cards: [
        {
          label: 'Pagos a proveedor',
          description:
            'Bandeja de pasivos autorizados por CxP: ejecutar pagos, revertir y solicitar cancelación.',
          to: '/tesoreria/pagos',
          icon: Banknote,
          permission: PermisosCanonicos.TesoreriaPasivosVer,
        },
        {
          label: 'Pagos a cuenta',
          description:
            'Egresos sin documento (máximo uno abierto por proveedor) con antigüedad y liga tardía al pasivo.',
          to: '/tesoreria/pagos-cuenta',
          icon: HandCoins,
          permissionsAny: [
            PermisosCanonicos.TesoreriaMovimientosVer,
            PermisosCanonicos.TesoreriaPagosCuentaRegistrar,
            PermisosCanonicos.TesoreriaPagosCuentaLigar,
          ],
        },
        {
          label: 'Corridas de pago',
          description:
            'Lotes de pasivos autorizables como unidad con oficio de cartera imprimible.',
          to: '/tesoreria/corridas',
          icon: ClipboardList,
          permissionsAny: [
            PermisosCanonicos.TesoreriaCorridasCrear,
            PermisosCanonicos.TesoreriaCorridasAutorizar,
            PermisosCanonicos.TesoreriaCorridasEjecutar,
          ],
        },
        {
          label: 'Depósitos por confirmar',
          description:
            'Propuestas de aplicación de CxC y expectativas de Caja: confirmar el hecho bancario que dispara el REPP.',
          to: '/tesoreria/depositos',
          icon: Inbox,
          permissionsAny: [
            PermisosCanonicos.TesoreriaDepositosConfirmar,
            PermisosCanonicos.TesoreriaDepositosRechazar,
          ],
        },
        {
          label: 'Movimientos bancarios',
          description:
            'Libro de movimientos por cuenta: ingresos, egresos, aplicaciones y contramovimientos.',
          to: '/tesoreria/movimientos',
          icon: ArrowRightLeft,
          permission: PermisosCanonicos.TesoreriaMovimientosVer,
        },
        {
          label: 'Conciliación bancaria',
          description:
            'Carga de extracto por perfil de banco, matching asistido y cierre con acta (saldo cuadrado).',
          to: '/tesoreria/conciliacion',
          icon: Scale,
          permissionsAny: [
            PermisosCanonicos.TesoreriaConciliacionOperar,
            PermisosCanonicos.TesoreriaConciliacionCerrar,
          ],
        },
        {
          label: 'Complementos de pago de proveedores',
          description:
            'Pagos PPD sin complemento recibido (SLA 5 días) y registro del REPP que libera la revisión en CxP.',
          to: '/tesoreria/repp',
          icon: ReceiptText,
          permission: PermisosCanonicos.TesoreriaReppRegistrar,
        },
        {
          label: 'Cuentas bancarias',
          description:
            'Catálogo de cuentas propias con saldo: alta, edición y activación (número/CLABE enmascarados).',
          to: '/tesoreria/cuentas',
          icon: Wallet,
          permission: PermisosCanonicos.TesoreriaCuentasVer,
        },
      ],
    },
    {
      label: 'Reportes',
      cards: [
        {
          label: 'Flujo de efectivo',
          description:
            'Ingresos y egresos clasificados por concepto (Operación / Inversión / Financiamiento). ADR-0036.',
          to: '/tesoreria/reportes/flujo-efectivo',
          icon: BarChart3,
          permission: PermisosCanonicos.TesoreriaReportesVer,
        },
        {
          label: 'Auxiliar de bancos',
          description:
            'Libro cronológico por cuenta y período con saldo acumulado. Exportable PDF/Excel.',
          to: '/tesoreria/reportes/auxiliar-bancos',
          icon: FileText,
          permission: PermisosCanonicos.TesoreriaReportesVer,
        },
      ],
    },
  ],
};

/**
 * Centros de costo (CECO-FE-PR1): un item de sidebar con DOS NavCards —
 * "dos entradas, ambas con NavCard" del 05 §1; la asignación tiene card
 * PROPIA (no se esconde tras configuración). El vocabulario Dim↔etiqueta
 * NO vive aquí (helper único, 07 §0) — labels y descripciones hablan de
 * niveles/grupos sin nombrar dimensiones específicas.
 */
const moduloCentrosCosto: NavModulo = {
  moduloId: 'centros-costo',
  label: 'Centros de costo',
  icon: PieChart,
  secciones: [
    {
      label: 'Configuración',
      cards: [
        {
          label: 'Centros de costo',
          description:
            'Catálogo jerárquico de 3 niveles con grupos de clasificación: consulta y administración.',
          to: '/centros-costo/configuracion',
          icon: ListTree,
          permissionsAny: [
            PermisosCanonicos.CentrosCostoCatalogoLeer,
            PermisosCanonicos.CentrosCostoCatalogoAdministrar,
          ],
        },
        {
          label: 'Asignación de centros de costo',
          description:
            'Alcance por usuario: marcar cualquier nivel del árbol asigna sus hojas (tri-estado).',
          to: '/centros-costo/asignaciones',
          icon: Users,
          permission: PermisosCanonicos.CentrosCostoAsignacionesAdministrar,
        },
      ],
    },
  ],
};

const moduloCuentasPorCobrar: NavModulo = {
  moduloId: 'cxc',
  label: 'Cobranza',
  icon: Wallet,
  secciones: [
    {
      label: 'Operación',
      cards: [
        {
          label: 'Líneas de crédito',
          description:
            'Master de líneas por cliente y moneda: límite, plazo, origen (SOLUNION / interno) y crédito disponible.',
          to: '/cxc/lineas-credito',
          icon: CreditCard,
          permission: PermisosCanonicos.CuentasPorCobrarLineasCreditoLeer,
        },
        {
          label: 'Liberación de pedidos',
          description:
            'Decisiones de liberación con cascada serie → crédito → override y autorizaciones consumibles.',
          to: '/cxc/liberaciones',
          icon: Unlock,
          permissionsAny: [
            PermisosCanonicos.CuentasPorCobrarLiberacionDecidir,
            PermisosCanonicos.CuentasPorCobrarLiberacionOverride,
          ],
        },
        {
          label: 'Cobranza',
          description:
            'Seguimientos de cobranza por cliente (canal, resultado, promesa de pago). Bitácora append-only.',
          to: '/cxc/cobranza',
          icon: PhoneCall,
          permissionsAny: [
            PermisosCanonicos.CuentasPorCobrarCarteraLeer,
            PermisosCanonicos.CuentasPorCobrarCobranzaRegistrar,
          ],
        },
        {
          label: 'Anticipos de clientes',
          description:
            'Saldos de anticipo por cliente (datos del read port de Facturación). Reemplazo del "mapa" A+W.',
          to: '/cxc/anticipos',
          icon: HandCoins,
          permission: PermisosCanonicos.CuentasPorCobrarCarteraLeer,
        },
        {
          label: 'Aplicación de pagos',
          description:
            'Propuestas de aplicación depósito ↔ facturas con tolerancia no fiscal; confirmación de Ingresos.',
          to: '/cxc/aplicaciones',
          icon: FileText,
          permissionsAny: [
            PermisosCanonicos.CuentasPorCobrarAplicacionPagoProponer,
            PermisosCanonicos.CuentasPorCobrarAplicacionPagoConfirmar,
          ],
        },
        {
          label: 'Alertas de cartera',
          description:
            'Alertas de vencimiento, sobregiro y promesas incumplidas evaluadas por el worker diario.',
          to: '/cxc/alertas',
          icon: Bell,
          permission: PermisosCanonicos.CuentasPorCobrarCarteraLeer,
        },
      ],
    },
    {
      label: 'Reportes',
      cards: [
        {
          label: 'Antigüedad de saldos',
          description:
            'Cartera viva por buckets configurables, totales por moneda. Exportable PDF/Excel (ADR-0036).',
          to: '/cxc/cartera',
          icon: BarChart3,
          permission: PermisosCanonicos.CuentasPorCobrarCarteraLeer,
        },
        {
          label: 'Estado de cuenta',
          description:
            'Estado de cuenta por cliente: facturas, pagos, NCs y saldo corriente. Impresión limpia.',
          to: '/cxc/estado-cuenta',
          icon: FileText,
          permission: PermisosCanonicos.CuentasPorCobrarCarteraLeer,
        },
      ],
    },
  ],
};

const moduloFacturacion: NavModulo = {
  moduloId: 'facturacion',
  label: 'Facturación',
  icon: FileCheck2,
  secciones: [
    {
      label: 'Operación',
      cards: [
        {
          label: 'Pedidos facturables',
          description:
            'Bandeja de pedidos por facturar (A+W / Planta Pintura / manual). Facturar toma soft-lock; o capturar un pedido manual.',
          to: '/facturacion/pedidos',
          icon: ClipboardList,
          permissionsAny: [
            PermisosCanonicos.FacturacionFacturasEmitir,
            PermisosCanonicos.FacturacionPedidosCapturar,
            PermisosCanonicos.FacturacionPedidosImportar,
          ],
        },
        {
          label: 'Excepciones de ingesta',
          description:
            'Pedidos que fallaron la importación (cliente no existe, producto sin clave SAT, almacén no asignado). Resolución inline.',
          to: '/facturacion/pedidos/excepciones',
          icon: AlertTriangle,
          permission: PermisosCanonicos.FacturacionPedidosExcepcionesResolver,
        },
        {
          label: 'Facturas',
          description:
            'Bandeja de comprobantes emitidos. Cadena de relaciones CFDI, historial del pedido, descargas XML/PDF y cancelación.',
          to: '/facturacion/facturas',
          icon: ReceiptText,
          permission: PermisosCanonicos.FacturacionFacturasLeer,
        },
        {
          label: 'Anticipos',
          description:
            'Control de Anticipos por cliente: emisión (serie FANT), vinculación a la factura final (relación 07) y saldos.',
          to: '/facturacion/anticipos',
          icon: HandCoins,
          permission: PermisosCanonicos.FacturacionAnticiposLeer,
        },
        {
          label: 'Facturas de anticipo',
          description:
            'Bandeja de CFDIs de anticipo (serie FANT): detalle con saldo y cadena, descargas XML/PDF, cancelación y reintento.',
          to: '/facturacion/anticipos/facturas',
          icon: ReceiptText,
          permission: PermisosCanonicos.FacturacionAnticiposLeer,
        },
        {
          label: 'Complementos de pago pendientes',
          description: 'Revisión de pagos bancarios y emisión manual de complementos de pago.',
          to: '/facturacion/repp/pendientes',
          icon: HandCoins,
          permission: PermisosCanonicos.FacturacionFacturasLeer,
        },
        {
          label: 'Complementos de pago',
          description:
            'Recibos electrónicos de pago (Pago 2.0). Emisión multi-factura y consulta de facturas cubiertas.',
          to: '/facturacion/repp',
          icon: HandCoins,
          permissionsAny: [
            PermisosCanonicos.FacturacionReppEmitir,
            PermisosCanonicos.FacturacionFacturasLeer,
          ],
        },
        {
          label: 'Carta Porte',
          description:
            'Bandeja de Carta Portes 3.1. Captura de tramo (vehículo, operador, mercancías) y "Crear siguiente tramo".',
          to: '/facturacion/carta-porte',
          icon: Truck,
          permission: PermisosCanonicos.FacturacionCartaPorteLeer,
        },
        {
          label: 'Autorización de activos',
          description:
            'El Contador General autoriza la venta de activos fijos (valor neto + utilidad/pérdida) antes de timbrar.',
          to: '/facturacion/activos',
          icon: Building2,
          permission: PermisosCanonicos.FacturacionActivosAutorizar,
        },
        {
          label: 'Mi caja',
          description:
            'Sesión de efectivo del cajero: apertura con fondo, cobros de mostrador, movimientos y arqueo de cierre.',
          to: '/facturacion/caja',
          icon: Wallet,
          permissionsAny: [
            PermisosCanonicos.FacturacionCajaOperar,
            PermisosCanonicos.FacturacionCajaSupervisar,
            PermisosCanonicos.FacturacionCajaLiquidar,
          ],
        },
      ],
    },
    {
      label: 'Configuración',
      cards: [
        {
          label: 'Cajas',
          description:
            'Administra las cajas del módulo: alcance por sucursal × canal, cajeros con acceso y concesiones por usuario.',
          to: '/facturacion/cajas',
          icon: Wallet,
          permission: PermisosCanonicos.FacturacionCajaAdministrar,
        },
      ],
    },
    {
      label: 'Reportes',
      cards: [
        {
          label: 'Liquidación de caja',
          description:
            'Cierre de caja: facturado vs cobrado por cajero y fecha. Exportable PDF/Excel.',
          to: '/facturacion/reportes/liquidacion-caja',
          icon: Banknote,
          permission: PermisosCanonicos.FacturacionReportesLeer,
        },
        {
          label: 'Estados de facturas de anticipo',
          description:
            'Reporte de anticipos emitidos, facturas vinculadas, NCs de amortización y saldo por cliente.',
          to: '/facturacion/reportes/estados-anticipos',
          icon: BarChart3,
          permission: PermisosCanonicos.FacturacionReportesLeer,
        },
      ],
    },
  ],
};

const moduloContabilidad: NavModulo = {
  moduloId: 'contabilidad',
  label: 'Contabilidad',
  icon: BookOpen,
  secciones: [
    {
      label: 'Catálogo',
      cards: [
        {
          label: 'Catálogo de cuentas',
          description:
            'Árbol y lista de cuentas contables: consulta, alta, edición y baja lógica. Marca las cuentas pendientes de validación.',
          to: '/contabilidad/catalogo',
          icon: BookOpen,
          permission: PermisosCanonicos.ContabilidadCatalogoLeer,
        },
        {
          label: 'Importación del catálogo',
          description:
            'Carga de un archivo .csv/.xlsx en 3 pasos: perfilado de solo lectura, vista previa y aplicación idempotente.',
          to: '/contabilidad/importacion',
          icon: Upload,
          permission: PermisosCanonicos.ContabilidadCatalogoImportar,
        },
      ],
    },
    {
      label: 'Dimensiones',
      cards: [
        {
          label: 'Dimensiones contables',
          description:
            'Reglas de cuenta × tipo de documento × dimensión con vigencia, sucursales de cada centro de costo y tipos de documento.',
          to: '/contabilidad/dimensiones',
          icon: Layers,
          permission: PermisosCanonicos.ContabilidadDimensionesLeer,
        },
        {
          label: 'Probar movimientos',
          description:
            'Valida cuenta, tipo de documento y centros contra las reglas vigentes y registra movimientos de prueba.',
          to: '/contabilidad/movimientos-prueba',
          icon: FlaskConical,
          permission: PermisosCanonicos.ContabilidadMovimientosValidar,
        },
      ],
    },
    {
      label: 'Periodos',
      cards: [
        {
          label: 'Periodos contables',
          description:
            'Ejercicio con 12 periodos y el 13 de ajustes: apertura, cierre y reapertura con motivo y bitácora.',
          to: '/contabilidad/periodos',
          icon: CalendarRange,
          permission: PermisosCanonicos.ContabilidadPeriodoLeer,
        },
      ],
    },
  ],
};

const modulosBase: readonly NavModulo[] = [
  moduloFacturacion,
  moduloCuentasPorCobrar,
  moduloCompras,
  moduloAlmacen,
  moduloCuentasPorPagar,
  moduloTesoreria,
  moduloCentrosCosto,
  placeholderModulo('activos', 'Activos Fijos', Building2),
  moduloContabilidad,
  placeholderModulo('reportes', 'Reportes', BarChart3),
];

export const modulos: readonly NavModulo[] = modulosBase.map((modulo) => {
  const maestros = datosMaestrosAdminCards.filter((entry) =>
    modulo.moduloId === 'compras'
      ? ['datos-maestros-proveedores', 'datos-maestros-articulos'].includes(entry.id)
      : modulo.moduloId === 'facturacion'
        ? ['datos-maestros-clientes', 'datos-maestros-productos-aw'].includes(entry.id)
        : false,
  );
  return {
    ...modulo,
    secciones: modulo.secciones.map((section) =>
      section.label === 'Operación'
        ? {
            ...section,
            cards: [
              ...section.cards,
              ...maestros.map((entry) => ({
                label: entry.titulo,
                description: entry.descripcion,
                to: entry.href,
                icon: entry.icon,
                permission: entry.permisoRequerido,
              })),
            ],
          }
        : section,
    ),
  };
});
