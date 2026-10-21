import { Badge } from '@/components/ui/badge';
import {
  DisponibilidadPendiente,
  type DisponibilidadPendiente as DisponibilidadPendienteValor,
} from '@/features/compras/api/types';

/**
 * Badge de disponibilidad de almacén de una RQ pendiente (S2). Colores por
 * variante semántica del design system (sin hex ni paleta default).
 */
const DISPONIBILIDAD: Record<
  DisponibilidadPendienteValor,
  { etiqueta: string; variant: 'success' | 'warning' | 'danger' | 'neutral' }
> = {
  [DisponibilidadPendiente.Total]: { etiqueta: 'Stock total', variant: 'success' },
  [DisponibilidadPendiente.Parcial]: { etiqueta: 'Stock parcial', variant: 'warning' },
  [DisponibilidadPendiente.Nula]: { etiqueta: 'Sin stock', variant: 'danger' },
  [DisponibilidadPendiente.SinDato]: { etiqueta: 'Sin datos', variant: 'neutral' },
};

export function DisponibilidadBadge({
  disponibilidad,
}: {
  disponibilidad: DisponibilidadPendienteValor;
}) {
  const { etiqueta, variant } =
    DISPONIBILIDAD[disponibilidad] ?? DISPONIBILIDAD[DisponibilidadPendiente.SinDato];
  return (
    <Badge variant={variant} className="font-medium">
      {etiqueta}
    </Badge>
  );
}

/**
 * Badge de recurrencia: `true` = "Recurrente", `false` = "No recurrente";
 * `null`/`undefined` (proyección sin dato) no se pinta.
 */
export function RecurrenteBadge({ esRecurrente }: { esRecurrente?: boolean | null }) {
  if (esRecurrente == null) return null;
  return (
    <Badge variant={esRecurrente ? 'info' : 'neutral'} className="font-medium">
      {esRecurrente ? 'Recurrente' : 'No recurrente'}
    </Badge>
  );
}
