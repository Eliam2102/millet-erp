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
