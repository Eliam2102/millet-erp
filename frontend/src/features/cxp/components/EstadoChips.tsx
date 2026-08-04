import { cn } from '@/lib/utils';
import {
  EstadoAnticipo,
  EstadoAnticipoLabels,
  EstadoComprobacionGastos,
  EstadoComprobacionGastosLabels,
  EstadoCuentaTcStatus,
  EstadoCuentaTcStatusLabels,
  EstadoMovimientoTc,
  EstadoMovimientoTcLabels,
  EstadoNotaCargo,
  EstadoNotaCargoLabels,
  EstadoNotaCredito,
  EstadoNotaCreditoLabels,
  EstadoSolicitudViaticos,
  EstadoSolicitudViaticosLabels,
  EstadoTarjeta,
  EstadoTarjetaLabels,
} from '@/features/cxp/api/types';

/**
 * Chips de estado de los 3 recursos auxiliares CxP (NC, anticipo, nota
 * cargo). Comparten el mismo patrón visual que <c>EstadoPasivoChip</c>:
 * <c>rounded-full</c> + <c>px-2</c> + colores por estado.
 */

export function EstadoNotaCreditoChip({
  estado,
}: {
  estado: EstadoNotaCredito;
}) {
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
        estado === EstadoNotaCredito.EnEspera &&
          'bg-amber-100 text-amber-800',
        estado === EstadoNotaCredito.Abierta && 'bg-blue-100 text-blue-800',
        estado === EstadoNotaCredito.Aplicada &&
          'bg-emerald-100 text-emerald-800',
        estado === EstadoNotaCredito.Cancelada && 'bg-rose-100 text-rose-800',
      )}
    >
      {EstadoNotaCreditoLabels[estado]}
    </span>
  );
}

export function EstadoAnticipoChip({ estado }: { estado: EstadoAnticipo }) {
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
        estado === EstadoAnticipo.Abierto && 'bg-blue-100 text-blue-800',
        estado === EstadoAnticipo.Amortizado &&
          'bg-emerald-100 text-emerald-800',
        estado === EstadoAnticipo.Cancelado && 'bg-slate-200 text-slate-700',
      )}
    >
      {EstadoAnticipoLabels[estado]}
    </span>
  );
}

export function EstadoComprobacionChip({
  estado,
}: {
  estado: EstadoComprobacionGastos;
}) {
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
        estado === EstadoComprobacionGastos.Borrador &&
          'bg-slate-200 text-slate-700',
        estado === EstadoComprobacionGastos.PorRevisar &&
          'bg-amber-100 text-amber-800',
        estado === EstadoComprobacionGastos.AutorizadaNivel1 &&
          'bg-purple-100 text-purple-800',
        estado === EstadoComprobacionGastos.Autorizada &&
          'bg-blue-100 text-blue-800',
        estado === EstadoComprobacionGastos.Aplicada &&
          'bg-emerald-100 text-emerald-800',
        estado === EstadoComprobacionGastos.Rechazada &&
          'bg-rose-100 text-rose-800',
      )}
    >
      {EstadoComprobacionGastosLabels[estado]}
    </span>
  );
}

export function EstadoSolicitudViaticosChip({
  estado,
}: {
  estado: EstadoSolicitudViaticos;
}) {
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
        estado === EstadoSolicitudViaticos.Solicitada &&
          'bg-slate-200 text-slate-700',
        estado === EstadoSolicitudViaticos.AutorizadaPorJefe &&
          'bg-blue-100 text-blue-800',
        estado === EstadoSolicitudViaticos.RequiereDireccionFinanzas &&
          'bg-amber-100 text-amber-800',
        estado === EstadoSolicitudViaticos.AutorizadaCompleta &&
          'bg-purple-100 text-purple-800',
        estado === EstadoSolicitudViaticos.Anticipada &&
          'bg-cyan-100 text-cyan-800',
        estado === EstadoSolicitudViaticos.ComprobacionCapturada &&
          'bg-indigo-100 text-indigo-800',
        estado === EstadoSolicitudViaticos.Liquidada &&
          'bg-emerald-100 text-emerald-800',
        estado === EstadoSolicitudViaticos.Rechazada &&
          'bg-rose-100 text-rose-800',
      )}
    >
      {EstadoSolicitudViaticosLabels[estado]}
    </span>
  );
}

export function EstadoTarjetaChip({ estado }: { estado: EstadoTarjeta }) {
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
        estado === EstadoTarjeta.Activa &&
          'bg-emerald-100 text-emerald-800',
        estado === EstadoTarjeta.Bloqueada && 'bg-amber-100 text-amber-800',
        estado === EstadoTarjeta.Cancelada && 'bg-rose-100 text-rose-800',
      )}
    >
      {EstadoTarjetaLabels[estado]}
    </span>
  );
}

export function EstadoMovimientoTcChip({
  estado,
}: {
  estado: EstadoMovimientoTc;
}) {
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
        estado === EstadoMovimientoTc.Registrado &&
          'bg-blue-100 text-blue-800',
        estado === EstadoMovimientoTc.ConciliadoConEstadoCuenta &&
          'bg-indigo-100 text-indigo-800',
        estado === EstadoMovimientoTc.EnDisputa &&
          'bg-amber-100 text-amber-800',
        estado === EstadoMovimientoTc.Reversado && 'bg-rose-100 text-rose-800',
        estado === EstadoMovimientoTc.PagadoAlBanco &&
          'bg-emerald-100 text-emerald-800',
      )}
    >
      {EstadoMovimientoTcLabels[estado]}
    </span>
  );
}

export function EstadoCuentaTcChip({
  estado,
}: {
  estado: EstadoCuentaTcStatus;
}) {
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
        estado === EstadoCuentaTcStatus.EnConciliacion &&
          'bg-amber-100 text-amber-800',
        estado === EstadoCuentaTcStatus.Conciliado &&
          'bg-blue-100 text-blue-800',
        estado === EstadoCuentaTcStatus.Cerrado &&
          'bg-purple-100 text-purple-800',
        estado === EstadoCuentaTcStatus.PagadoBanco &&
          'bg-emerald-100 text-emerald-800',
      )}
    >
      {EstadoCuentaTcStatusLabels[estado]}
    </span>
  );
}

export function EstadoNotaCargoChip({ estado }: { estado: EstadoNotaCargo }) {
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
        estado === EstadoNotaCargo.Borrador && 'bg-slate-200 text-slate-700',
        estado === EstadoNotaCargo.Autorizada && 'bg-blue-100 text-blue-800',
        estado === EstadoNotaCargo.Aplicada &&
          'bg-emerald-100 text-emerald-800',
        estado === EstadoNotaCargo.Formalizada &&
          'bg-teal-100 text-teal-800',
        estado === EstadoNotaCargo.Cancelada && 'bg-rose-100 text-rose-800',
      )}
    >
      {EstadoNotaCargoLabels[estado]}
    </span>
  );
}
