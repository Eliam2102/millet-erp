import { cn } from '@/lib/utils';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import {
  EstatusSatCfdi,
  EstatusSatCfdiLabels,
} from '@/features/cxp/api/types';

/**
 * Badge del estatus del CFDI ante el SAT. El significado siempre va en texto
 * (Cancelado incluye la fecha); sin dato se muestra "Sin dato".
 */
export function EstatusSatBadge({
  estatus,
  fechaCancelacion,
}: {
  estatus?: EstatusSatCfdi | null;
  fechaCancelacion?: string | null;
}) {
  const label = estatus != null ? EstatusSatCfdiLabels[estatus] : undefined;
  if (!label) {
    return <span className="text-xs text-muted-foreground">Sin dato</span>;
  }
  const cancelado = estatus === EstatusSatCfdi.Cancelado;
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium',
        cancelado ? 'bg-danger-bg text-danger-fg' : 'bg-success-bg text-success-fg',
      )}
    >
      {label}
      {cancelado && fechaCancelacion ? (
        <DateTimeDisplay value={fechaCancelacion} variant="corta" />
      ) : null}
    </span>
  );
}
