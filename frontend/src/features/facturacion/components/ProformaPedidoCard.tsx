import { Button } from '@/components/ui/button';
import type { ProformaPedidoResponse } from '@/features/facturacion/api/types';
import { formatDateTime } from '@/lib/datetime';
import { formatMoney } from '@/lib/money';
import { Download, Eye } from 'lucide-react';

/**
 * Tarjeta «Proforma» del detalle del pedido (U1.8a): última versión
 * generada (folio PF, fecha, usuario, total), con «Ver» (visor en pantalla)
 * y «Descargar». La proforma no tiene valor fiscal ni consume folio.
 */
export function ProformaPedidoCard({
  proforma,
  cargando,
  descargando,
  onVer,
  onDescargar,
}: {
  proforma: ProformaPedidoResponse | null | undefined;
  cargando: boolean;
  descargando: boolean;
  onVer: (proforma: ProformaPedidoResponse) => void;
  onDescargar: (proforma: ProformaPedidoResponse) => void;
}) {
  return (
    <section
      aria-label="Proforma del pedido"
      data-print="hidden"
      className="flex flex-wrap items-center justify-between gap-3 rounded-lg bg-surface-card px-4 py-3 shadow-card-flat"
    >
      <div className="space-y-0.5">
        <h2 className="text-sm font-semibold text-ink">Proforma</h2>
        {cargando ? (
          <div className="h-4 w-56 animate-pulse rounded bg-surface-muted" />
        ) : proforma == null ? (
          <p className="text-xs text-ink-muted">
            El pedido aún no tiene proforma. Genérala para enviar al cliente una versión sin valor
            fiscal antes de timbrar.
          </p>
        ) : (
          <p className="text-xs text-ink-muted">
            Última proforma{' '}
            <span className="font-mono text-sm font-medium text-ink">{proforma.folio}</span> ·{' '}
            {formatDateTime(proforma.generadaEn)}
            {proforma.generadaPor ? <> · {proforma.generadaPor}</> : null} ·{' '}
            <span className="tabular-nums">
              {formatMoney({
                amount: proforma.contenido.totales.total,
                currency: proforma.contenido.moneda,
              })}
            </span>{' '}
            · Sin valor fiscal
          </p>
        )}
      </div>
      {proforma != null && (
        <div className="flex items-center gap-2">
          <Button
            type="button"
            variant="outline"
            size="sm"
            aria-label={`Ver proforma ${proforma.folio}`}
            onClick={() => onVer(proforma)}
          >
            <Eye className="mr-1.5 h-3.5 w-3.5" strokeWidth={1.8} />
            Ver
          </Button>
          <Button
            type="button"
            variant="outline"
            size="sm"
            disabled={descargando}
            aria-label={`Descargar proforma ${proforma.folio}`}
            onClick={() => onDescargar(proforma)}
          >
            <Download className="mr-1.5 h-3.5 w-3.5" strokeWidth={1.8} />
            {descargando ? 'Generando…' : 'Descargar'}
          </Button>
        </div>
      )}
    </section>
  );
}
