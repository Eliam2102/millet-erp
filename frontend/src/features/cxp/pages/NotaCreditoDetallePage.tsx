import { Link, useParams } from '@tanstack/react-router';
import { ArrowLeft } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { ErrorState, TableSkeleton } from '@/components/erp';
import { useNotaCredito } from '@/features/cxp/api/useNotasYAnticipos';
import { TipoNotaCreditoLabels } from '@/features/cxp/api/types';
import { esApiError } from '@/lib/api';
import { EstadoNotaCreditoChip } from '@/features/cxp/components/EstadoChips';
import { Campo, Importe } from '@/features/cxp/components/DetalleCampos';
import {
  formatearFecha,
  formatearFechaHora,
} from '@/features/cxp/lib/formato';

/**
 * <c>P3 — Detalle de Nota de Crédito de proveedor</c> (serie detalles
 * CxP, backend PR #632). Vista read-only: encabezado con folio/UUID +
 * estado, importes desglosados, vínculos (factura origen, CFDI) y
 * trazabilidad de captura/match/cancelación.
 */
const FROM = '/_app/cxp/notas-credito/$id' as const;

export function NotaCreditoDetallePage() {
  const { id } = useParams({ from: FROM });
  const query = useNotaCredito(id);

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-2">
        <Button asChild variant="ghost" size="sm">
          <Link to="/cxp/notas-credito" search={{}}>
            <ArrowLeft className="mr-2 h-4 w-4" />
            Notas de crédito
          </Link>
        </Button>
      </div>

      {query.isError ? (
        <ErrorState
          title="No se pudo cargar la nota de crédito"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={6}
          columns={[
            { width: 'w-40' },
            { width: 'w-32' },
            { width: 'w-32' },
            { width: 'w-32' },
          ]}
        />
      ) : query.data == null ? null : (
        <div className="space-y-6">
          <header className="space-y-2">
            <div className="flex flex-wrap items-center gap-3">
              <h1 className="text-2xl font-semibold tracking-tight font-mono">
                {query.data.serieProveedor
                  ? `${query.data.serieProveedor}-${query.data.folioProveedor ?? ''}`
                  : (query.data.folioProveedor ?? 'Sin folio')}
              </h1>
              <EstadoNotaCreditoChip estado={query.data.estado} />
              <span className="inline-flex items-center rounded-full bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-700">
                {TipoNotaCreditoLabels[query.data.tipo]}
              </span>
            </div>
            <p className="text-sm text-muted-foreground">
              Fecha CFDI: {formatearFecha(query.data.fechaCfdi)} · Capturada:{' '}
              {formatearFechaHora(query.data.fechaCaptura)}
            </p>
          </header>

          <section className="grid grid-cols-1 gap-x-6 gap-y-2 rounded-md border p-4 md:grid-cols-2">
            <Campo
              label="Proveedor"
              valor={query.data.proveedorNombre ?? query.data.proveedorId}
              mono={query.data.proveedorNombre == null}
            />
            <Campo label="UUID CFDI" valor={query.data.uuidCfdi} mono />
            <Campo label="Moneda" valor={query.data.moneda} />
            <Campo
              label="Tipo de cambio"
              valor={
                query.data.tipoCambio != null
                  ? query.data.tipoCambio.toFixed(4)
                  : '—'
              }
            />
            <Campo
              label="Relación CFDI"
              valor={`Tipo 0${query.data.tipoRelacionCfdi}`}
            />
            <Campo
              label="UUID relacionado"
              valor={query.data.uuidRelacionCfdi}
              mono
            />
          </section>

          <section className="rounded-md border p-4">
            <h2 className="mb-2 text-sm font-semibold uppercase tracking-wide text-muted-foreground">
              Importes
            </h2>
            <dl className="grid grid-cols-2 gap-x-6 gap-y-1 text-sm sm:grid-cols-4">
              <Importe
                label="Subtotal"
                v={query.data.subtotal}
                m={query.data.moneda}
              />
              <Importe
                label="IVA trasladado"
                v={query.data.impuestosTrasladados}
                m={query.data.moneda}
              />
              <Importe
                label="Retenciones"
                v={query.data.retenciones}
                m={query.data.moneda}
              />
              <Importe
                label="Total"
                v={query.data.total}
                m={query.data.moneda}
                strong
              />
              <Importe
                label="Monto aplicado"
                v={query.data.montoAplicado}
                m={query.data.moneda}
              />
              <Importe
                label="Saldo por aplicar"
                v={query.data.saldoPorAplicar}
                m={query.data.moneda}
                strong
              />
            </dl>
          </section>

          <section className="rounded-md border p-4">
            <h2 className="mb-2 text-sm font-semibold uppercase tracking-wide text-muted-foreground">
              Vínculos
            </h2>
            <dl className="grid grid-cols-1 gap-x-6 gap-y-2 md:grid-cols-2">
              <div>
                <dt className="text-xs text-muted-foreground">
                  Factura origen
                </dt>
                <dd className="font-mono text-xs">
                  {query.data.facturaOrigenId != null ? (
                    <Link
                      to="/cxp/facturas/$id"
                      params={{ id: query.data.facturaOrigenId }}
                      className="text-primary underline-offset-2 hover:underline"
                    >
                      {query.data.facturaOrigenId}
                    </Link>
                  ) : (
                    '— (en espera de match)'
                  )}
                </dd>
              </div>
              <Campo
                label="CFDI recibido"
                valor={query.data.cfdiRecibidoId ?? '—'}
                mono
              />
            </dl>
          </section>

          <section className="rounded-md border p-4">
            <h2 className="mb-2 text-sm font-semibold uppercase tracking-wide text-muted-foreground">
              Trazabilidad
            </h2>
            <dl className="grid grid-cols-1 gap-x-6 gap-y-2 sm:grid-cols-2 md:grid-cols-3">
              <Campo
                label="Capturada"
                valor={formatearFechaHora(query.data.fechaCaptura)}
              />
              <Campo
                label="Capturado por"
                valor={query.data.capturadoPor ?? '—'}
                mono={query.data.capturadoPor != null}
              />
              <Campo
                label="Match con factura"
                valor={
                  query.data.fechaMatch != null
                    ? formatearFechaHora(query.data.fechaMatch)
                    : '—'
                }
              />
              <Campo
                label="Cancelada"
                valor={
                  query.data.fechaCancelacion != null
                    ? formatearFechaHora(query.data.fechaCancelacion)
                    : '—'
                }
              />
              <Campo
                label="Motivo cancelación"
                valor={query.data.motivoCancelacion ?? '—'}
              />
            </dl>
          </section>
        </div>
      )}
    </div>
  );
}
