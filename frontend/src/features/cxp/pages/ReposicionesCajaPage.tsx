import { PageHeader } from '@/components/erp/PageHeader';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  EmptyState,
  ErrorState,
  TableSkeleton,
} from '@/components/erp';
import {
  useEmitirReposicionManual,
  useReposiciones,
  useSaldosReposicion,
} from '@/features/cxp/api/useReposiciones';
import {
  DestinoReposicionCajaLabels,
  type SaldoPendienteReposicion,
} from '@/features/cxp/api/types';
import { useSucursales } from '@/features/catalogos/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';

/**
 * <c>Reposiciones de caja chica</c> (GI-PR4b, P8.5). Dos
 * bloques: saldos heredados por reponer (corte manual) y bandeja de
 * reposiciones emitidas (una por comprobación aprobada).
 */
export function ReposicionesCajaPage() {
  const puedeAdmin = useHasPermission(
    PermisosCanonicos.CuentasPorPagarReposicionesAdministrar,
  );

  const saldos = useSaldosReposicion();
  const reposiciones = useReposiciones();
  const sucursales = useSucursales();
  const emitir = useEmitirReposicionManual();

  const nombreSucursal = (id: string) =>
    sucursales.data?.items.find((s) => s.id === id)?.nombre ?? '—';

  function emitirAhora(s: SaldoPendienteReposicion) {
    emitir.mutate(
      {
        sucursalId: s.sucursalId,
        destino: s.destino,
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: (r) =>
          toast.success(
            `Reposición emitida: ${r.montoTotal.toFixed(2)} ${r.moneda} (${r.numeroComprobaciones} comprobaciones)`,
          ),
        onError: (error) =>
          toast.error(
            esApiError(error) ? error.problem.title : 'Error al emitir.',
          ),
      },
    );
  }

  return (
    <div className="space-y-6">
      <PageHeader
        titulo="Reposiciones de caja chica"
        descripcion="La reposición se genera hacia Tesorería cuando CxP aprueba el pasivo de cada comprobación de caja chica. El corte manual solo vacía saldos heredados de comprobaciones aplicadas sin reposición."
      />

      <section className="space-y-2">
        <h2 className="text-sm font-medium">Saldos por reponer</h2>
        {saldos.isLoading ? (
          <TableSkeleton rows={3} />
        ) : saldos.isError ? (
          <ErrorState onRetry={() => saldos.refetch()} />
        ) : (saldos.data?.length ?? 0) === 0 ? (
          <EmptyState
            title="Sin saldos pendientes"
            description="No hay comprobaciones aplicadas esperando reposición."
          />
        ) : (
          <div className="overflow-x-auto rounded-md border">
            <table className="w-full text-sm">
              <thead className="bg-muted/50 text-left">
                <tr>
                  <th className="px-3 py-2 font-medium">Sucursal</th>
                  <th className="px-3 py-2 font-medium">Destino</th>
                  <th className="px-3 py-2 font-medium text-right">Saldo</th>
                  <th className="px-3 py-2 font-medium text-right">Comps.</th>
                  {puedeAdmin ? <th className="px-3 py-2" /> : null}
                </tr>
              </thead>
              <tbody>
                {saldos.data!.map((s) => (
                  <tr
                    key={`${s.sucursalId}-${s.destino}-${s.moneda}`}
                    className="border-t"
                  >
                    <td className="px-3 py-2">
                      {nombreSucursal(s.sucursalId)}
                    </td>
                    <td className="px-3 py-2">
                      {DestinoReposicionCajaLabels[s.destino]}
                    </td>
                    <td className="px-3 py-2 pr-4 text-right tabular-nums">
                      <MoneyDisplay amount={s.saldo} currency={s.moneda} strong />
                    </td>
                    <td className="px-3 py-2 text-right tabular-nums">
                      {s.numeroComprobaciones}
                    </td>
                    {puedeAdmin ? (
                      <td className="px-3 py-2 text-right">
                        <Button
                          size="sm"
                          variant="outline"
                          disabled={emitir.isPending}
                          onClick={() => emitirAhora(s)}
                        >
                          Emitir ahora
                        </Button>
                      </td>
                    ) : null}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className="space-y-2">
        <h2 className="text-sm font-medium">Reposiciones emitidas</h2>
        {reposiciones.isLoading ? (
          <TableSkeleton rows={3} />
        ) : reposiciones.isError ? (
          <ErrorState onRetry={() => reposiciones.refetch()} />
        ) : (reposiciones.data?.items.length ?? 0) === 0 ? (
          <EmptyState
            title="Sin reposiciones"
            description="Aún no se ha emitido ninguna reposición."
          />
        ) : (
          <div className="overflow-x-auto rounded-md border">
            <table className="w-full text-sm">
              <thead className="bg-muted/50 text-left">
                <tr>
                  <th className="px-3 py-2 font-medium">Fecha</th>
                  <th className="px-3 py-2 font-medium">Sucursal</th>
                  <th className="px-3 py-2 font-medium">Destino</th>
                  <th className="px-3 py-2 font-medium text-right">Monto</th>
                  <th className="px-3 py-2 font-medium text-right">Comps.</th>
                  <th className="px-3 py-2 font-medium">Corte</th>
                </tr>
              </thead>
              <tbody>
                {reposiciones.data!.items.map((r) => (
                  <tr key={r.id} className="border-t">
                    <td className="px-3 py-2 tabular-nums">
                      <DateTimeDisplay value={r.fechaEmision} variant="datetime" />
                    </td>
                    <td className="px-3 py-2">
                      {nombreSucursal(r.sucursalId)}
                    </td>
                    <td className="px-3 py-2">
                      {DestinoReposicionCajaLabels[r.destino]}
                    </td>
                    <td className="px-3 py-2 pr-4 text-right tabular-nums">
                      <MoneyDisplay amount={r.montoTotal} currency={r.moneda} strong />
                    </td>
                    <td className="px-3 py-2 text-right tabular-nums">
                      {r.numeroComprobaciones}
                    </td>
                    <td className="px-3 py-2">
                      {r.esCorteManual ? 'Manual' : 'Automático'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  );
}
