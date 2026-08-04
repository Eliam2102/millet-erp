import { useMemo } from 'react';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { HandCoins } from 'lucide-react';
import { Label } from '@/components/ui/label';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { useAnticiposCliente } from '@/features/cxc/api/useCartera';
import { ClienteSelectorCxc } from '@/features/cxc/components/ClienteSelectorCxc';
import { formatoMonto } from '@/features/cxc/lib/glosario';
import type { AnticiposCxcSearch } from '@/features/cxc/lib/cartera-search-schema';
import { esApiError } from '@/lib/api';
import { cn } from '@/lib/utils';

const FROM = '/_app/cxc/anticipos/' as const;

/**
 * <c>Anticipos de clientes</c> (CXC-FE-PR5) — saldos del read port de
 * Facturación (§6.1 del 01-diseño; reemplazo del "mapa" A+W). Los
 * anticipos NO son cartera: es la vista de "dinero del cliente en la
 * casa". Totales POR MONEDA — nunca se suman divisas distintas (§5).
 */
export function AnticiposCxcPage() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const clienteId = search.clienteId ?? null;

  const query = useAnticiposCliente(clienteId);

  function actualizar(parcial: Partial<AnticiposCxcSearch>) {
    navigate({ to: '/cxc/anticipos', search: { ...search, ...parcial } });
  }

  // Totales por moneda (nunca cross-divisa).
  const totalesPorMoneda = useMemo(() => {
    const m = new Map<string, { saldo: number; disponible: number }>();
    for (const a of query.data ?? []) {
      const acc = m.get(a.moneda) ?? { saldo: 0, disponible: 0 };
      acc.saldo += a.saldo;
      acc.disponible += a.saldoDisponible;
      m.set(a.moneda, acc);
    }
    return Array.from(m.entries()).sort(([a], [b]) => a.localeCompare(b));
  }, [query.data]);

  return (
    <div className="space-y-4 px-4 py-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">
          Anticipos de clientes
        </h1>
        <p className="text-sm text-muted-foreground">
          Saldos de anticipo por cliente (emisión y amortización viven en
          Facturación; aquí se consulta el dinero del cliente en la casa).
        </p>
      </div>

      <div className="w-full max-w-md space-y-1">
        <Label className="text-xs text-muted-foreground">
          Cliente (obligatorio)
        </Label>
        <ClienteSelectorCxc
          value={clienteId}
          onChange={(item) => actualizar({ clienteId: item?.id ?? undefined })}
          placeholder="Elige el cliente…"
        />
      </div>

      {clienteId == null ? (
        <EmptyState
          icon={<HandCoins className="h-10 w-10" />}
          title="Elige un cliente para ver sus anticipos."
          description="Los saldos se consultan por cliente vía el read port de Facturación."
        />
      ) : query.isError ? (
        <ErrorState
          title="No se pudieron cargar los anticipos"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={4}
          columns={[
            { width: 'w-24' },
            { width: 'w-28' },
            { width: 'w-28' },
            { width: 'w-28' },
            { width: 'w-28' },
            { width: 'w-32' },
          ]}
        />
      ) : (query.data?.length ?? 0) === 0 ? (
        <EmptyState
          icon={<HandCoins className="h-10 w-10" />}
          title="El cliente no tiene anticipos."
          description="Los anticipos se emiten desde Facturación (serie FANT)."
        />
      ) : (
        <>
          <div className="overflow-x-auto rounded-md border">
            <table className="w-full text-sm">
              <thead className="bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
                <tr>
                  <th className="px-3 py-2 font-medium">Estado</th>
                  <th className="px-3 py-2 text-right font-medium">Cobrado</th>
                  <th className="px-3 py-2 text-right font-medium">
                    Amortizado
                  </th>
                  <th className="px-3 py-2 text-right font-medium">Saldo</th>
                  <th className="px-3 py-2 text-right font-medium">
                    Disponible
                  </th>
                  <th className="px-3 py-2 font-medium">Pedido origen</th>
                </tr>
              </thead>
              <tbody className="divide-y">
                {query.data!.map((a) => (
                  <tr key={a.anticipoId} className="hover:bg-muted/30">
                    <td className="px-3 py-2">
                      <span
                        className={cn(
                          'inline-flex items-center rounded-full px-2 py-0.5 text-[11px] font-medium',
                          a.estado === 'Abierto'
                            ? 'bg-emerald-100 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-300'
                            : a.estado === 'Cancelado'
                              ? 'bg-red-100 text-red-800 dark:bg-red-950 dark:text-red-300'
                              : 'bg-muted text-muted-foreground',
                        )}
                      >
                        {a.estado}
                      </span>
                    </td>
                    <td className="px-3 py-2 text-right font-mono tabular-nums">
                      {formatoMonto(a.montoCobrado, a.moneda)}
                    </td>
                    <td className="px-3 py-2 text-right font-mono tabular-nums">
                      {formatoMonto(a.montoAmortizado, a.moneda)}
                    </td>
                    <td className="px-3 py-2 text-right font-mono tabular-nums">
                      {formatoMonto(a.saldo, a.moneda)}
                    </td>
                    <td className="px-3 py-2 text-right font-mono font-medium tabular-nums">
                      {formatoMonto(a.saldoDisponible, a.moneda)}
                    </td>
                    <td className="px-3 py-2 font-mono text-xs">
                      {a.pedidoOrigenRef ?? '—'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="flex flex-wrap gap-4">
            {totalesPorMoneda.map(([moneda, t]) => (
              <div
                key={moneda}
                className="rounded-md border bg-card px-4 py-2 text-sm"
              >
                <span className="text-xs text-muted-foreground">
                  Total {moneda}:{' '}
                </span>
                <span className="font-mono tabular-nums">
                  saldo {formatoMonto(t.saldo, moneda)} · disponible{' '}
                  {formatoMonto(t.disponible, moneda)}
                </span>
              </div>
            ))}
          </div>
        </>
      )}
    </div>
  );
}
