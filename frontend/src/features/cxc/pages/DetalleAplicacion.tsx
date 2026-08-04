import { useState } from 'react';
import { Link, useParams, useSearch } from '@tanstack/react-router';
import { Check, X, XCircle } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { TextAreaField } from '@/components/erp/forms/TextAreaField';
import { ErrorState } from '@/components/erp';
import {
  useConfirmarPropuesta,
  usePropuestaAplicacion,
  useRechazarPropuesta,
} from '@/features/cxc/api/useAplicaciones';
import { useClientesLookupCxc } from '@/features/cxc/api/useLineasCredito';
import { ChipEstadoPropuesta } from '@/features/cxc/components/ChipEstadoPropuesta';
import {
  EstadoPropuestaAplicacion,
  type PropuestaAplicacionResponse,
} from '@/features/cxc/api/types';
import { formatoMonto } from '@/features/cxc/lib/glosario';
import type { AplicacionesSearch } from '@/features/cxc/lib/aplicaciones-search-schema';
import { useQueryClient } from '@tanstack/react-query';
import { esApiError, esConflictoConcurrencia } from '@/lib/api';
import { useConflictDialog } from '@/components/erp/collaboration/conflict-dialog-context';
import { cxcKeys } from '@/features/cxc/api/keys';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * <c>Detalle de propuesta de aplicación</c> (CXC-FE-PR6, P3). Depósito +
 * facturas propuestas + ajuste no fiscal; acciones de Ingresos
 * (Confirmar / Rechazar con motivo, gate
 * <c>aplicacion-pago.confirmar</c> — interino A2). La confirmación NO
 * aplica pagos a cartera: eso lo hace el REPP timbrado vía eventos.
 */
export function DetalleAplicacion() {
  const { id } = useParams({ from: '/_app/cxc/aplicaciones/$id' });
  const query = usePropuestaAplicacion(id);

  return (
    <div className="space-y-4">
      {query.isError ? (
        <ErrorState
          title="No se pudo cargar la propuesta"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading || query.data == null ? (
        <div className="space-y-3">
          <div className="h-8 w-64 animate-pulse rounded bg-muted" />
          <div className="h-40 w-full animate-pulse rounded bg-muted" />
        </div>
      ) : (
        <Contenido p={query.data} />
      )}
    </div>
  );
}

function Contenido({ p }: { p: PropuestaAplicacionResponse }) {
  const search = useSearch({ strict: false }) as AplicacionesSearch;
  const puedeConfirmar = useHasPermission(
    PermisosCanonicos.CuentasPorCobrarAplicacionPagoConfirmar,
  );
  const confirmar = useConfirmarPropuesta();
  const rechazar = useRechazarPropuesta();
  const queryClient = useQueryClient();
  const conflictDialog = useConflictDialog();
  const [dialogRechazo, setDialogRechazo] = useState(false);
  const [motivo, setMotivo] = useState('');

  const lookup = useClientesLookupCxc({ ids: [p.clienteId] });
  const cliente = lookup.data?.[0] ?? null;
  const pendiente = p.estado === EstadoPropuestaAplicacion.Propuesta;
  const sumaAplicada = p.facturas.reduce((a, f) => a + f.importeAplicado, 0);

  function manejarError(error: unknown, fallback: string) {
    // Conflicto de concurrencia (409): otra sesión ya cambió la propuesta.
    // La versión en caché quedó vieja → reintentar re-fallaría. Se abre el
    // diálogo de conflicto que refresca (invalida la query → versión nueva),
    // igual que DetalleLineaCredito, en vez de un toast que deja atascado.
    if (esConflictoConcurrencia(error)) {
      conflictDialog.openSimple({
        onRefrescar: () =>
          queryClient.invalidateQueries({ queryKey: cxcKeys.propuestas() }),
        traceId: error.traceId,
      });
      return;
    }
    toast.error(
      esApiError(error)
        ? `${error.problem.title}${error.problem.detail ? ` — ${error.problem.detail}` : ''}`
        : fallback,
    );
  }

  return (
    <div className="space-y-6">
      {/* ── Sub-topbar §6.5 ─────────────────────────────────────── */}
      <div
        className="sticky top-0 z-10 flex flex-wrap items-center justify-between gap-2 border-b bg-background/95 pb-2 backdrop-blur"
        data-print="hidden"
      >
        <div className="flex min-w-0 flex-wrap items-center gap-2">
          <h1 className="truncate font-mono text-lg font-semibold">
            {p.depositoRef}
          </h1>
          <ChipEstadoPropuesta estado={p.estado} />
        </div>
        <div className="flex items-center gap-1.5">
          {puedeConfirmar && pendiente && (
            <>
              <Button
                size="sm"
                disabled={confirmar.isPending}
                onClick={() =>
                  // Key fresca por submit: el backend exige UUID v4 puro
                  // (patrón multi-submit de lib/api/idempotency.ts).
                  confirmar.mutate(
                    {
                      id: p.id,
                      versionEsperada: p.version,
                      idempotencyKey: crypto.randomUUID(),
                    },
                    {
                      onSuccess: () =>
                        toast.success(
                          'Propuesta confirmada. El pago se aplica a cartera cuando el REPP se timbre.',
                        ),
                      onError: (e) =>
                        manejarError(e, 'No se pudo confirmar la propuesta.'),
                    },
                  )
                }
              >
                <Check className="mr-1.5 h-3.5 w-3.5" />
                {confirmar.isPending ? 'Confirmando…' : 'Confirmar'}
              </Button>
              <Button
                variant="outline"
                size="sm"
                className="text-destructive"
                onClick={() => setDialogRechazo(true)}
              >
                <XCircle className="mr-1.5 h-3.5 w-3.5" />
                Rechazar
              </Button>
            </>
          )}
          <Button variant="ghost" size="icon" asChild aria-label="Cerrar detalle">
            <Link to="/cxc/aplicaciones" search={search}>
              <X className="h-4 w-4" />
            </Link>
          </Button>
        </div>
      </div>

      {p.estado === EstadoPropuestaAplicacion.Rechazada && p.motivoRechazo && (
        <div className="rounded-md border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-900 dark:border-red-900 dark:bg-red-950/40 dark:text-red-200">
          <p className="font-medium">Propuesta rechazada</p>
          <p>{p.motivoRechazo}</p>
        </div>
      )}

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        {/* ── Depósito ─────────────────────────────────────────── */}
        <section className="rounded-md border bg-card p-4" aria-label="Depósito">
          <h3 className="text-sm font-medium">Depósito</h3>
          <dl className="mt-3 grid grid-cols-2 gap-x-4 gap-y-3 text-sm">
            <div>
              <dt className="text-xs text-muted-foreground">Cliente</dt>
              <dd className="truncate">
                {cliente?.razonSocial ?? p.clienteId.slice(0, 8)}
              </dd>
            </div>
            <div>
              <dt className="text-xs text-muted-foreground">Monto</dt>
              <dd className="font-mono tabular-nums">
                {formatoMonto(p.montoDeposito, p.moneda)}
              </dd>
            </div>
            <div>
              <dt className="text-xs text-muted-foreground">Remittance</dt>
              <dd className="truncate text-xs">{p.remittanceRef}</dd>
            </div>
            <div>
              <dt className="text-xs text-muted-foreground">
                Ajuste no fiscal
              </dt>
              <dd className="font-mono tabular-nums">
                {p.ajusteNoFiscal !== 0
                  ? formatoMonto(p.ajusteNoFiscal, p.moneda)
                  : '—'}
              </dd>
            </div>
          </dl>
          {p.ajusteNoFiscal !== 0 && (
            <p className="mt-3 text-xs text-amber-800 dark:text-amber-300">
              Diferencia dentro de tolerancia registrada como ajuste no
              fiscal (no genera CFDI).
            </p>
          )}
        </section>

        {/* ── Matching propuesto ───────────────────────────────── */}
        <section
          className="rounded-md border bg-card p-4"
          aria-label="Facturas propuestas"
        >
          <h3 className="text-sm font-medium">Facturas propuestas</h3>
          <table className="mt-3 w-full text-sm">
            <thead className="text-left text-xs uppercase tracking-wide text-muted-foreground">
              <tr>
                <th className="py-1 font-medium">Factura</th>
                <th className="py-1 text-right font-medium">Importe</th>
              </tr>
            </thead>
            <tbody className="divide-y">
              {p.facturas.map((f) => (
                <tr key={f.facturaUuid}>
                  <td className="max-w-56 truncate py-1.5 font-mono text-xs">
                    {f.folio ?? f.facturaUuid}
                    {f.numParcialidad != null && (
                      <span className="ml-1 text-muted-foreground">
                        (parc. {f.numParcialidad})
                      </span>
                    )}
                  </td>
                  <td className="py-1.5 text-right font-mono tabular-nums">
                    {formatoMonto(f.importeAplicado, p.moneda)}
                  </td>
                </tr>
              ))}
            </tbody>
            <tfoot>
              <tr className="border-t">
                <td className="py-1.5 text-xs font-medium">Σ aplicado</td>
                <td className="py-1.5 text-right font-mono font-medium tabular-nums">
                  {formatoMonto(sumaAplicada, p.moneda)}
                </td>
              </tr>
            </tfoot>
          </table>
        </section>
      </div>

      {/* ── Dialog de rechazo (motivo obligatorio) ─────────────── */}
      <Dialog
        open={dialogRechazo}
        onOpenChange={(open) => {
          if (!open) setMotivo('');
          setDialogRechazo(open);
        }}
      >
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>Rechazar propuesta</DialogTitle>
            <DialogDescription>
              {p.depositoRef}: la propuesta vuelve a CxC con el motivo para
              corregir el matching.
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-1">
            <Label htmlFor="motivo-rechazo" className="text-xs">
              Motivo (requerido)
            </Label>
            <TextAreaField
              value={motivo}
              onChange={(v) => setMotivo(v ?? '')}
              maxLength={400}
              textareaProps={{
                id: 'motivo-rechazo',
                placeholder: 'p. ej. El depósito corresponde a otro cliente…',
              }}
            />
          </div>
          <DialogFooter>
            <Button
              type="button"
              variant="ghost"
              onClick={() => setDialogRechazo(false)}
              disabled={rechazar.isPending}
            >
              Cancelar
            </Button>
            <Button
              type="button"
              variant="destructive"
              disabled={motivo.trim().length === 0 || rechazar.isPending}
              onClick={() =>
                rechazar.mutate(
                  {
                    id: p.id,
                    versionEsperada: p.version,
                    motivo: motivo.trim(),
                    idempotencyKey: crypto.randomUUID(),
                  },
                  {
                    onSuccess: () => {
                      setDialogRechazo(false);
                      toast.success('Propuesta rechazada.');
                    },
                    onError: (e) =>
                      manejarError(e, 'No se pudo rechazar la propuesta.'),
                  },
                )
              }
            >
              {rechazar.isPending ? 'Rechazando…' : 'Rechazar propuesta'}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
