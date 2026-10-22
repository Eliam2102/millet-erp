import { BarraFiltros, PaginacionTabla, TabsEstado } from '@/components/erp';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { usePaginacionLocal } from '../lib/use-paginacion-local';
import { useState } from 'react';
import { Loader2 } from 'lucide-react';
import { Link, useNavigate } from '@tanstack/react-router';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { esApiError } from '@/lib/api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PageHeader } from '@/components/erp/PageHeader';
import {
  lecturaAwApagada,
  MOTIVO_LECTURA_AW_APAGADA,
  useAwOrigen,
} from '@/modules/datos-maestros/api/aw-origen';
import {
  mensajeErrorSincronizacion,
  useEjecucionesSync,
} from '@/modules/datos-maestros/api/clientes';
import { RfcRepetidosSeccion } from '@/modules/datos-maestros/components/RfcRepetidosSeccion';
import { EstadoCorridaBadge } from '@/modules/datos-maestros/components/EstadoCorridaBadge';
import {
  fechaCorridaAw,
  useConflictosAw,
  useIniciarCorridaAw,
  useMatrizAw,
  useResolverConflictoAw,
  TIPO_CAMBIO_AW,
  type CambioMaestroAw,
  type EntidadConflictoAw,
} from '@/modules/datos-maestros/api/sincronizacion-aw';

const POR_PAGINA = 10;
const ENTIDAD_MATRIZ: Record<string, string> = { Cliente: 'Cliente', ProductoAw: 'Producto A+W' };

function mensaje(error: unknown) {
  return esApiError(error)
    ? (error.problem.detail ?? error.problem.title)
    : 'No se pudo resolver el conflicto. Reintenta.';
}

/** Pantalla «Sincronización con A+W» (ADM-06): conflictos de clientes y productos por resolver y matriz campo → dueño. */
export function SincronizacionAwPage() {
  const puedeClientes = useHasPermission(PermisosCanonicos.DatosMaestrosClientesSincronizar);
  const puedeProductos = useHasPermission(PermisosCanonicos.DatosMaestrosProductosAwGestionar);
  const [entidadElegida, setEntidad] = useState<EntidadConflictoAw>('clientes');
  const entidad: EntidadConflictoAw =
    entidadElegida === 'clientes' && !puedeClientes && puedeProductos
      ? 'productos-aw'
      : entidadElegida;
  const navigate = useNavigate();
  const corridas = useEjecucionesSync({ limit: 20 }, puedeClientes);
  const iniciarCorrida = useIniciarCorridaAw();
  const apagada = lecturaAwApagada(useAwOrigen(puedeClientes).data);
  const [estado, setEstado] = useState<'Pendiente' | 'Resuelto' | 'Todos'>('Pendiente');
  const [pagina, setPagina] = useState(0);
  const [confirmar, setConfirmar] = useState<CambioMaestroAw | null>(null);
  const [accionProducto, setAccionProducto] = useState<'AplicarAw' | 'MantenerErp'>('AplicarAw');
  const habilitada = entidad === 'clientes' ? puedeClientes : puedeProductos;
  const pendientes = useConflictosAw(entidad, 'Pendiente', habilitada);
  const resueltos = useConflictosAw(entidad, 'Resuelto', habilitada);
  const matriz = useMatrizAw();
  const resolver = useResolverConflictoAw();
  const lista = estado === 'Pendiente' ? pendientes : resueltos;
  const filas =
    estado === 'Todos'
      ? [...(pendientes.data ?? []), ...(resueltos.data ?? [])]
      : (lista.data ?? []);
  const paginaCorridas = usePaginacionLocal(corridas.data?.items ?? []);
  const visibles = filas.slice(pagina * POR_PAGINA, (pagina + 1) * POR_PAGINA);

  function correr() {
    iniciarCorrida.mutate(undefined, {
      onSuccess: (r) => {
        void navigate({ to: '/admin/datos-maestros/sincronizacion-aw/$id', params: { id: r.id } });
      },
      onError: (e) => toast.error(mensajeErrorSincronizacion(e)),
    });
  }

  function resolverConflicto(c: CambioMaestroAw) {
    resolver.mutate(
      {
        entidad,
        id: c.id,
        resolucion: c.tipo === 'EditadoEnErp' ? accionProducto : 'MantenerSeparados',
        version: c.version,
      },
      {
        onSuccess: () => {
          toast.success('Conflicto resuelto');
          setConfirmar(null);
        },
        onError: (e) => toast.error(mensaje(e)),
      },
    );
  }

  return (
    <div className="space-y-5 text-sm text-ink">
      <nav aria-label="Migas" className="text-xs text-ink-muted">
        Administración / Datos maestros / Sincronización con A+W
      </nav>
      <PageHeader titulo="Sincronización con A+W" />

      <section className="rounded-lg bg-surface-card p-4 shadow-card-flat" aria-label="Corridas">
        <div className="flex items-center justify-between gap-3">
          <h2 className="text-lg font-semibold">Corridas</h2>
          <div className="flex items-center gap-3">
            {apagada && (
              <p role="status" className="text-xs text-ink-muted">
                {MOTIVO_LECTURA_AW_APAGADA}
              </p>
            )}
            {iniciarCorrida.isPending && (
              <p role="status" className="max-w-xs text-xs text-ink-muted">
                Leyendo productos de A+W. Puede tardar un par de minutos; no cierres esta pestaña
                hasta que se abra la corrida.
              </p>
            )}
            <Button
              aria-busy={iniciarCorrida.isPending}
              disabled={iniciarCorrida.isPending || !puedeProductos || !puedeClientes || apagada}
              title={
                !puedeClientes
                  ? 'Necesitas permiso de sincronización de clientes para correr la sincronización.'
                  : puedeProductos
                    ? undefined
                    : 'Necesitas permiso de productos A+W para correr clientes y productos juntos.'
              }
              onClick={correr}
            >
              {iniciarCorrida.isPending && (
                <Loader2 aria-hidden className="mr-1.5 h-4 w-4 animate-spin motion-reduce:animate-none" />
              )}
              {iniciarCorrida.isPending ? 'Iniciando…' : 'Correr sincronización'}
            </Button>
          </div>
        </div>
        {corridas.isError && (
          <p role="alert" className="py-3 text-danger-fg">
            No se pudieron cargar las corridas.
          </p>
        )}
        {!corridas.isError && !corridas.isLoading && (corridas.data?.items.length ?? 0) === 0 && (
          <p className="py-3 text-ink-muted">Aún no hay corridas.</p>
        )}
        {(corridas.data?.items.length ?? 0) > 0 && (
          <ListadoModulo
            filtros={
              <BarraFiltros total={`${corridas.data?.items.length ?? 0} corridas recibidas`} />
            }
            pie={
              <PaginacionTabla
                total={corridas.data?.items.length ?? 0}
                offset={paginaCorridas.offset}
                limit={10}
                tamanos={[]}
                onChange={paginaCorridas.onChange}
              />
            }
          >
            {' '}
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Fecha</TableHead>
                  <TableHead>Estado</TableHead>
                  <TableHead>Actor</TableHead>
                  <TableHead>Detalle</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {paginaCorridas.visibles.map((c) => (
                  <TableRow key={c.id}>
                    <TableCell>{fechaCorridaAw(c.iniciadaEnUtc)}</TableCell>
                    <TableCell>
                      <EstadoCorridaBadge estado={c.estado} />
                    </TableCell>
                    <TableCell>{c.actor ?? '—'}</TableCell>
                    <TableCell>
                      <Link
                        to="/admin/datos-maestros/sincronizacion-aw/$id"
                        params={{ id: c.id }}
                        className="text-brand"
                      >
                        Ver detalle
                      </Link>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </ListadoModulo>
        )}
      </section>

      <section
        className="rounded-lg bg-surface-card p-4 shadow-card-flat"
        aria-label="Conflictos por resolver"
      >
        <h2 className="text-lg font-semibold">Conflictos por resolver</h2>
        {puedeClientes && puedeProductos && (
          <Tabs
            value={entidad}
            onValueChange={(v) => {
              setEntidad(v as EntidadConflictoAw);
              setPagina(0);
              setConfirmar(null);
            }}
          >
            <TabsList aria-label="Entidad">
              <TabsTrigger value="clientes">Clientes</TabsTrigger>
              <TabsTrigger value="productos-aw">Productos</TabsTrigger>
            </TabsList>
          </Tabs>
        )}
        <ListadoModulo
          tabs={
            <TabsEstado
              opciones={[
                { valor: 0, label: 'Pendientes' },
                { valor: 1, label: 'Resueltos' },
              ]}
              valor={estado === 'Todos' ? undefined : estado === 'Pendiente' ? 0 : 1}
              onChange={(v) => {
                setEstado(v === undefined ? 'Todos' : v === 0 ? 'Pendiente' : 'Resuelto');
                setPagina(0);
                setConfirmar(null);
              }}
              conteos={{ 0: pendientes.data?.length ?? 0, 1: resueltos.data?.length ?? 0 }}
            />
          }
          filtros={<BarraFiltros total={`${filas.length} conflictos`} />}
          pie={
            <PaginacionTabla
              total={filas.length}
              offset={pagina * POR_PAGINA}
              limit={POR_PAGINA}
              tamanos={[]}
              onChange={(offset) => setPagina(offset / POR_PAGINA)}
            />
          }
        >
          {lista.isError && (
            <p role="alert" className="py-3 text-danger-fg">
              No se pudieron cargar los conflictos.{' '}
              <Button
                variant="secondary"
                size="sm"
                onClick={() => {
                  void lista.refetch();
                }}
              >
                Reintentar
              </Button>
            </p>
          )}
          {!lista.isError && !lista.isLoading && filas.length === 0 && (
            <p className="py-3 text-ink-muted">
              {estado === 'Pendiente'
                ? 'No hay conflictos por resolver.'
                : 'No hay conflictos resueltos.'}
            </p>
          )}
          {filas.length > 0 && (
            <>
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Registro</TableHead>
                    <TableHead>Tipo</TableHead>
                    <TableHead>Causa</TableHead>
                    <TableHead>Dueño del campo</TableHead>
                    <TableHead>Acción</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {visibles.map((c) => (
                    <TableRow key={c.id}>
                      <TableCell className="font-mono">{c.referenciaExterna}</TableCell>
                      <TableCell>
                        <Badge variant={TIPO_CAMBIO_AW[c.tipo].variante}>
                          {TIPO_CAMBIO_AW[c.tipo].texto}
                        </Badge>
                      </TableCell>
                      <TableCell>
                        {c.causa}
                        {c.tipo === 'EditadoEnErp' && (
                          <span className="block text-xs text-ink-muted">{`ERP «${c.valorAntes ?? ''}» · A+W «${c.valorDespues ?? ''}»`}</span>
                        )}
                      </TableCell>
                      <TableCell>{c.dueno === 'AW' ? 'A+W' : 'ERP'}</TableCell>
                      <TableCell>
                        {c.estado === 'Pendiente' && c.tipo === 'RfcCompartido' && (
                          <Button
                            size="sm"
                            variant="secondary"
                            disabled={resolver.isPending}
                            onClick={() => setConfirmar(c)}
                          >
                            Mantener separados
                          </Button>
                        )}
                        {c.estado === 'Pendiente' && c.tipo === 'EditadoEnErp' && (
                          <div className="flex gap-2">
                            <Button
                              size="sm"
                              disabled={resolver.isPending}
                              onClick={() => {
                                setAccionProducto('AplicarAw');
                                setConfirmar(c);
                              }}
                            >
                              Aplicar A+W
                            </Button>
                            <Button
                              size="sm"
                              variant="secondary"
                              disabled={resolver.isPending}
                              onClick={() => {
                                setAccionProducto('MantenerErp');
                                setConfirmar(c);
                              }}
                            >
                              Mantener ERP
                            </Button>
                          </div>
                        )}
                        {c.estado === 'Resuelto' && (
                          <span className="text-ink-muted">
                            {c.resolucion === 'AplicarAw'
                              ? 'A+W aplicado'
                              : c.resolucion === 'MantenerErp'
                                ? 'ERP mantenido'
                                : 'Mantenidos separados'}
                          </span>
                        )}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </>
          )}
        </ListadoModulo>
        {confirmar && (
          <div
            role="note"
            className="mt-3 space-y-3 rounded-md bg-warning-note-bg p-3 text-warning-note-fg"
          >
            <p>
              {confirmar.tipo === 'EditadoEnErp'
                ? accionProducto === 'AplicarAw'
                  ? 'Se reemplazará el valor del ERP por el de A+W. Queda en la bitácora.'
                  : 'Se conserva el valor del ERP y el conflicto queda resuelto. Si A+W cambia ese valor, se volverá a señalar.'
                : 'Los dos clientes quedan separados. Esto no fusiona ni liga identidades.'}
            </p>
            <Button disabled={resolver.isPending} onClick={() => resolverConflicto(confirmar)}>
              Confirmar
            </Button>
            <Button variant="ghost" onClick={() => setConfirmar(null)}>
              Cancelar
            </Button>
          </div>
        )}
      </section>

      {puedeClientes && <RfcRepetidosSeccion />}

      <section
        className="rounded-lg bg-surface-card p-4 shadow-card-flat"
        aria-label="Matriz campo a dueño"
      >
        <h2 className="text-lg font-semibold">Matriz campo → dueño</h2>
        {matriz.isError && (
          <p role="alert" className="text-danger-fg">
            No se pudo cargar la matriz.
          </p>
        )}
        {matriz.data && (
          <>
            <p className="text-ink-muted">
              {`Versión ${matriz.data.version}`}
              {/propuesta/i.test(matriz.data.version)
                ? ' · pendiente de acordar con TI'
                : ' · acordada con TI'}
            </p>
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Entidad</TableHead>
                  <TableHead>Campo</TableHead>
                  <TableHead>Dueño</TableHead>
                  <TableHead>Si difiere</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {matriz.data.campos.map((m) => (
                  <TableRow key={`${m.entidad}-${m.campo}`}>
                    <TableCell>{ENTIDAD_MATRIZ[m.entidad] ?? m.entidad}</TableCell>
                    <TableCell>{m.campo}</TableCell>
                    <TableCell>
                      <Badge variant={m.dueno === 'AW' ? 'info' : 'neutral'}>
                        {m.dueno === 'AW' ? 'A+W' : 'ERP'}
                      </Badge>
                    </TableCell>
                    <TableCell>{m.siDifiere}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </>
        )}
      </section>
    </div>
  );
}
