import { useState } from 'react';
import { Link, useNavigate } from '@tanstack/react-router';
import { ArrowRightLeft, RotateCcw, X } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { useMovimiento, useRevertirPago } from '@/features/tesoreria/api/useTesoreria';
import type { AplicacionMovimientoDto } from '@/features/tesoreria/api/types';
import {
  EstadoAplicacionBadge,
  EstadoConciliacionBadge,
  SentidoBadge,
} from '@/features/tesoreria/components/Badges';
import { MotivoDialog } from '@/features/tesoreria/components/MotivoDialog';
import { BENEFICIARIO_TIPO_LABELS } from '@/features/tesoreria/api/types';
import { formatoFecha, formatoMonto } from '@/features/tesoreria/lib/formato';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError, useBodyScopedIdempotencyKey } from '@/lib/api';

/**
 * <c>Detalle de movimiento bancario</c> (TES-FE-PR2, P3): cabecera,
 * aplicaciones a pasivos (con reversa RN-10 por aplicación — motivo
 * obligatorio) y contramovimientos ligados. La lista compacta del
 * master-detail queda como follow-up; el "Cerrar" regresa a la bandeja.
 */
export function DetalleMovimiento({ id }: { id: string }) {
  const navigate = useNavigate();
  const query = useMovimiento(id);
  const revertir = useRevertirPago();
  const keyFor = useBodyScopedIdempotencyKey();
  const puedeRevertir = useHasPermission(PermisosCanonicos.TesoreriaPagosRevertir);

  const [revirtiendo, setRevirtiendo] = useState<AplicacionMovimientoDto | null>(null);

  function confirmarReversa(motivo: string) {
    if (revirtiendo == null) return;
    const command = { pagoId: revirtiendo.pagoId, motivo };
    revertir.mutate(
      {
        ...command,
        // Reintento tras timeout no genera un segundo contramovimiento.
        idempotencyKey: keyFor(command),
      },
      {
        onSuccess: () => {
          toast.success('Pago revertido', {
            description:
              'Se generó el contramovimiento y CxP regresará el pasivo a Autorizada.',
          });
          setRevirtiendo(null);
        },
        onError: (error) => {
          toast.error(
            esApiError(error) ? error.problem.title : 'No se pudo revertir el pago',
            {
              description: esApiError(error)
                ? (error.problem.detail ?? `Código: ${error.traceId}`)
                : undefined,
            },
          );
        },
      },
    );
  }

  if (query.isError) {
    return (
      <div className="px-4 py-6">
        <ErrorState
          title="No se pudo cargar el movimiento"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      </div>
    );
  }

  if (query.isLoading || query.data == null) {
    return (
      <div className="px-4 py-6">
        <TableSkeleton rows={6} columns={[{ width: 'w-full' }]} />
      </div>
    );
  }

  const { movimiento: m, aplicaciones, contramovimientos } = query.data;

  return (
    <div className="mx-auto max-w-4xl space-y-6 px-4 py-6">
      <div
        className="flex flex-wrap items-center justify-between gap-3"
        data-print="hidden"
      >
        <h1 className="flex items-center gap-2 text-xl font-semibold">
          <ArrowRightLeft className="h-5 w-5 text-primary" />
          Movimiento{' '}
          <span className="font-mono text-base text-muted-foreground">
            {m.id.slice(0, 8)}
          </span>
          <SentidoBadge sentido={m.sentido} />
        </h1>
        <Button
          variant="ghost"
          size="icon"
          aria-label="Cerrar"
          onClick={() => navigate({ to: '/tesoreria/movimientos' })}
        >
          <X className="h-4 w-4" />
        </Button>
      </div>

      <dl className="grid grid-cols-2 gap-x-6 gap-y-3 rounded-md border p-4 text-sm md:grid-cols-3">
        <div>
          <dt className="text-xs text-muted-foreground">Monto</dt>
          <dd className="font-mono text-base font-semibold tabular-nums">
            {formatoMonto(m.monto, m.moneda)}
          </dd>
        </div>
        <div>
          <dt className="text-xs text-muted-foreground">Fecha valor</dt>
          <dd>{formatoFecha(m.fechaValor)}</dd>
        </div>
        <div>
          <dt className="text-xs text-muted-foreground">Referencia</dt>
          <dd className="font-mono">{m.referenciaBancaria ?? '—'}</dd>
        </div>
        <div>
          <dt className="text-xs text-muted-foreground">Concepto</dt>
          <dd>{m.conceptoNombre ?? (m.motivoNoAplicado ? 'Pago a cuenta' : '—')}</dd>
        </div>
        <div>
          <dt className="text-xs text-muted-foreground">Beneficiario</dt>
          <dd>
            {m.beneficiarioTipo != null
              ? BENEFICIARIO_TIPO_LABELS[m.beneficiarioTipo]
              : '—'}
            {m.beneficiarioRef != null && (
              <span className="ml-1 font-mono text-xs text-muted-foreground">
                {m.beneficiarioRef.slice(0, 8)}
              </span>
            )}
          </dd>
        </div>
        <div>
          <dt className="text-xs text-muted-foreground">Estados</dt>
          <dd className="flex flex-wrap gap-1">
            <EstadoAplicacionBadge estado={m.estadoAplicacion} />
            <EstadoConciliacionBadge estado={m.estadoConciliacion} />
          </dd>
        </div>
        {m.motivoNoAplicado != null && (
          <div className="col-span-2 md:col-span-3">
            <dt className="text-xs text-muted-foreground">Motivo (pago a cuenta)</dt>
            <dd>{m.motivoNoAplicado}</dd>
          </div>
        )}
        {m.contramovimientoDe != null && (
          <div className="col-span-2 md:col-span-3">
            <dt className="text-xs text-muted-foreground">Contramovimiento de</dt>
            <dd>
              <Link
                to="/tesoreria/movimientos/$id"
                params={{ id: m.contramovimientoDe }}
                className="font-mono text-xs text-primary hover:underline"
              >
                {m.contramovimientoDe}
              </Link>
            </dd>
          </div>
        )}
      </dl>

      <section className="space-y-2">
        <h2 className="text-sm font-semibold">Aplicaciones a pasivos</h2>
        {aplicaciones.length === 0 ? (
          <EmptyState
            icon={<ArrowRightLeft className="h-8 w-8" />}
            title="Sin aplicaciones."
            description="Este movimiento no está ligado a pasivos de CxP."
          />
        ) : (
          <div className="overflow-x-auto rounded-md border">
            <table className="w-full text-sm">
              <thead className="bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
                <tr>
                  <th className="px-3 py-2 font-medium">Pago (correlación CxP)</th>
                  <th className="px-3 py-2 font-medium">Factura</th>
                  <th className="px-3 py-2 text-right font-medium">Importe</th>
                  <th className="px-3 py-2 font-medium">Estado</th>
                  <th className="px-3 py-2" />
                </tr>
              </thead>
              <tbody className="divide-y">
                {aplicaciones.map((a) => (
                  <tr key={a.pagoId} className="hover:bg-muted/30">
                    <td className="px-3 py-2 font-mono text-xs">
                      {a.pagoId.slice(0, 8)}
                    </td>
                    <td className="px-3 py-2 font-mono text-xs">
                      {a.facturaProveedorId.slice(0, 8)}
                    </td>
                    <td className="px-3 py-2 text-right font-mono tabular-nums">
                      {formatoMonto(a.importeAplicado, m.moneda)}
                    </td>
                    <td className="px-3 py-2 text-xs">
                      {a.revertida ? (
                        <span className="text-rose-700">Revertida</span>
                      ) : (
                        <span className="text-emerald-700">Vigente</span>
                      )}
                    </td>
                    <td className="px-3 py-2 text-right">
                      {puedeRevertir && !a.revertida && (
                        <Button
                          variant="outline"
                          size="sm"
                          onClick={() => setRevirtiendo(a)}
                        >
                          <RotateCcw className="mr-1 h-3 w-3" />
                          Revertir
                        </Button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      {contramovimientos.length > 0 && (
        <section className="space-y-2">
          <h2 className="text-sm font-semibold">Contramovimientos (reversas RN-10)</h2>
          <ul className="space-y-1 text-sm">
            {contramovimientos.map((c) => (
              <li key={c.id} className="flex items-center gap-2">
                <SentidoBadge sentido={c.sentido} />
                <span className="font-mono tabular-nums">
                  {formatoMonto(c.monto, c.moneda)}
                </span>
                <span className="text-xs text-muted-foreground">
                  {formatoFecha(c.fechaValor)}
                </span>
                <Link
                  to="/tesoreria/movimientos/$id"
                  params={{ id: c.id }}
                  className="font-mono text-xs text-primary hover:underline"
                >
                  {c.id.slice(0, 8)}
                </Link>
              </li>
            ))}
          </ul>
        </section>
      )}

      <MotivoDialog
        open={revirtiendo != null}
        onOpenChange={(o) => {
          if (!o) setRevirtiendo(null);
        }}
        titulo="Revertir pago"
        descripcion="Se genera un contramovimiento ligado (nada se borra) y CxP regresa el pasivo a Autorizada si queda saldo. El motivo es obligatorio (RN-10)."
        confirmLabel="Revertir"
        onConfirm={confirmarReversa}
        pending={revertir.isPending}
      />
    </div>
  );
}
