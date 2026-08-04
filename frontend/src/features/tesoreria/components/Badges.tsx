import { Badge } from '@/components/ui/badge';
import {
  ESTADO_APLICACION_LABELS,
  ESTADO_CONCILIACION_LABELS,
  ESTADO_DEPOSITO_LABELS,
  EstadoAplicacionMovimiento,
  EstadoConciliacionMovimiento,
  EstadoDepositoConfirmacion,
  SENTIDO_MOVIMIENTO_LABELS,
  SentidoMovimiento,
} from '@/features/tesoreria/api/types';
import { cn } from '@/lib/utils';

/** Chip de sentido del movimiento (verde ingreso / rojo egreso). */
export function SentidoBadge({ sentido }: { sentido: SentidoMovimiento }) {
  return (
    <Badge
      variant="outline"
      className={cn(
        sentido === SentidoMovimiento.Ingreso
          ? 'border-emerald-300 bg-emerald-50 text-emerald-700'
          : 'border-rose-300 bg-rose-50 text-rose-700',
      )}
    >
      {SENTIDO_MOVIMIENTO_LABELS[sentido]}
    </Badge>
  );
}

/** Chip del estado de aplicación (§4.2: NoAplicado / Parcial / Aplicado). */
export function EstadoAplicacionBadge({
  estado,
}: {
  estado: EstadoAplicacionMovimiento;
}) {
  return (
    <Badge
      variant="outline"
      className={cn(
        estado === EstadoAplicacionMovimiento.Aplicado &&
          'border-emerald-300 bg-emerald-50 text-emerald-700',
        estado === EstadoAplicacionMovimiento.AplicadoParcial &&
          'border-amber-300 bg-amber-50 text-amber-700',
        estado === EstadoAplicacionMovimiento.NoAplicado &&
          'border-slate-300 bg-slate-50 text-slate-600',
      )}
    >
      {ESTADO_APLICACION_LABELS[estado]}
    </Badge>
  );
}

/** Chip del ciclo Pendiente/Confirmada/Rechazada del depósito (TES-FE-PR4b). */
export function EstadoDepositoBadge({
  estado,
}: {
  estado: EstadoDepositoConfirmacion;
}) {
  return (
    <Badge
      variant="outline"
      className={cn(
        estado === EstadoDepositoConfirmacion.Confirmada &&
          'border-emerald-300 bg-emerald-50 text-emerald-700',
        estado === EstadoDepositoConfirmacion.Pendiente &&
          'border-amber-300 bg-amber-50 text-amber-700',
        estado === EstadoDepositoConfirmacion.Rechazada &&
          'border-rose-300 bg-rose-50 text-rose-700',
      )}
    >
      {ESTADO_DEPOSITO_LABELS[estado]}
    </Badge>
  );
}

/**
 * Chip del ciclo FISCAL del depósito confirmado (TES-FE-PR4b): confirmar
 * registra el hecho bancario; el REPP lo emite Facturación y este badge
 * refleja el timbrado — son dos momentos distintos a propósito.
 */
export function ReppTimbradoBadge({ timbrado }: { timbrado: boolean }) {
  return (
    <Badge
      variant="outline"
      className={cn(
        timbrado
          ? 'border-emerald-300 bg-emerald-50 text-emerald-700'
          : 'border-slate-300 bg-slate-50 text-slate-600',
      )}
    >
      {timbrado ? 'REPP timbrado' : 'REPP pendiente'}
    </Badge>
  );
}

/** Chip del estado de conciliación contra extracto. */
export function EstadoConciliacionBadge({
  estado,
}: {
  estado: EstadoConciliacionMovimiento;
}) {
  return (
    <Badge
      variant="outline"
      className={cn(
        estado === EstadoConciliacionMovimiento.Conciliado
          ? 'border-emerald-300 bg-emerald-50 text-emerald-700'
          : 'border-slate-300 bg-slate-50 text-slate-600',
      )}
    >
      {ESTADO_CONCILIACION_LABELS[estado]}
    </Badge>
  );
}
