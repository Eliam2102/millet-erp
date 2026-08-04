import { Link, useParams } from '@tanstack/react-router';
import { ArrowLeft } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { ErrorState, TableSkeleton } from '@/components/erp';
import { useNotaCargo } from '@/features/cxp/api/useNotasYAnticipos';
import { esApiError } from '@/lib/api';
import { EstadoNotaCargoChip } from '@/features/cxp/components/EstadoChips';
import { Campo, Importe } from '@/features/cxp/components/DetalleCampos';
import { formatearFechaHora } from '@/features/cxp/lib/formato';

/**
 * <c>P3 — Detalle de Nota de Cargo</c> (serie detalles CxP, backend
 * PR #632). Vista read-only: encabezado con folio + estado, monto,
 * vínculos (factura origen, devolución 8.B, NC del proveedor que la
 * concilió) y trazabilidad del ciclo Borrador → Autorizada → Aplicada
 * → Formalizada.
 */
const FROM = '/_app/cxp/notas-cargo/$id' as const;

export function NotaCargoDetallePage() {
  const { id } = useParams({ from: FROM });
  const query = useNotaCargo(id);

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-2">
        <Button asChild variant="ghost" size="sm">
          <Link to="/cxp/notas-cargo" search={{}}>
            <ArrowLeft className="mr-2 h-4 w-4" />
            Notas de cargo
          </Link>
        </Button>
      </div>

      {query.isError ? (
        <ErrorState
          title="No se pudo cargar la nota de cargo"
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
                {query.data.folio}
              </h1>
              <EstadoNotaCargoChip estado={query.data.estado} />
            </div>
            <p className="text-sm text-muted-foreground">
              Creada: {formatearFechaHora(query.data.fechaCreacion)} ·
              Ejercicio: {query.data.folioAnio}
            </p>
          </header>

          <section className="grid grid-cols-1 gap-x-6 gap-y-2 rounded-md border p-4 md:grid-cols-2">
            <Campo
              label="Proveedor"
              valor={query.data.proveedorNombre ?? query.data.proveedorId}
              mono={query.data.proveedorNombre == null}
            />
            <Campo
              label="Sucursal"
              valor={query.data.sucursalId ?? '—'}
              mono={query.data.sucursalId != null}
            />
            <div className="md:col-span-2">
              <dt className="text-xs text-muted-foreground">Concepto</dt>
              <dd className="text-sm">{query.data.concepto}</dd>
            </div>
            <Campo
              label="Concepto contable"
              valor={query.data.conceptoContableId ?? '—'}
              mono={query.data.conceptoContableId != null}
            />
            <Campo
              label="Tipo de cambio"
              valor={
                query.data.tipoCambio != null
                  ? query.data.tipoCambio.toFixed(4)
                  : '—'
              }
            />
          </section>

          <section className="rounded-md border p-4">
            <h2 className="mb-2 text-sm font-semibold uppercase tracking-wide text-muted-foreground">
              Importes
            </h2>
            <dl className="grid grid-cols-2 gap-x-6 gap-y-1 text-sm sm:grid-cols-4">
              <Importe
                label="Monto"
                v={query.data.monto}
                m={query.data.moneda}
                strong
              />
            </dl>
          </section>

          <section className="rounded-md border p-4">
            <h2 className="mb-2 text-sm font-semibold uppercase tracking-wide text-muted-foreground">
              Vínculos
            </h2>
            <dl className="grid grid-cols-1 gap-x-6 gap-y-2 md:grid-cols-3">
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
                    '—'
                  )}
                </dd>
              </div>
              <Campo
                label="Devolución a proveedor (8.B)"
                valor={query.data.devolucionAProveedorId ?? '—'}
                mono={query.data.devolucionAProveedorId != null}
              />
              <div>
                <dt className="text-xs text-muted-foreground">
                  NC del proveedor (conciliación)
                </dt>
                <dd className="font-mono text-xs">
                  {query.data.notaCreditoProveedorId != null ? (
                    <Link
                      to="/cxp/notas-credito/$id"
                      params={{ id: query.data.notaCreditoProveedorId }}
                      className="text-primary underline-offset-2 hover:underline"
                    >
                      {query.data.notaCreditoProveedorId}
                    </Link>
                  ) : (
                    '—'
                  )}
                </dd>
              </div>
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
                label="Creado por"
                valor={query.data.creadoPor ?? '—'}
                mono={query.data.creadoPor != null}
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
                label="Formalizada"
                valor={
                  query.data.fechaFormalizacion != null
                    ? formatearFechaHora(query.data.fechaFormalizacion)
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
