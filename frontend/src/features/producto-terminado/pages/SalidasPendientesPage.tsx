import { useState } from 'react';
import { Breadcrumbs, BarraFiltros, EmptyState, ErrorState, PaginacionTabla, TableSkeleton } from '@/components/erp';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { PageHeader } from '@/components/erp/PageHeader';
import { SucursalSelector } from '@/components/erp/selectors/SucursalSelector';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { Badge } from '@/components/ui/badge';
import { Input } from '@/components/ui/input';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { useSucursales } from '@/features/catalogos/api';
import { cn } from '@/lib/utils';
import { useSalidasPendientes } from '../api/hooks';
import type { AlertaSalida, SalidaPendienteDto } from '../api/types';

const PAGE_SIZE = 25;
const columnas = 'grid grid-cols-[minmax(0,1fr)_minmax(0,1.4fr)_minmax(0,1fr)_80px_minmax(0,1fr)_170px_120px] items-center';
const TODAS = 'todas';
type FiltroAlerta = '24h' | '48h';

// Texto además del color: la alerta no depende solo de la vista.
const alertaUi: Record<Exclude<AlertaSalida, 'ninguna'>, { texto: string; tono: 'warning' | 'danger' }> = {
  '24h': { texto: '24 h', tono: 'warning' },
  '48h': { texto: '48 h o más', tono: 'danger' },
};

const hace = (h: number) => `hace ${Math.floor(h)} h`;

function AlertaBadge({ alerta }: { alerta: AlertaSalida }) {
  if (alerta === 'ninguna') return null;
  const u = alertaUi[alerta];
  return <Badge variant={u.tono}>{u.texto}</Badge>;
}

function Contador({ etiqueta, valor, activo, tono, onClick }: {
  etiqueta: string;
  valor: number | undefined;
  activo: boolean;
  tono: 'warning' | 'danger';
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      aria-pressed={activo}
      onClick={onClick}
      className={cn(
        'flex min-h-touch flex-1 flex-col items-start rounded-lg bg-surface-card p-3 text-left shadow-card-flat',
        'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand',
        activo && (tono === 'danger' ? 'ring-2 ring-danger-fg' : 'ring-2 ring-warning-fg'),
      )}
    >
      <span className="text-xs text-ink-muted">{etiqueta}</span>
      <span className="text-xl font-semibold tabular-nums">{valor ?? '–'}</span>
    </button>
  );
}

export function SalidasPendientesPage() {
  const [buscar, setBuscar] = useState('');
  const [sucursalId, setSucursalId] = useState<string | null>(null);
  const [alerta, setAlerta] = useState<FiltroAlerta | null>(null);
  const [offset, setOffset] = useState(0);
  const texto = buscar.trim();
  const q = useSalidasPendientes({
    sucursalId: sucursalId ?? undefined,
    alerta: alerta ?? undefined,
    buscar: texto || undefined,
    page: offset / PAGE_SIZE + 1,
    pageSize: PAGE_SIZE,
  });
  const sucursales = useSucursales();
  const nombreSucursal = (id: string) => sucursales.data?.items.find((s) => s.id === id)?.nombre ?? id.slice(0, 8);
  const items: SalidaPendienteDto[] = q.data?.items ?? [];
  const elegir = (a: FiltroAlerta | null) => {
    setAlerta(a);
    setOffset(0);
  };

  return (
    <main className="flex h-full min-h-0 flex-col gap-5 overflow-auto text-sm">
      <Breadcrumbs items={[{ label: 'Producto Terminado' }, { label: 'Salidas pendientes' }]} />
      <PageHeader
        titulo="Salidas pendientes"
        descripcion="Pedidos pagados y completos en producto terminado que todavía no salen. Se actualiza cada minuto."
      />
      <section aria-label="Pedidos con retraso" className="flex gap-3">
        <Contador etiqueta="Entre 24 y 48 h" valor={q.data?.con24h} tono="warning" activo={alerta === '24h'} onClick={() => elegir(alerta === '24h' ? null : '24h')} />
        <Contador etiqueta="48 h o más" valor={q.data?.con48h} tono="danger" activo={alerta === '48h'} onClick={() => elegir(alerta === '48h' ? null : '48h')} />
      </section>
      {q.isError ? (
        <ErrorState
          title="No se pudieron cargar las salidas pendientes"
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
                    aria-label="Buscar pedido o cliente"
                    placeholder="Pedido o cliente"
                    value={buscar}
                    onChange={(e) => {
                      setBuscar(e.target.value);
                      setOffset(0);
                    }}
                  />
                }
                filtros={
                  <>
                    <SucursalSelector
                      value={sucursalId}
                      onChange={(id) => {
                        setSucursalId(id);
                        setOffset(0);
                      }}
                      placeholder="Todas las sucursales"
                      triggerProps={{ 'aria-label': 'Sucursal' }}
                    />
                    <Select value={alerta ?? TODAS} onValueChange={(v) => elegir(v === TODAS ? null : (v as FiltroAlerta))}>
                      <SelectTrigger aria-label="Alerta" className="h-9">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value={TODAS}>Todas</SelectItem>
                        <SelectItem value="24h">24 h</SelectItem>
                        <SelectItem value="48h">48 h o más</SelectItem>
                      </SelectContent>
                    </Select>
                  </>
                }
                total={q.data ? `${q.data.total} pedidos` : undefined}
              />
            }
            pie={<PaginacionTabla total={q.data?.total ?? 0} offset={offset} limit={PAGE_SIZE} tamanos={[]} onChange={(o) => setOffset(o)} />}
          >
            {q.isLoading ? (
              <TableSkeleton />
            ) : items.length === 0 ? (
              <EmptyState title="No hay pedidos pendientes de salir." />
            ) : (
              <>
                <ul className="grid gap-3 p-3 md:hidden" aria-label="Pedidos pendientes de salir">
                  {items.map((s) => (
                    <li key={s.pedidoPtId} className="rounded-lg bg-surface-card p-3 shadow-card-flat">
                      <div className="flex items-start justify-between gap-2">
                        <span className="min-w-0 break-words text-md font-semibold">{s.numeroPedido}</span>
                        <AlertaBadge alerta={s.alerta} />
                      </div>
                      <p className="mt-1 break-words">{s.cliente}</p>
                      {s.destino && <p className="break-words text-xs text-ink-muted">{s.destino}</p>}
                      <p className="mt-2 text-xs text-ink-muted">
                        {s.piezas} piezas · {nombreSucursal(s.sucursalId)}
                      </p>
                      <p className="mt-1 text-xs">
                        <strong className="font-semibold">{hace(s.horasSinSalida)}</strong>
                        {' · liberado '}
                        <DateTimeDisplay value={s.liberadoAt} variant="datetime" />
                      </p>
                    </li>
                  ))}
                </ul>
                <Table className="hidden min-w-[1000px] text-sm md:grid">
                  <TableHeader className="h-auto min-h-9 bg-surface-subtle text-left text-2xs uppercase text-ink-muted">
                    <TableRow className={`${columnas} min-h-9`}>
                      {['Pedido', 'Cliente', 'Destino', 'Piezas', 'Sucursal', 'Liberado', 'Alerta'].map((x) => (
                        <TableHead key={x} className="px-3">
                          {x}
                        </TableHead>
                      ))}
                    </TableRow>
                  </TableHeader>
                  <TableBody className="grid">
                    {items.map((s) => (
                      <TableRow key={s.pedidoPtId} className={`${columnas} min-h-12 border-b border-line-row`}>
                        <TableCell className="min-w-0 truncate px-3 font-medium">{s.numeroPedido}</TableCell>
                        <TableCell className="min-w-0 truncate px-3" title={s.cliente}>{s.cliente}</TableCell>
                        <TableCell className="min-w-0 truncate px-3" title={s.destino ?? undefined}>{s.destino ?? '—'}</TableCell>
                        <TableCell className="px-3 tabular-nums">{s.piezas}</TableCell>
                        <TableCell className="min-w-0 truncate px-3">{nombreSucursal(s.sucursalId)}</TableCell>
                        <TableCell className="px-3 tabular-nums">
                          <div>{hace(s.horasSinSalida)}</div>
                          <div className="text-xs text-ink-muted">
                            <DateTimeDisplay value={s.liberadoAt} variant="datetime" />
                          </div>
                        </TableCell>
                        <TableCell className="px-3">
                          <AlertaBadge alerta={s.alerta} />
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </>
            )}
          </ListadoModulo>
        </div>
      )}
    </main>
  );
}
