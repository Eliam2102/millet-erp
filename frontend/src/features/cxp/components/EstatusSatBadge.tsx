import { Badge } from '@/components/ui/badge';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import {
  EstatusSatCfdi,
  EstatusSatCfdiLabels,
} from '@/features/cxp/api/types';

/**
 * Badge del estatus del CFDI ante el SAT. El significado siempre va en texto
 * (Cancelado incluye la fecha); sin dato se muestra «—».
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
    return <span className="text-xs text-ink-muted">—</span>;
  }
  const cancelado = estatus === EstatusSatCfdi.Cancelado;
  return (
    <Badge variant={cancelado ? 'danger' : 'success'} className="gap-1">
      {label}
      {cancelado && fechaCancelacion ? (
        <>
          {' '}
          <span aria-hidden="true">·</span>
          <DateTimeDisplay value={fechaCancelacion} variant="corta" />
        </>
      ) : null}
    </Badge>
  );
}
