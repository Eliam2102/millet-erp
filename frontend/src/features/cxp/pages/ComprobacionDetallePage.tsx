import { Link, useParams } from '@tanstack/react-router';
import { ArrowLeft } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { ErrorState, TableSkeleton } from '@/components/erp';
import { useComprobacionGastos } from '@/features/cxp/api/useComprobaciones';
import { TipoComprobacionGastosLabels } from '@/features/cxp/api/types';
import { esApiError } from '@/lib/api';
import { EstadoComprobacionChip } from '@/features/cxp/components/EstadoChips';
import { Campo, Importe } from '@/features/cxp/components/DetalleCampos';
import {
  formatearFechaHora,
  formatearMonto,
} from '@/features/cxp/lib/formato';

/**
 * <c>P3 — Detalle de Comprobación de Gastos</c> (serie detalles CxP,
 * backend PR #632). Vista read-only: encabezado con tipo + estado,
 * pedimento (aduanales), observaciones, trazabilidad de
 * revisión/autorización (doble firma)/aplicación/rechazo y tabla de
 * líneas (CFDIs) con link a la factura generada.
 */
const FROM = '/_app/cxp/comprobaciones/$id' as const;

export function ComprobacionDetallePage() {
  const { id } = useParams({ from: FROM });
  const query = useComprobacionGastos(id);

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-2">
        <Button asChild variant="ghost" size="sm">
          <Link to="/cxp/comprobaciones" search={{}}>
            <ArrowLeft className="mr-2 h-4 w-4" />
            Comprobaciones
          </Link>
        </Button>
      </div>

      {query.isError ? (
        <ErrorState
          title="No se pudo cargar la comprobación"
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
                {TipoComprobacionGastosLabels[query.data.tipo]}
              </h1>
              <EstadoComprobacionChip estado={query.data.estado} />
            </div>
            <p className="text-sm text-muted-foreground">
              Periodo: {query.data.fechaInicio} → {query.data.fechaFin} ·
              Creada: {formatearFechaHora(query.data.fechaCreacion)}
            </p>
          </header>

          <section className="grid grid-cols-1 gap-x-6 gap-y-2 rounded-md border p-4 md:grid-cols-2">
            <Campo
              label="Sucursal"
              valor={query.data.sucursalNombre ?? query.data.sucursalId}
              mono={query.data.sucursalNombre == null}
            />
            <Campo
              label="Responsable"
              valor={query.data.responsableNombre ?? query.data.responsableId}
              mono={query.data.responsableNombre == null}
            />
            <Campo label="Moneda" valor={query.data.moneda} />
            <Campo
              label="Número de pedimento"
              valor={query.data.numeroPedimento ?? '—'}
              mono={query.data.numeroPedimento != null}
            />
            {query.data.observaciones != null && (
              <div className="md:col-span-2">
                <dt className="text-xs text-muted-foreground">
                  Observaciones
                </dt>
                <dd className="text-sm">{query.data.observaciones}</dd>
              </div>
            )}
          </section>

          <section className="rounded-md border p-4">
            <h2 className="mb-2 text-sm font-semibold uppercase tracking-wide text-muted-foreground">
              Importes
            </h2>
            <dl className="grid grid-cols-2 gap-x-6 gap-y-1 text-sm sm:grid-cols-4">
              <Importe
                label="Monto total"
                v={query.data.montoTotal}
                m={query.data.moneda}
                strong
              />
            </dl>
          </section>

          <section className="rounded-md border p-4">
            <h2 className="mb-2 text-sm font-semibold uppercase tracking-wide text-muted-foreground">
              Trazabilidad
            </h2>
            <dl className="grid grid-cols-1 gap-x-6 gap-y-2 sm:grid-cols-2 md:grid-cols-3">
              <Campo
                label="Creada"
                valor={formatearFechaHora(query.data.fechaCreacion)}
              />
              <Campo
                label="Enviada a revisión"
                valor={
                  query.data.fechaEnvioRevision != null
                    ? formatearFechaHora(query.data.fechaEnvioRevision)
                    : '—'
                }
              />
              <Campo
                label="Firma Nivel 1"
                valor={
                  query.data.fechaAutorizacionNivel1 != null
                    ? formatearFechaHora(query.data.fechaAutorizacionNivel1)
                    : '—'
                }
              />
              <Campo
                label="Firmado N1 por"
                valor={query.data.autorizadoPorNivel1 ?? '—'}
                mono={query.data.autorizadoPorNivel1 != null}
              />
              <Campo
                label="Autorizada"
                valor={
                  query.data.fechaAutorizacion != null
                    ? formatearFechaHora(query.data.fechaAutorizacion)
                    : '—'
                }
              />
              <Campo
                label="Autorizado por"
                valor={query.data.autorizadoPor ?? '—'}
                mono={query.data.autorizadoPor != null}
              />
              <Campo
                label="Aplicada"
                valor={
                  query.data.fechaAplicacion != null
                    ? formatearFechaHora(query.data.fechaAplicacion)
                    : '—'
                }
              />
              <Campo
                label="Aplicado por"
                valor={query.data.aplicadoPor ?? '—'}
                mono={query.data.aplicadoPor != null}
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
              Líneas ({query.data.lineas.length})
            </h2>
            <div className="overflow-x-auto rounded-md border">
              <table className="w-full text-sm">
                <thead className="bg-muted/50">
                  <tr>
                    <th className="px-3 py-2 text-left">Fecha CFDI</th>
                    <th className="px-3 py-2 text-left">UUID / Folio</th>
                    <th className="px-3 py-2 text-left">Concepto</th>
                    <th className="px-3 py-2 text-left">Factura</th>
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
                        {l.fechaCfdi.slice(0, 10)}
                      </td>
                      <td className="px-3 py-2 font-mono text-xs">
                        {l.uuidCfdi ?? '—'}
                        {l.folioProveedor != null && ` · ${l.folioProveedor}`}
                      </td>
                      <td className="max-w-64 truncate px-3 py-2">
                        {l.concepto ?? '—'}
                      </td>
                      <td className="px-3 py-2 font-mono text-xs">
                        <Link
                          to="/cxp/facturas/$id"
                          params={{ id: l.facturaProveedorId }}
                          className="text-primary underline-offset-2 hover:underline"
                        >
                          {`${l.facturaProveedorId.slice(0, 8)}…`}
                        </Link>
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
          </section>
        </div>
      )}
    </div>
  );
}
