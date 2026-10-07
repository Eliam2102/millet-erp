import { Badge } from '@/components/ui/badge';
import {
  EstadoAdjunto,
  EstadoExpediente,
} from '@/components/erp/adjuntos/api/types';

type Variante = 'success' | 'warning' | 'danger' | 'neutral';

const ADJUNTO: Record<EstadoAdjunto, { label: string; variant: Variante }> = {
  [EstadoAdjunto.Vigente]: { label: 'Vigente', variant: 'success' },
  [EstadoAdjunto.PorVencer]: { label: 'Por vencer', variant: 'warning' },
  [EstadoAdjunto.Vencido]: { label: 'Vencido', variant: 'danger' },
  [EstadoAdjunto.SinVigencia]: { label: 'Sin vigencia', variant: 'neutral' },
  [EstadoAdjunto.Baja]: { label: 'Dado de baja', variant: 'neutral' },
};

const EXPEDIENTE: Record<EstadoExpediente, { label: string; variant: Variante }> = {
  [EstadoExpediente.Faltante]: { label: 'Faltante', variant: 'warning' },
  [EstadoExpediente.Vigente]: { label: 'Vigente', variant: 'success' },
  [EstadoExpediente.PorVencer]: { label: 'Por vencer', variant: 'warning' },
  [EstadoExpediente.Vencido]: { label: 'Vencido', variant: 'danger' },
};

/** Badge de estado de un adjunto (el estado siempre lleva texto, DESIGN.md §8). */
export function EstadoAdjuntoBadge({ estado }: { estado: EstadoAdjunto }) {
  const e = ADJUNTO[estado];
  return e ? (
    <Badge variant={e.variant} data-estado={estado}>
      {e.label}
    </Badge>
  ) : null;
}

/** Badge del estado de un tipo de documento dentro del expediente. */
export function EstadoExpedienteBadge({ estado }: { estado: EstadoExpediente }) {
  const e = EXPEDIENTE[estado];
  return e ? (
    <Badge variant={e.variant} data-estado={estado}>
      {e.label}
    </Badge>
  ) : null;
}
