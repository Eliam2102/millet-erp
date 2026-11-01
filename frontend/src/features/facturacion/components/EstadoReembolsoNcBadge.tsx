import { Badge } from '@/components/ui/badge';
import type { EstadoReembolsoNc } from '@/features/facturacion/api/types';

const VARIANTE: Record<EstadoReembolsoNc, 'info' | 'success' | 'danger' | 'neutral'> = {
  Solicitado: 'info',
  Autorizado: 'info',
  Rechazado: 'danger',
  Cancelado: 'neutral',
  Pagado: 'success',
};

/** «2026-10-31» → «31 oct 2026», sin desfase de zona horaria. */
function formatearFechaCorta(fecha: string): string {
  const [anio, mes, dia] = fecha.split('-').map(Number);
  return new Date(anio, mes - 1, dia).toLocaleDateString('es-MX', {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
  });
}

/**
 * F6.3: estado del reembolso de una NC según Tesorería. «Pagado» lleva la fecha valor y la referencia del pago;
 * «Rechazado» y «Cancelado», el motivo. Sin estado (NC que pidió reembolso antes de F6.3) se muestra «Solicitado».
 */
export function EstadoReembolsoNcBadge({
  folio,
  estado,
  pagadoEl,
  referencia,
  motivo,
}: {
  folio: string;
  estado: EstadoReembolsoNc | null;
  pagadoEl?: string | null;
  referencia?: string | null;
  motivo?: string | null;
}) {
  const actual: EstadoReembolsoNc = estado ?? 'Solicitado';
  const detalle =
    actual === 'Pagado'
      ? [pagadoEl && formatearFechaCorta(pagadoEl), referencia].filter(Boolean).join(' · ')
      : actual === 'Rechazado' || actual === 'Cancelado'
        ? (motivo ?? '')
        : '';
  return (
    <span className="inline-flex flex-col gap-0.5" data-testid={`reembolso-nc-${folio}`}>
      <Badge variant={VARIANTE[actual]}>{actual}</Badge>
      {detalle && <span className="text-xs text-ink-muted">{detalle}</span>}
    </span>
  );
}
