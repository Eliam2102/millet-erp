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
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { usePiezas } from '../api/hooks';
import type { AvancePosicionDto, PiezaDto } from '../api/types';
import { textoMotivoPedido } from '../lib/motivos-ui';
import { SolicitarParcialSheet, UbicarPiezasSheet } from './PiezasSheets';
import { TramoDetenidoBanner } from './TramoDetenidoBanner';

const PAGE_SIZE = 25;
// Con un pedido filtrado caben todas sus piezas en una página para poder pedir la salida parcial.
const PAGE_SIZE_PEDIDO = 100;
const columnas = 'grid grid-cols-[36px_minmax(0,1.3fr)_90px_64px_64px_minmax(0,1fr)_150px_minmax(0,1.2fr)_minmax(0,1.2fr)_140px] items-center';
const enPt = (p: PiezaDto) => p.estado.toLowerCase() === 'enpt';
const almacenTexto = { Nacional: 'Nacional', Exportacion: 'Exportación' } as const;

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
  const pageSize = pedidoBuscado ? PAGE_SIZE_PEDIDO : PAGE_SIZE;
  const operar = useAuthStore((s) => s.permisos.includes(P.ProductoTerminadoExcepcionesGestionar));
  const [seleccion, setSeleccion] = useState<string[]>([]);
  const [hoja, setHoja] = useState<'ubicar' | 'parcial' | null>(null);
  const q = usePiezas({
    pedido: pedidoBuscado || undefined,
    sucursalId: sucursalId ?? undefined,
    page: offset / pageSize + 1,
    pageSize,
  });
  const sucursales = useSucursales();
  const nombreSucursal = (id: string) =>
    sucursales.data?.items.find((s) => s.id === id)?.nombre ?? 'Sin sucursal';
  const items = q.data?.items ?? [];
  const alternar = (et: string) => setSeleccion((s) => (s.includes(et) ? s.filter((x) => x !== et) : [...s, et]));
  const piezasPedidoEnPt = items.filter((p) => enPt(p) && p.numeroPedido === pedidoBuscado);

  return (
    <main className="flex h-full min-h-0 flex-col gap-5 overflow-auto text-sm">
      <Breadcrumbs items={[{ label: "Producto terminado" }, { label: 'Piezas' }]} />
      <PageHeader
        titulo="Piezas en producto terminado"
        descripcion="Consulta las piezas escaneadas por pedido y sucursal, con su avance por partida."
      />
      <TramoDetenidoBanner />
      {operar && (
        <div className="flex flex-wrap items-center gap-2">
          <Button variant="outline" disabled={seleccion.length === 0} onClick={() => setHoja('ubicar')}>
            Ubicar{seleccion.length > 0 ? ` (${seleccion.length})` : ''}
          </Button>
          {pedidoBuscado && (
            <Button variant="outline" disabled={piezasPedidoEnPt.length === 0} onClick={() => setHoja('parcial')}>
              Solicitar salida parcial
            </Button>
          )}
          {seleccion.length === 0 && <span className="text-xs text-ink-muted">Marca piezas En PT para ubicarlas.</span>}
        </div>
      )}
      {pedidoBuscado && q.data && (q.data.estadoPedido || q.data.avance.length > 0) && (
        <section aria-label="Estado del pedido" className="flex flex-wrap items-center gap-3 rounded-lg bg-surface-card p-4 shadow-card-flat">
          <h2 className="text-md font-semibold">Estado del pedido</h2>
          {q.data.estadoPedido ? (
            <>
              <Badge variant={q.data.estadoPedido.estado === 'Liberado' ? 'success' : 'warning'}>{q.data.estadoPedido.estado}</Badge>
              <span>{textoMotivoPedido(q.data.estadoPedido.motivo)}</span>
              {q.data.estadoPedido.desde && (
                <span className="text-xs text-ink-muted">
                  desde <DateTimeDisplay value={q.data.estadoPedido.desde} variant="datetime" />
                </span>
              )}
            </>
          ) : (
            // Sin estatus hasta que el pedido está completo en PT (P-096).
            <Badge variant="neutral">En producción</Badge>
          )}
        </section>
      )}
      {pedidoBuscado && q.data && q.data.avance.length > 0 && (
        <section aria-label="Avance por partida" className="grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-3">
          {q.data.avance.map((a) => (
            <article key={a.posicion} className="rounded-lg bg-surface-card p-4 shadow-card-flat">
              <header className="flex items-center justify-between gap-2">
                <h2 className="text-md font-semibold">Partida {a.posicion}</h2>
                <Badge variant={avanceUi[a.avanceFisico].tono}>{avanceUi[a.avanceFisico].texto}</Badge>
              </header>
              {a.descripcion && <p className="mt-1 truncate text-xs text-ink-muted">{a.descripcion}</p>}
              <dl className="mt-3 grid grid-cols-3 gap-2 text-center">
                {[
                  ['Esperadas', a.esperadas],
                  ['Terminadas', a.terminadas],
                  ['En PT', a.enPt],
                  ['Cargadas', a.cargadas],
                  ['Entregadas', a.entregadas],
                  ['Pendientes', a.pendientes],
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
                limit={pageSize}
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
              <Table className="grid min-w-[1200px] text-sm">
                <TableHeader className="h-auto min-h-9 bg-surface-subtle text-left text-2xs uppercase text-ink-muted">
                  <TableRow className={`${columnas} min-h-9`}>
                    {['', 'Etiqueta', 'Pedido', 'Partida', 'N.º de pieza', 'Sucursal', 'Estado', 'Ubicación', 'Estación de origen', 'Escaneada'].map((x, i) => (
                      <TableHead key={x || i} className="px-3">
                        {x || <span className="sr-only">Seleccionar</span>}
                      </TableHead>
                    ))}
                  </TableRow>
                </TableHeader>
                <TableBody className="grid">
                  {items.map((p) => (
                    <TableRow key={p.id} className={`${columnas} min-h-12 border-b border-line-row`}>
                      <TableCell className="px-3">
                        {operar && enPt(p) && (
                          <Checkbox aria-label={`Seleccionar ${p.etiqueta}`} checked={seleccion.includes(p.etiqueta)} onCheckedChange={() => alternar(p.etiqueta)} />
                        )}
                      </TableCell>
                      <TableCell className="min-w-0 truncate px-3 font-mono text-xs">{p.etiqueta}</TableCell>
                      <TableCell className="px-3">{p.numeroPedido}</TableCell>
                      <TableCell className="px-3 tabular-nums">{p.posicion}</TableCell>
                      <TableCell className="px-3 tabular-nums">{p.numeroPieza}</TableCell>
                      <TableCell className="min-w-0 truncate px-3">{nombreSucursal(p.sucursalId)}</TableCell>
                      <TableCell className="px-3">
                        {p.estado === 'EnTransito' ? (
                          <Badge variant="info" className="h-auto min-h-ctl-xs whitespace-normal">
                            En tránsito{p.sucursalDestinoId ? ` a ${nombreSucursal(p.sucursalDestinoId)}` : ''}
                          </Badge>
                        ) : p.estado === 'Cargada' ? (
                          <Badge variant="warning">Cargada</Badge>
                        ) : p.estado === 'Embarcada' ? (
                          <Badge variant="neutral">Embarcada</Badge>
                        ) : p.estado === 'Entregada' ? (
                          <Badge variant="success">Entregada</Badge>
                        ) : p.estado === 'EnRevision' ? (
                          <Badge variant="warning">En revisión</Badge>
                        ) : p.estado === 'EnReproceso' ? (
                          <Badge variant="warning">En reproceso</Badge>
                        ) : p.estado === 'Baja' ? (
                          <Badge variant="neutral">Baja</Badge>
                        ) : (
                          <Badge variant="success">En PT</Badge>
                        )}
                        {p.costoPendiente && <Badge variant="warning" className="ml-1">Costo pendiente</Badge>}
                      </TableCell>
                      <TableCell className="min-w-0 px-3">
                        {p.porUbicar ? (
                          <Badge variant="warning">Por ubicar</Badge>
                        ) : p.almacen ? (
                          <span>{almacenTexto[p.almacen]}{p.lugar ? ` · ${p.lugar}` : ''}</span>
                        ) : (
                          <span className="text-ink-muted">—</span>
                        )}
                      </TableCell>
                      <TableCell className="min-w-0 px-3 text-xs">
                        {p.estacionOrigen != null ? (
                          <>
                            <span className="tabular-nums">Estación {p.estacionOrigen}</span>{p.areaOrigen ? ` · ${p.areaOrigen}` : ''}
                            {p.terminadaAwAt && <p className="text-ink-muted tabular-nums"><DateTimeDisplay value={p.terminadaAwAt} variant="datetime" /></p>}
                          </>
                        ) : (
                          <span className="text-ink-muted">—</span>
                        )}
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
      {hoja === 'ubicar' && (
        <UbicarPiezasSheet etiquetas={seleccion} onCerrar={() => setHoja(null)} onListo={() => { setHoja(null); setSeleccion([]); }} />
      )}
      {hoja === 'parcial' && (
        <SolicitarParcialSheet pedido={pedidoBuscado} piezas={piezasPedidoEnPt} onCerrar={() => setHoja(null)} onListo={() => setHoja(null)} />
      )}
    </main>
  );
}
