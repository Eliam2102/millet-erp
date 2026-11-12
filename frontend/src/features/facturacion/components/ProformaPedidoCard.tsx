import { Button } from '@/components/ui/button';
import type { ProformaPedidoResponse } from '@/features/facturacion/api/types';
import { formatDateTime } from '@/lib/datetime';
import { formatMoney } from '@/lib/money';
import { textoConformidadProforma, textoVigenciaProforma } from '@/features/facturacion/lib/proforma-vigencia';
import { CheckCircle2, Download, Eye } from 'lucide-react';

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
  onConformidad,
  onFacturar,
  facturando = false,
}: {
  proforma: ProformaPedidoResponse | null | undefined;
  cargando: boolean;
  descargando: boolean;
  onVer: (proforma: ProformaPedidoResponse) => void;
  onDescargar: (proforma: ProformaPedidoResponse) => void;
  /** M-234: abre «Registrar conformidad»; sin handler no se ofrece la acción. */
  onConformidad?: (proforma: ProformaPedidoResponse) => void;
  /** M-340: factura desde la versión aprobada, sin recaptura; sin handler no se ofrece la acción. */
  onFacturar?: (proforma: ProformaPedidoResponse) => void;
  facturando?: boolean;
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
            {proforma.generadaPor ? <> · {proforma.generadaPor}</> : null}
            {textoVigenciaProforma(proforma) ? (
              <span className={proforma.vencida ? ' font-medium text-warning-fg' : undefined}> · {textoVigenciaProforma(proforma)}</span>
            ) : null}{' '}·{' '}
            <span className="tabular-nums">
              {formatMoney({
                amount: proforma.contenido.totales.total,
                currency: proforma.contenido.moneda,
              })}
            </span>{' '}
            · Sin valor fiscal
            {proforma.conformidad && !proforma.conformidad.invalidada ? (
              <span className="block font-medium text-success-fg">{textoConformidadProforma(proforma)}</span>
            ) : null}
            {proforma.conformidad?.invalidada ? (
              <span role="status" className="block font-medium text-warning-fg">
                Conformidad invalidada: {proforma.conformidad.invalidadaMotivo ?? 'el pedido cambió.'} Genera una proforma
                nueva y registra su conformidad para facturar.
              </span>
            ) : null}
          </p>
        )}
      </div>
      {proforma != null && (
        <div className="flex items-center gap-2">
          {onConformidad && !proforma.conformidad && !proforma.vencida ? (
            <Button
              type="button"
              variant="outline"
              size="sm"
              aria-label={`Registrar conformidad de la proforma ${proforma.folio}`}
              onClick={() => onConformidad(proforma)}
            >
              <CheckCircle2 className="mr-1.5 h-3.5 w-3.5" strokeWidth={1.8} />
              Registrar conformidad
            </Button>
          ) : null}
          {onFacturar && proforma.conformidad && !proforma.conformidad.invalidada ? (
            <Button
              type="button"
              size="sm"
              disabled={facturando}
              aria-label={`Facturar desde la proforma ${proforma.folio}`}
              onClick={() => onFacturar(proforma)}
            >
              {facturando ? 'Facturando…' : 'Facturar desde esta proforma'}
            </Button>
          ) : null}
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
