import {
  ListadoModulo,
  TabsEstado,
  BarraFiltros,
  PaginacionTabla,
  KpiCard,
  MontoMovimiento,
  PageHeader,
  EmptyState,
  ErrorState,
  TableSkeleton,
} from '@/components/erp';

import { usePaginacionLocal } from '../lib/use-paginacion-local';

import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '@/components/ui/table';
import { useMemo, useState } from 'react';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { HandCoins } from 'lucide-react';
import { Label } from '@/components/ui/label';

import { useAnticiposCliente } from '@/features/cxc/api/useCartera';
import { ClienteSelectorCxc } from '@/features/cxc/components/ClienteSelectorCxc';
import { formatoMonto } from '@/features/cxc/lib/glosario';
import type { AnticiposCxcSearch } from '@/features/cxc/lib/cartera-search-schema';
import { esApiError } from '@/lib/api';
import { EstadoCxcBadge } from '../components/EstadoCxcBadge';

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

  const [estado, setEstado] = useState<number | undefined>();
  const estados = ['Abierto', 'Amortizado', 'Cancelado'];
  const items = (query.data ?? []).filter(
    (a) => estado === undefined || a.estado === estados[estado],
  );
  const pagina = usePaginacionLocal(items, JSON.stringify([clienteId, estado]));

  return (
    <div className="flex min-h-0 flex-col gap-5">
      <div>
        <PageHeader
          titulo={<>Anticipos de clientes</>}
          descripcion={
            <>
              Saldos de anticipo por cliente (emisión y amortización viven en Facturación; aquí se
              consulta el dinero del cliente en la casa).
            </>
          }
        />
      </div>

      <ListadoModulo
        tabs={
          <TabsEstado
            opciones={estados.map((label, valor) => ({ label, valor }))}
            valor={estado}
            conteos={undefined}
            onChange={setEstado}
          />
        }
        filtros={
          <BarraFiltros
            filtros={
              <div className="w-full max-w-md space-y-1">
                <Label className="text-xs text-ink-muted">Cliente *</Label>
                <ClienteSelectorCxc
                  value={clienteId}
                  onChange={(item) => actualizar({ clienteId: item?.id ?? undefined })}
                  placeholder="Elige el cliente…"
                />
              </div>
            }
          />
        }
        pie={
          <PaginacionTabla
            total={items.length}
            offset={pagina.offset}
            limit={10}
            tamanos={[]}
            onChange={pagina.cambiar}
          />
        }
      >
        {clienteId == null ? (
          <EmptyState
            icon={<HandCoins className="h-10 w-10" />}
            title="Elige un cliente para ver sus anticipos."
            description="Consulta los saldos de anticipos registrados en Facturación."
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
        ) : items.length === 0 ? (
          <EmptyState
            icon={<HandCoins className="h-10 w-10" />}
            title="El cliente no tiene anticipos."
            description="Los anticipos se emiten desde Facturación."
          />
        ) : (
          <>
            <div className="overflow-x-auto rounded-md border">
              <Table className="w-full text-sm">
                <TableHeader className="bg-surface-muted/50 text-left text-xs uppercase tracking-wide text-ink-muted">
                  <TableRow>
                    <TableHead className="px-3 py-2 font-medium">Estado</TableHead>
                    <TableHead className="px-3 py-2 text-right font-medium">Cobrado</TableHead>
                    <TableHead className="px-3 py-2 text-right font-medium">Amortizado</TableHead>
                    <TableHead className="px-3 py-2 text-right font-medium">Saldo</TableHead>
                    <TableHead className="px-3 py-2 text-right font-medium">Disponible</TableHead>
                    <TableHead className="px-3 py-2 font-medium">Pedido origen</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody className="divide-y">
                  {pagina.visibles.map((a) => (
                    <TableRow key={a.anticipoId} className="h-12 hover:bg-surface-subtle">
                      <TableCell className="px-3 py-2">
                        <EstadoCxcBadge entidad="anticipo" estado={a.estado} />
                      </TableCell>
                      <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                        <MontoMovimiento monto={a.montoCobrado} moneda={a.moneda} tipo="entrada" />
                      </TableCell>
                      <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                        <MontoMovimiento
                          monto={a.montoAmortizado}
                          moneda={a.moneda}
                          tipo="salida"
                        />
                      </TableCell>
                      <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                        {formatoMonto(a.saldo, a.moneda)}
                      </TableCell>
                      <TableCell className="px-3 py-2 text-right font-mono font-medium tabular-nums">
                        {formatoMonto(a.saldoDisponible, a.moneda)}
                      </TableCell>
                      <TableCell className="px-3 py-2 font-mono text-xs">
                        {a.pedidoOrigenRef ?? '—'}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>

            <div className="flex flex-wrap gap-4">
              {totalesPorMoneda.map(([moneda, t]) => (
                <div key={moneda} className="grid gap-3 sm:grid-cols-2">
                  <KpiCard etiqueta={`Saldo ${moneda}`} valor={formatoMonto(t.saldo, moneda)} />
                  <KpiCard
                    etiqueta={`Disponible ${moneda}`}
                    valor={formatoMonto(t.disponible, moneda)}
                    tono="info"
                  />
                </div>
              ))}
            </div>
          </>
        )}
      </ListadoModulo>
    </div>
  );
}
