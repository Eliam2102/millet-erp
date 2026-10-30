import { useState } from 'react';
import { Breadcrumbs, BarraFiltros, EmptyState, ErrorState, PaginacionTabla, TableSkeleton } from '@/components/erp';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { PageHeader } from '@/components/erp/PageHeader';
import { SucursalSelector } from '@/components/erp/selectors/SucursalSelector';
import { Badge } from '@/components/ui/badge';
import { Input } from '@/components/ui/input';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { useSucursales } from '@/features/catalogos/api';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { usePiezas } from '../api/hooks';
import type { AvancePosicionDto } from '../api/types';

const PAGE_SIZE = 25;
const columnas = 'grid grid-cols-[minmax(0,1.4fr)_120px_90px_90px_minmax(0,1fr)_110px_170px] items-center';

const avanceUi: Record<AvancePosicionDto['avanceFisico'], { texto: string; tono: 'neutral' | 'warning' | 'success' }> = {
  SinPiezas: { texto: 'Sin piezas', tono: 'neutral' },
  Parcial: { texto: 'Parcial', tono: 'warning' },
  Completo: { texto: 'Completo', tono: 'success' },
};

export function PiezasPage() {
  const [pedido, setPedido] = useState('');
  const [sucursalId, setSucursalId] = useState<string | null>(null);
  const [offset, setOffset] = useState(0);
  const pedidoBuscado = pedido.trim();
  const q = usePiezas({
    pedido: pedidoBuscado || undefined,
    sucursalId: sucursalId ?? undefined,
    page: offset / PAGE_SIZE + 1,
    pageSize: PAGE_SIZE,
  });
  const sucursales = useSucursales();
  const nombreSucursal = (id: string) =>
    sucursales.data?.items.find((s) => s.id === id)?.nombre ?? id.slice(0, 8);
  const items = q.data?.items ?? [];

  return (
    <main className="flex h-full min-h-0 flex-col gap-5 overflow-auto text-sm">
      <Breadcrumbs items={[{ label: 'Producto Terminado' }, { label: 'Piezas' }]} />
      <PageHeader
        titulo="Piezas en producto terminado"
        descripcion="Consulte las piezas escaneadas por pedido y sucursal, con su avance por posición."
      />
      {pedidoBuscado && q.data && q.data.avance.length > 0 && (
        <section aria-label="Avance por posición" className="grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-3">
          {q.data.avance.map((a) => (
            <article key={a.posicion} className="rounded-lg bg-surface-card p-4 shadow-card-flat">
              <header className="flex items-center justify-between gap-2">
                <h2 className="text-md font-semibold">Posición {a.posicion}</h2>
                <Badge variant={avanceUi[a.avanceFisico].tono}>{avanceUi[a.avanceFisico].texto}</Badge>
              </header>
              {a.descripcion && <p className="mt-1 truncate text-xs text-ink-muted">{a.descripcion}</p>}
              <dl className="mt-3 grid grid-cols-3 gap-2 text-center">
                {[
                  ['Esperadas', a.esperadas],
                  ['Terminadas', a.terminadas],
                  ['Faltantes', a.faltantes],
                ].map(([k, v]) => (
                  <div key={k}>
                    <dt className="text-2xs uppercase text-ink-muted">{k}</dt>
                    <dd className="text-lg font-semibold tabular-nums">{v}</dd>
                  </div>
                ))}
              </dl>
            </article>
          ))}
        </section>
      )}
      {q.isError ? (
        <ErrorState
          title="No se pudieron cargar las piezas"
          onRetry={() => {
            void q.refetch();
          }}
        />
      ) : (
        <div className="flex min-h-80 flex-1 flex-col">
          <ListadoModulo
            filtros={
              <BarraFiltros
                busqueda={
                  <Input
                    aria-label="Pedido"
                    placeholder="Número de pedido"
                    value={pedido}
                    onChange={(e) => {
                      setPedido(e.target.value);
                      setOffset(0);
                    }}
                  />
                }
                filtros={
                  <SucursalSelector
                    value={sucursalId}
                    onChange={(id) => {
                      setSucursalId(id);
                      setOffset(0);
                    }}
                    placeholder="Todas las sucursales"
                    triggerProps={{ 'aria-label': 'Sucursal' }}
                  />
                }
                total={q.data ? `${q.data.total} piezas` : undefined}
              />
            }
            pie={
              <PaginacionTabla
                total={q.data?.total ?? 0}
                offset={offset}
                limit={PAGE_SIZE}
                tamanos={[]}
                onChange={(o) => setOffset(o)}
              />
            }
          >
            {q.isLoading ? (
              <TableSkeleton />
            ) : items.length === 0 ? (
              <EmptyState title="No hay piezas para esos filtros" />
            ) : (
              <Table className="grid min-w-0 text-sm">
                <TableHeader className="h-9 bg-surface-subtle text-left text-2xs uppercase text-ink-muted">
                  <TableRow className={`${columnas} h-9`}>
                    {['Etiqueta', 'Pedido', 'Posición', 'N.º de pieza', 'Sucursal', 'Estado', 'Escaneada'].map((x) => (
                      <TableHead key={x} className="px-3">
                        {x}
                      </TableHead>
                    ))}
                  </TableRow>
                </TableHeader>
                <TableBody className="grid">
                  {items.map((p) => (
                    <TableRow key={p.id} className={`${columnas} min-h-12 border-b border-line-row`}>
                      <TableCell className="min-w-0 truncate px-3 font-mono text-xs">{p.etiqueta}</TableCell>
                      <TableCell className="px-3">{p.numeroPedido}</TableCell>
                      <TableCell className="px-3 tabular-nums">{p.posicion}</TableCell>
                      <TableCell className="px-3 tabular-nums">{p.numeroPieza}</TableCell>
                      <TableCell className="min-w-0 truncate px-3">{nombreSucursal(p.sucursalId)}</TableCell>
                      <TableCell className="px-3">
                        <Badge variant="success">En PT</Badge>
                      </TableCell>
                      <TableCell className="px-3 tabular-nums">
                        <DateTimeDisplay value={p.escaneadaAt} variant="datetime" />
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
          </ListadoModulo>
        </div>
      )}
    </main>
  );
}
