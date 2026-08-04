import { Link, useParams } from '@tanstack/react-router';
import { AlertTriangle, ArrowLeft } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { ErrorState, TableSkeleton } from '@/components/erp';
import { useSolicitudViaticos } from '@/features/cxp/api/useViaticos';
import { TipoDestinoViaticoLabels } from '@/features/cxp/api/types';
import { esApiError } from '@/lib/api';
import { EstadoSolicitudViaticosChip } from '@/features/cxp/components/EstadoChips';
import { Campo, Importe } from '@/features/cxp/components/DetalleCampos';
import {
  formatearFechaHora,
  formatearMonto,
} from '@/features/cxp/lib/formato';

/**
 * <c>P3 — Detalle de Solicitud de Viáticos</c> (serie detalles CxP,
 * backend PR #632). Vista read-only: encabezado con destino + estado,
 * política aplicada (tope snapshot), montos del ciclo, trazabilidad de
 * firmas (jefe + DF) y tabla de líneas de la comprobación capturada.
 */
const FROM = '/_app/cxp/viaticos/$id' as const;

export function ViaticoDetallePage() {
  const { id } = useParams({ from: FROM });
  const query = useSolicitudViaticos(id);

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-2">
        <Button asChild variant="ghost" size="sm">
          <Link to="/cxp/viaticos" search={{}}>
            <ArrowLeft className="mr-2 h-4 w-4" />
            Viáticos
          </Link>
        </Button>
      </div>

      {query.isError ? (
        <ErrorState
          title="No se pudo cargar la solicitud de viáticos"
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
              <h1 className="text-2xl font-semibold tracking-tight">
                {query.data.destino}
              </h1>
              <EstadoSolicitudViaticosChip estado={query.data.estado} />
              <span className="inline-flex items-center rounded-full bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-700">
                {TipoDestinoViaticoLabels[query.data.tipoDestino]}
              </span>
              {query.data.excedePolitica && (
                <span className="inline-flex items-center rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-800">
                  <AlertTriangle className="mr-1 h-3 w-3" />
                  Excede política
                </span>
              )}
            </div>
            <p className="text-sm text-muted-foreground">
              {query.data.fechaSalida} → {query.data.fechaRegreso} (
              {query.data.diasEstimados} días) · Solicitada:{' '}
              {formatearFechaHora(query.data.fechaSolicitud)}
            </p>
          </header>

          <section className="grid grid-cols-1 gap-x-6 gap-y-2 rounded-md border p-4 md:grid-cols-2">
            <Campo
              label="Empleado"
              valor={query.data.empleadoNombre ?? query.data.empleadoId}
              mono={query.data.empleadoNombre == null}
            />
            <Campo
              label="Puesto"
              valor={query.data.puestoNombre ?? query.data.puestoId}
              mono={query.data.puestoNombre == null}
            />
            <Campo
              label="Jefe directo"
              valor={query.data.jefeDirectoNombre ?? query.data.jefeDirectoId}
              mono={query.data.jefeDirectoNombre == null}
            />
            <Campo label="Moneda" valor={query.data.moneda} />
            {query.data.justificacionExceso != null && (
              <div className="md:col-span-2">
                <dt className="text-xs text-muted-foreground">
                  Justificación del exceso
                </dt>
                <dd className="text-sm">{query.data.justificacionExceso}</dd>
              </div>
            )}
          </section>

          <section className="rounded-md border p-4">
            <h2 className="mb-2 text-sm font-semibold uppercase tracking-wide text-muted-foreground">
              Importes
            </h2>
            <dl className="grid grid-cols-2 gap-x-6 gap-y-1 text-sm sm:grid-cols-4">
              <Importe
                label="Monto solicitado"
                v={query.data.montoSolicitado}
                m={query.data.moneda}
                strong
              />
              <Importe
                label="Tope de política"
                v={query.data.topePolitica}
                m={query.data.moneda}
              />
              {query.data.montoComprobado != null && (
                <Importe
                  label="Monto comprobado"
                  v={query.data.montoComprobado}
                  m={query.data.moneda}
                />
              )}
              {query.data.diferenciaLiquidacion != null && (
                <Importe
                  label="Diferencia liquidación"
                  v={query.data.diferenciaLiquidacion}
                  m={query.data.moneda}
                  strong
                />
              )}
            </dl>
          </section>

          <section className="rounded-md border p-4">
            <h2 className="mb-2 text-sm font-semibold uppercase tracking-wide text-muted-foreground">
              Trazabilidad
            </h2>
            <dl className="grid grid-cols-1 gap-x-6 gap-y-2 sm:grid-cols-2 md:grid-cols-3">
              <Campo
                label="Solicitada"
                valor={formatearFechaHora(query.data.fechaSolicitud)}
              />
              <Campo
                label="Autorizada por jefe"
                valor={
                  query.data.fechaAutorizacionJefe != null
                    ? formatearFechaHora(query.data.fechaAutorizacionJefe)
                    : '—'
                }
              />
              <Campo
                label="Autorizada por DF"
                valor={
                  query.data.fechaAutorizacionDf != null
                    ? formatearFechaHora(query.data.fechaAutorizacionDf)
                    : '—'
                }
              />
              <Campo
                label="Anticipo pagado"
                valor={
                  query.data.fechaAnticipoPagado != null
                    ? formatearFechaHora(query.data.fechaAnticipoPagado)
                    : '—'
                }
              />
              <Campo
                label="Comprobación capturada"
                valor={
                  query.data.fechaComprobacion != null
                    ? formatearFechaHora(query.data.fechaComprobacion)
                    : '—'
                }
              />
              <Campo
                label="Liquidada"
                valor={
                  query.data.fechaLiquidacion != null
                    ? formatearFechaHora(query.data.fechaLiquidacion)
                    : '—'
                }
              />
              <Campo
                label="Rechazada"
                valor={
                  query.data.fechaRechazo != null
                    ? formatearFechaHora(query.data.fechaRechazo)
                    : '—'
                }
              />
              <Campo
                label="Rechazado por"
                valor={query.data.rechazadoPor ?? '—'}
                mono={query.data.rechazadoPor != null}
              />
              <Campo
                label="Motivo rechazo"
                valor={query.data.motivoRechazo ?? '—'}
              />
            </dl>
          </section>

          <section className="space-y-2">
            <h2 className="text-sm font-semibold uppercase tracking-wide text-muted-foreground">
              Líneas de la comprobación ({query.data.lineas.length})
            </h2>
            {query.data.lineas.length === 0 ? (
              <p className="rounded-md border p-4 text-sm text-muted-foreground">
                Aún no se captura la comprobación de gastos.
              </p>
            ) : (
              <div className="overflow-x-auto rounded-md border">
                <table className="w-full text-sm">
                  <thead className="bg-muted/50">
                    <tr>
                      <th className="px-3 py-2 text-left">Fecha gasto</th>
                      <th className="px-3 py-2 text-left">Comprobante</th>
                      <th className="px-3 py-2 text-left">Concepto</th>
                      <th className="px-3 py-2 text-right">Subtotal</th>
                      <th className="px-3 py-2 text-right">IVA</th>
                      <th className="px-3 py-2 text-right">Retenciones</th>
                      <th className="px-3 py-2 text-right">Total</th>
                    </tr>
                  </thead>
                  <tbody>
                    {query.data.lineas.map((l) => (
                      <tr key={l.id} className="border-t">
                        <td className="px-3 py-2 whitespace-nowrap">
                          {l.fechaGasto.slice(0, 10)}
                        </td>
                        <td className="px-3 py-2">
                          {l.esTicketNoFiscal ? (
                            <span className="inline-flex items-center rounded-full bg-slate-200 px-2 py-0.5 text-xs font-medium text-slate-700">
                              No fiscal
                            </span>
                          ) : (
                            <span className="font-mono text-xs">
                              {l.uuidCfdi ?? '—'}
                              {l.folioProveedor != null &&
                                ` · ${l.folioProveedor}`}
                            </span>
                          )}
                        </td>
                        <td className="max-w-64 truncate px-3 py-2">
                          {l.concepto}
                        </td>
                        <td className="px-3 py-2 text-right font-mono">
                          {formatearMonto(l.subtotal, l.moneda)}
                        </td>
                        <td className="px-3 py-2 text-right font-mono">
                          {formatearMonto(l.impuestosTrasladados, l.moneda)}
                        </td>
                        <td className="px-3 py-2 text-right font-mono">
                          {formatearMonto(l.retenciones, l.moneda)}
                        </td>
                        <td className="px-3 py-2 text-right font-mono">
                          {formatearMonto(l.total, l.moneda)}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>
        </div>
      )}
    </div>
  );
}
