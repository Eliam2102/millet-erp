import { PageHeader } from '@/components/erp/PageHeader';
import { DocumentoP4Acciones } from '@/features/cxp/components/DocumentoP4Acciones';
import { Link, useParams, useSearch } from '@tanstack/react-router';
import { ArrowLeft } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { ErrorState, DetalleSkeleton } from '@/components/erp';
import { useNotaCredito } from '@/features/cxp/api/useNotasYAnticipos';
import { TipoNotaCreditoLabels } from '@/features/cxp/api/types';
import { esApiError } from '@/lib/api';
import { EstadoNotaCreditoChip } from '@/features/cxp/components/EstadoChips';
import { Campo, Importe } from '@/features/cxp/components/DetalleCampos';
import {
  formatearFechaHora,
} from '@/features/cxp/lib/formato';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { formatCantidad } from '@/lib/format-cantidad';

/**
 * <c>P3 — Detalle de Nota de Crédito de proveedor</c> (serie detalles
 * CxP, backend PR #632). Vista read-only: encabezado con folio/UUID +
 * estado, importes desglosados, vínculos (factura origen, CFDI) y
 * trazabilidad de captura/match/cancelación.
 */
const FROM = '/_app/cxp/notas-credito/$id' as const;

export function NotaCreditoDetallePage() {
  const { id } = useParams({ from: FROM });
  const search = useSearch({ from: FROM });
  const query = useNotaCredito(id);

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-2">
        <Button asChild variant="ghost" size="sm">
          <Link to="/cxp/notas-credito" search={search}>
            <ArrowLeft className="mr-2 size-3.5" />
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
        <DetalleSkeleton />
      ) : query.data == null ? null : (
        <div className="space-y-6">
          <PageHeader
            titulo={
              <>
                {query.data.serieProveedor ? `${query.data.serieProveedor}-${query.data.folioProveedor ?? ''}` : (query.data.folioProveedor ?? 'Sin folio')}
              </>
            }
            estado={
              <>
                <EstadoNotaCreditoChip estado={query.data.estado} />
                <span className="inline-flex items-center rounded-full bg-neutral-bg px-2 py-0.5 text-xs font-medium text-neutral-fg">
                  {TipoNotaCreditoLabels[query.data.tipo]}
                </span>
              </>
            }
            descripcion={
              <>
                Fecha CFDI: <DateTimeDisplay value={query.data.fechaCfdi} variant="media" /> · Capturada:{' '}
                {formatearFechaHora(query.data.fechaCaptura)}
              </>
            }
            mono
          />

          <DocumentoP4Acciones tipo="notas-credito" documento={query.data} />

          <dl className="grid grid-cols-1 gap-x-6 gap-y-2 rounded-md border p-4 @xl:grid-cols-2 @3xl:grid-cols-4 @5xl:grid-cols-6">
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
                  ? formatCantidad(query.data.tipoCambio, 4)
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
          </dl>

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
                <dd className="text-xs">
                  {query.data.facturaOrigenId != null ? (
                    <Link
                      to="/cxp/facturas/$id"
                      params={{ id: query.data.facturaOrigenId }}
                      className="text-primary underline-offset-2 hover:underline"
                    >
                      Ver factura origen
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
