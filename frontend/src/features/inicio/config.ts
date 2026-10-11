import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { rutaPermitida } from '@/lib/nav';

export type ConteoId =
  | 'requisiciones'
  | 'ordenes'
  | 'abiertas'
  | 'valesVencidos'
  | 'valesPorVencer'
  | 'recepciones'
  | 'proveedores'
  | 'facturas'
  | 'pagosCuenta'
  | 'depositos'
  | 'alertas';
export interface PendienteConfig {
  id: string;
  modulo: string;
  titulo: string;
  descripcion: string;
  to: string;
  search?: { estado?: 0 | 2; pendientes?: boolean; nivelPendiente?: 1 | 2; soloVales?: boolean; noRegularizados?: boolean; soloVencidos?: boolean; soloPorVencer?: boolean };
  conteo?: ConteoId;
  periodo?: boolean;
  permisos?: readonly string[];
  permisosAlguno?: readonly string[];
}

export const pendientes: readonly PendienteConfig[] = [
  {
    id: 'requisiciones',
    modulo: 'Compras',
    titulo: 'Requisiciones por autorizar',
    descripcion: 'Solicitudes que requieren autorización.',
    to: '/compras/pendientes',
    conteo: 'requisiciones',
    permisos: [P.ComprasRequisicionesLeer],
    permisosAlguno: [P.ComprasRequisicionesAutorizarNivel1, P.ComprasRequisicionesAutorizarNivel2],
  },
  {
    id: 'ordenes',
    modulo: 'Compras',
    titulo: 'OC por autorizar',
    descripcion: 'Órdenes que requieren autorización.',
    to: '/compras/ordenes/pendientes-autorizacion',
    conteo: 'ordenes',
    permisos: [P.ComprasOrdenesLeer],
    permisosAlguno: [P.ComprasOrdenesAutorizarNivel1, P.ComprasOrdenesAutorizarNivel2],
  },
  {
    id: 'abiertas',
    modulo: 'Compras',
    titulo: 'Partidas abiertas',
    descripcion: 'Partidas de compra con operaciones pendientes.',
    to: '/compras/ordenes/partidas-abiertas',
    conteo: 'abiertas',
    permisos: [P.ComprasOrdenesReportesPartidasAbiertas],
  },
  {
    id: 'recepciones',
    modulo: 'Almacén',
    titulo: 'Recepciones en borrador',
    descripcion: 'Recepciones por completar y registrar.',
    to: '/almacen/recepciones',
    search: { estado: 0 },
    conteo: 'recepciones',
    permisos: [P.AlmacenEntradasLeer],
  },
  {
    id: 'valesVencidos', modulo: 'Almacén', titulo: 'Vales vencidos sin regularizar',
    descripcion: 'Vincula una requisición autorizada para regularizar la entrega.',
    to: '/almacen/salidas', conteo: 'valesVencidos',
    search: { soloVales: true, noRegularizados: true, soloVencidos: true },
    permisos: [P.AlmacenSalidasLeerTodas],
  },
  {
    id: 'valesPorVencer', modulo: 'Almacén', titulo: 'Vales por vencer en 24 horas',
    descripcion: 'Regulariza estos vales antes de su fecha límite.',
    to: '/almacen/salidas', conteo: 'valesPorVencer',
    search: { soloVales: true, noRegularizados: true, soloPorVencer: true },
    permisos: [P.AlmacenSalidasLeerTodas],
  },
  {
    id: 'proveedores',
    modulo: 'Proveedores',
    titulo: 'Proveedores en revisión',
    descripcion: 'Proveedores pendientes de validar por CxP.',
    to: '/admin/datos-maestros/proveedores',
    conteo: 'proveedores',
    permisos: [P.DatosMaestrosProveedoresGestionar, P.DatosMaestrosProveedoresValidar],
  },
  {
    id: 'facturas',
    modulo: 'CxP',
    titulo: 'Facturas en revisión',
    descripcion: 'Facturas de proveedor que requieren revisión.',
    to: '/cxp/facturas',
    search: { estado: 2 },
    conteo: 'facturas',
    permisos: [P.CuentasPorPagarFacturasLeer],
  },
  {
    id: 'pagosCuenta',
    modulo: 'Tesorería',
    titulo: 'Pagos a cuenta por aplicar',
    descripcion: 'Pagos abiertos o parcialmente aplicados.',
    to: '/tesoreria/pagos-cuenta',
    conteo: 'pagosCuenta',
    permisos: [P.TesoreriaMovimientosVer],
  },
  {
    id: 'depositos',
    modulo: 'Tesorería',
    titulo: 'Depósitos por confirmar',
    descripcion: 'Depósitos pendientes de confirmación.',
    to: '/tesoreria/depositos',
    conteo: 'depositos',
    permisos: [P.TesoreriaDepositosConfirmar],
  },
  {
    id: 'alertas',
    modulo: 'CxC',
    titulo: 'Alertas de cartera pendientes',
    descripcion: 'Alertas de cartera aún no atendidas.',
    to: '/cxc/alertas',
    search: { pendientes: true },
    conteo: 'alertas',
    permisos: [P.CuentasPorCobrarCarteraLeer],
  },
  {
    id: 'reabasto',
    modulo: 'Almacén',
    titulo: 'Reabasto',
    descripcion: 'Consulta la configuración de reabasto.',
    to: '/almacen/reorden',
  },
  {
    id: 'vencidas',
    modulo: 'CxP',
    titulo: 'Facturas vencidas',
    descripcion: 'Consulta vencimientos en la bandeja de facturas.',
    to: '/cxp/facturas',
  },
  {
    id: 'cartera',
    modulo: 'CxC',
    titulo: 'Cartera vencida',
    descripcion: 'Consulta la antigüedad de saldos de clientes.',
    to: '/cxc/cartera',
  },
  {
    id: 'liberaciones',
    modulo: 'CxC',
    titulo: 'Liberaciones',
    descripcion: 'Consulta las decisiones de liberación.',
    to: '/cxc/liberaciones',
  },
  {
    id: 'pedidos',
    modulo: 'Facturación',
    titulo: 'Pedidos por facturar',
    descripcion: 'Revisa los pedidos disponibles para facturar.',
    to: '/facturacion/pedidos',
  },
  {
    id: 'excepciones',
    modulo: 'Facturación',
    titulo: 'Excepciones de importación',
    descripcion: 'Revisa las excepciones de pedidos importados.',
    to: '/facturacion/pedidos/excepciones',
  },
  {
    id: 'periodo',
    modulo: 'Contabilidad',
    titulo: 'Periodo actual',
    descripcion: 'Estado del periodo contable del mes en curso.',
    to: '/contabilidad/periodos',
    periodo: true,
    permisos: [P.ContabilidadPeriodoLeer],
  },
];

export function pendienteVisible(fila: PendienteConfig, permisos: readonly string[]) {
  return (
    rutaPermitida(fila.to, permisos) &&
    (fila.permisos?.every((p) => permisos.includes(p)) ?? true) &&
    (fila.permisosAlguno?.some((p) => permisos.includes(p)) ?? true)
  );
}

export const prioridadAccesos = [
  '/compras/requisiciones',
  '/compras/ordenes',
  '/almacen/recepciones',
  '/cxp/facturas',
  '/tesoreria/pagos',
  '/tesoreria/depositos',
  '/cxc/cartera',
  '/facturacion/pedidos',
  '/contabilidad/periodos',
];

export interface FuenteInicio {
  endpoint: string;
  permisos: readonly string[];
  alguno?: readonly string[];
  excluirSi?: string;
}
export interface PiezaInicio {
  id: string;
  tipo: 'indicador' | 'visual';
  titulo: string;
  to: string;
  prioridad: number;
  moneda?: boolean;
  fuentes: readonly FuenteInicio[];
}
const fuente = (endpoint: string, ...permisos: string[]): FuenteInicio => ({ endpoint: `/api/v1/${endpoint}`, permisos });
/** Registro único: permiso de cada fuente, composición y prioridad. No depende del nombre del rol. */
export const piezasInicio: readonly PiezaInicio[] = [
  { id: 'autorizar', tipo: 'indicador', titulo: 'Por autorizar', to: '/compras/pendientes', prioridad: 100, fuentes: [
    { ...fuente('compras/inicio/autorizar-rq', P.ComprasRequisicionesLeer), alguno: [P.ComprasRequisicionesAutorizarNivel1, P.ComprasRequisicionesAutorizarNivel2] },
    { ...fuente('compras/inicio/autorizar-oc', P.ComprasOrdenesLeer), alguno: [P.ComprasOrdenesAutorizarNivel1, P.ComprasOrdenesAutorizarNivel2] },
  ] },
  { id: 'requisicionesPropias', tipo: 'indicador', titulo: 'Tus requisiciones', to: '/compras/requisiciones', prioridad: 70, fuentes: [fuente('compras/inicio/requisiciones', P.ComprasRequisicionesLeer)] },
  { id: 'recepciones', tipo: 'indicador', titulo: 'Recepciones pendientes', to: '/almacen/recepciones', prioridad: 80, fuentes: [
    fuente('almacen/inicio/recepciones', P.AlmacenEntradasLeer),
    { ...fuente('compras/inicio/recepciones', P.ComprasOrdenesLeer), excluirSi: P.AlmacenEntradasLeer },
  ] },
  { id: 'conteos', tipo: 'indicador', titulo: 'Conteos abiertos', to: '/almacen/conteos', prioridad: 70, fuentes: [fuente('almacen/inicio/conteos', P.AlmacenInventariosLeer)] },
  { id: 'vales', tipo: 'indicador', titulo: 'Vales por regularizar', to: '/almacen/salidas', prioridad: 90, fuentes: [fuente('almacen/inicio/vales', P.AlmacenSalidasLeerTodas)] },
  { id: 'facturas', tipo: 'indicador', titulo: 'Facturas de proveedor en revisión', to: '/cxp/facturas', prioridad: 85, fuentes: [fuente('cuentas-por-pagar/inicio/facturas', P.CuentasPorPagarFacturasLeer)] },
  { id: 'pagos', tipo: 'indicador', titulo: 'Por pagar esta semana', to: '/tesoreria/pasivos', prioridad: 95, moneda: true, fuentes: [fuente('tesoreria/inicio/pagos', P.TesoreriaPasivosVer)] },
  { id: 'bancos', tipo: 'indicador', titulo: 'Saldo en bancos', to: '/tesoreria/cuentas', prioridad: 65, moneda: true, fuentes: [fuente('tesoreria/inicio/bancos', P.TesoreriaCuentasVer)] },
  { id: 'cartera', tipo: 'indicador', titulo: 'Por cobrar vencido', to: '/cxc/cartera', prioridad: 95, moneda: true, fuentes: [fuente('cuentas-por-cobrar/inicio/vencido', P.CuentasPorCobrarCarteraLeer)] },
  // No hay pedidos.leer/comprobantes.leer canónicos: facturas.leer es la lectura fiscal más cercana.
  { id: 'pedidos', tipo: 'indicador', titulo: 'Pedidos por facturar', to: '/facturacion/pedidos', prioridad: 75, fuentes: [fuente('facturacion/inicio/pedidos', P.FacturacionFacturasLeer)] },
  { id: 'timbrado', tipo: 'indicador', titulo: 'Timbrado con problema', to: '/facturacion/comprobantes', prioridad: 100, fuentes: [fuente('facturacion/inicio/timbrado', P.FacturacionFacturasLeer)] },
  // El recurso no tiene un permiso de lectura separado; coincide con la bandeja de depósitos.
  { id: 'depositos', tipo: 'indicador', titulo: 'Depósitos por confirmar', to: '/tesoreria/depositos', prioridad: 80, fuentes: [fuente('tesoreria/inicio/depositos', P.TesoreriaDepositosConfirmar)] },
  { id: 'periodo', tipo: 'indicador', titulo: 'Periodo contable', to: '/contabilidad/periodos', prioridad: 60, fuentes: [fuente('contabilidad/inicio/periodo', P.ContabilidadPeriodoLeer)] },
  { id: 'compra', tipo: 'visual', titulo: 'Circuito de compra', to: '/compras/ordenes', prioridad: 80, fuentes: [fuente('compras/inicio/circuito', P.ComprasOrdenesLeer)] },
  { id: 'venta', tipo: 'visual', titulo: 'Circuito de venta', to: '/cxc/cartera', prioridad: 80, fuentes: [
    fuente('facturacion/inicio/circuito', P.FacturacionFacturasLeer),
    fuente('cuentas-por-cobrar/inicio/circuito', P.CuentasPorCobrarCarteraLeer),
  ] },
  { id: 'flujo', tipo: 'visual', titulo: 'Flujo de la semana', to: '/tesoreria/movimientos', prioridad: 90, fuentes: [fuente('tesoreria/inicio/flujo', P.TesoreriaMovimientosVer)] },
];
export function fuentePermitida(f: FuenteInicio, permisos: readonly string[]) {
  return f.permisos.every(p => permisos.includes(p)) && (!f.alguno || f.alguno.some(p => permisos.includes(p))) && (!f.excluirSi || !permisos.includes(f.excluirSi));
}
export function piezasPermitidas(permisos: readonly string[]) {
  return piezasInicio.flatMap(p => {
    const fuentes = p.fuentes.filter(f => fuentePermitida(f, permisos));
    return fuentes.length ? [{ ...p, fuentes }] : [];
  });
}

export const accionesInicio = [
  { id: 'rq', titulo: 'Requisición', permiso: P.ComprasRequisicionesCrear },
  { id: 'oc', titulo: 'Orden de compra', permiso: P.ComprasOrdenesCrear },
  { id: 'recepcion', titulo: 'Recepción', permiso: P.AlmacenEntradasCapturar },
  { id: 'factura', titulo: 'Factura de proveedor', permiso: P.CuentasPorPagarFacturasCapturar },
  { id: 'gestion', titulo: 'Gestión de cobranza', permiso: P.CuentasPorCobrarCobranzaRegistrar },
] as const;
