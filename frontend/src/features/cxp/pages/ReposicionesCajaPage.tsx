import { useState } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  EmptyState,
  ErrorState,
  SucursalSelector,
  TableSkeleton,
} from '@/components/erp';
import {
  useConfigurarReposicion,
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

/**
 * <c>Reposiciones de caja chica</c> (GI-PR4b, doc 12 §D2/Q4). Tres
 * bloques: saldos acumulados por reponer (con corte manual), mínimo por
 * sucursal (upsert) y bandeja de reposiciones emitidas.
 */
export function ReposicionesCajaPage() {
  const puedeAdmin = useHasPermission(
    PermisosCanonicos.CuentasPorPagarReposicionesAdministrar,
  );

  const saldos = useSaldosReposicion();
  const reposiciones = useReposiciones();
  const sucursales = useSucursales();
  const emitir = useEmitirReposicionManual();
  const configurar = useConfigurarReposicion();

  const nombreSucursal = (id: string) =>
    sucursales.data?.items.find((s) => s.id === id)?.nombre ?? id.slice(0, 8);

  const [configSucursalId, setConfigSucursalId] = useState<string | null>(null);
  const [configMinimo, setConfigMinimo] = useState('');

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

  function guardarMinimo() {
    const monto = Number(configMinimo);
    if (!configSucursalId || Number.isNaN(monto) || monto < 0) {
      toast.error('Elige sucursal y un mínimo válido (≥ 0).');
      return;
    }
    configurar.mutate(
      {
        sucursalId: configSucursalId,
        montoMinimo: monto,
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: (r) =>
          toast.success(`Mínimo guardado: ${r.montoMinimo.toFixed(2)}`),
        onError: (error) =>
          toast.error(
            esApiError(error) ? error.problem.title : 'Error al guardar.',
          ),
      },
    );
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-lg font-semibold">Reposiciones de caja chica</h1>
        <p className="text-sm text-muted-foreground">
          Los montos de las comprobaciones aplicadas se acumulan por sucursal
          y destino; al alcanzar el mínimo se emite la reposición hacia
          Tesorería (o antes, con el corte manual).
        </p>
      </div>

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
                  <th className="px-3 py-2 font-medium text-right">Mínimo</th>
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
                    <td className="px-3 py-2 text-right tabular-nums">
                      {s.saldo.toFixed(2)} {s.moneda}
                    </td>
                    <td className="px-3 py-2 text-right tabular-nums">
                      {s.numeroComprobaciones}
                    </td>
                    <td className="px-3 py-2 text-right tabular-nums">
                      {s.montoMinimo.toFixed(2)}
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

      {puedeAdmin ? (
        <section className="space-y-2">
          <h2 className="text-sm font-medium">Mínimo por sucursal</h2>
          <div className="flex flex-wrap items-end gap-2">
            <div className="w-64">
              <SucursalSelector
                value={configSucursalId}
                onChange={setConfigSucursalId}
              />
            </div>
            <Input
              type="number"
              min={0}
              step="0.01"
              placeholder="Monto mínimo (0 = inmediata)"
              className="w-56"
              value={configMinimo}
              onChange={(e) => setConfigMinimo(e.target.value)}
            />
            <Button
              onClick={guardarMinimo}
              disabled={configurar.isPending || !configSucursalId}
            >
              Guardar mínimo
            </Button>
          </div>
        </section>
      ) : null}

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
                      {new Date(r.fechaEmision).toLocaleString()}
                    </td>
                    <td className="px-3 py-2">
                      {nombreSucursal(r.sucursalId)}
                    </td>
                    <td className="px-3 py-2">
                      {DestinoReposicionCajaLabels[r.destino]}
                    </td>
                    <td className="px-3 py-2 text-right tabular-nums">
                      {r.montoTotal.toFixed(2)} {r.moneda}
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
