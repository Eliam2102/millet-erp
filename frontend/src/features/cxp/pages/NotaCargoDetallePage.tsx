import { PageHeader } from '@/components/erp/PageHeader';
import { DocumentoP4Acciones } from '@/features/cxp/components/DocumentoP4Acciones';
import { useState } from 'react';
import { Link, useParams, useSearch } from '@tanstack/react-router';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { ConfirmarAccionDialog } from '@/components/erp/feedback/ConfirmarAccionDialog';
import { ArrowLeft } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { ErrorState, DetalleSkeleton } from '@/components/erp';
import { useNotaCargo, useAutorizarNotaCargo } from '@/features/cxp/api/useNotasYAnticipos';
import { esApiError } from '@/lib/api';
import { toast } from 'sonner';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { EstadoNotaCargo } from '@/features/cxp/api/types';
import { EstadoNotaCargoChip } from '@/features/cxp/components/EstadoChips';
import { Campo, Importe } from '@/features/cxp/components/DetalleCampos';
import { formatearFechaHora } from '@/features/cxp/lib/formato';
import { formatCantidad } from '@/lib/format-cantidad';

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
  const search = useSearch({ from: FROM });
  const query = useNotaCargo(id);
  const autorizar = useAutorizarNotaCargo();
  const puedeAutorizar = useHasPermission(PermisosCanonicos.CuentasPorPagarNotasCargoAutorizar);
  const usuarioId = useAuthStore(s => s.user?.id);
  const [confirmarAutorizar, setConfirmarAutorizar] = useState(false);
  const esCreador = query.data?.creadoPor != null && query.data.creadoPor === usuarioId;


  return (
    <div className="space-y-4">
      <div className="flex items-center gap-2">
        <Button asChild variant="ghost" size="sm">
          <Link to="/cxp/notas-cargo" search={search}>
            <ArrowLeft className="mr-2 size-3.5" />
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
        <DetalleSkeleton />
      ) : query.data == null ? null : (
        <div className="space-y-6">
          <PageHeader
            titulo={query.data.folio}
            estado={
              <>
                <EstadoNotaCargoChip estado={query.data.estado} />
              </>
            }
            descripcion={
              <>
                Creada: {formatearFechaHora(query.data.fechaCreacion)} ·
                Ejercicio: {query.data.folioAnio}
              </>
            }
            mono
          />

          {puedeAutorizar && query.data.estado === EstadoNotaCargo.Borrador && (
            <div className="space-y-1" data-print="hidden">
              <Button
                disabled={autorizar.isPending || esCreador}
                aria-describedby={esCreador ? 'nc-creador' : undefined}
                onClick={() => setConfirmarAutorizar(true)}
              >
                Autorizar
              </Button>
              {esCreador && (
                <p id="nc-creador" className="text-xs text-ink-muted">
                  Quien creó la nota de cargo no puede autorizarla.
                </p>
              )}
              <ConfirmarAccionDialog
                open={confirmarAutorizar}
                onOpenChange={setConfirmarAutorizar}
                titulo="Autorizar nota de cargo"
                resumen={
                  <>
                    Autorizas la nota de cargo {query.data.folio}
                    {query.data.proveedorNombre ? ` a ${query.data.proveedorNombre}` : ''} por{' '}
                    <MoneyDisplay amount={query.data.monto} currency={query.data.moneda} strong />.
                    Después se podrá aplicar.
                  </>
                }
                verbo="Autorizar nota de cargo"
                tono="primario"
                pendiente={autorizar.isPending}
                onConfirmar={() =>
                  autorizar.mutate(
                    { id, versionEsperada: query.data!.version, idempotencyKey: crypto.randomUUID() },
                    {
                      onSuccess: () => {
                        toast.success('Se autorizó la nota de cargo.');
                        void query.refetch();
                      },
                      onError: (error) =>
                        toast.error(
                          esApiError(error)
                            ? (error.problem.detail ?? error.problem.title)
                            : 'No se pudo autorizar la nota de cargo.',
                        ),
                      onSettled: () => setConfirmarAutorizar(false),
                    },
                  )
                }
              />
            </div>
          )}
          <DocumentoP4Acciones tipo="notas-cargo" documento={query.data} />

          <dl className="grid grid-cols-1 gap-x-6 gap-y-2 rounded-md border p-4 @xl:grid-cols-2 @3xl:grid-cols-4 @5xl:grid-cols-6">
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
                  ? formatCantidad(query.data.tipoCambio, 4)
                  : '—'
              }
            />
          </dl>

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
                    '—'
                  )}
                </dd>
              </div>
              <Campo
                label="Devolución a proveedor"
                valor={query.data.devolucionAProveedorId != null ? 'Vinculada' : '—'}
              />
              <div>
                <dt className="text-xs text-muted-foreground">
                  NC del proveedor (conciliación)
                </dt>
                <dd className="text-xs">
                  {query.data.notaCreditoProveedorId != null ? (
                    <Link
                      to="/cxp/notas-credito/$id"
                      params={{ id: query.data.notaCreditoProveedorId }}
                      className="text-primary underline-offset-2 hover:underline"
                    >
                      Ver NC del proveedor
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
