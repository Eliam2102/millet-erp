import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { TabsEstado } from '@/components/erp/data/TabsEstado';
import { BarraFiltros } from '@/components/erp/data/BarraFiltros';
import { EstadoOrdenCompra, estadoOcToString } from '@/features/compras/ordenes/api/types';
import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableHead,
  TableCell,
} from '@/components/ui/table';
import { useMemo } from 'react';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { Button } from '@/components/ui/button';
import {
  EmptyState,
  ErrorState,
  TableSkeleton,
  EstadoBadge,
  DateTimeDisplay,
  PaginacionTabla,
} from '@/components/erp';
import { useProveedores, useUsuarios, mapById } from '@/features/catalogos/api';
import { esApiError } from '@/lib/api';
import { usePartidasAbiertas } from '@/features/compras/ordenes/api/usePartidasAbiertas';
import { useKpisPartidasAbiertas } from '@/features/compras/ordenes/api/useKpisPartidasAbiertas';
import { KpiCardsPartidasAbiertas } from '@/features/compras/ordenes/components/KpiCardsPartidasAbiertas';
import { DiasAtrasadosBadge } from '@/features/compras/ordenes/components/DiasAtrasadosBadge';
import { FiltrosPartidasAbiertas } from '@/features/compras/ordenes/components/FiltrosPartidasAbiertas';
import { HoverSubEstados } from '@/features/compras/ordenes/components/HoverSubEstados';
import { HistorialIncidenciasProveedor } from '@/features/compras/ordenes/components/HistorialIncidenciasProveedor';
import { Badge } from '@/components/ui/badge';
import type { PartidasAbiertasSearch } from '@/features/compras/ordenes/lib/partidas-abiertas-search-schema';
import { cn } from '@/lib/utils';
import { nombreUsuarioOGuion } from '@/lib/nombre-usuario';
import { PageHeader } from '@/components/erp/PageHeader';

const FROM = '/_app/compras/ordenes/partidas-abiertas' as const;

/**
 * <c>P9 — Partidas abiertas</c> (UF7-PR1, FOC10).
 *
 * <para>Vista crítica del negocio: KPI cards reactivas + tabla densa
 * de OCs con al menos una dimensión sub-estado abierta. Filtros sticky
 * lateral. Permiso requerido: <c>compras.ordenes.reportes-partidas-abiertas</c>.</para>
 *
 * <para>Performance target del doc: P95 query &lt; 500ms con 5k OCs
 * activas seed (backend indexado por <c>ix_oc_partidas_abiertas</c>).</para>
 */
export function PartidasAbiertas() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();

  const itemsQuery = usePartidasAbiertas(search);
  const kpisQuery = useKpisPartidasAbiertas({
    proveedorId: search.proveedorId,
    compradorTitularId: search.compradorTitularId,
    fechaDesde: search.fechaDesde,
    fechaHasta: search.fechaHasta,
  });

  // limit alto + includeInactivas para no perder proveedores
  // históricos en este reporte agregado.
  const proveedoresQuery = useProveedores({
    limit: 1000,
    includeInactivas: true,
  });
  const usuariosQuery = useUsuarios();
  const proveedoresMap = useMemo(
    () => mapById(proveedoresQuery.data?.items),
    [proveedoresQuery.data],
  );
  const usuariosMap = useMemo(() => mapById(usuariosQuery.data?.items), [usuariosQuery.data]);

  function setSearch(next: PartidasAbiertasSearch) {
    navigate({
      to: '/compras/ordenes/partidas-abiertas',
      search: next,
      replace: false,
    });
  }

  function resolverProveedor(id: string): string {
    const p = proveedoresMap.get(id);
    if (p == null) return id.slice(0, 8) + '…';
    return p.nombreComercial ?? p.razonSocial;
  }
  function resolverUsuario(id: string): string {
    return nombreUsuarioOGuion(usuariosMap.get(id)?.nombre);
  }

  return (
    <div className="flex min-h-0 flex-1 flex-col gap-5" data-component="partidas-abiertas">
      {/* Contenido principal */}
      <div className="space-y-4 min-w-0">
        <PageHeader
          titulo="Partidas abiertas"
          descripcion="Órdenes con recepción, facturación o pago pendientes, las más atrasadas primero."
        />

        <KpiCardsPartidasAbiertas
          search={search}
          kpis={kpisQuery.data}
          isLoading={kpisQuery.isLoading}
        />

        <ListadoModulo
          pie={
            <PaginacionTabla
              total={itemsQuery.data?.totalCount ?? 0}
              offset={(search.page - 1) * search.pageSize}
              limit={search.pageSize}
              onChange={(offset, limit) =>
                setSearch({ ...search, page: Math.floor(offset / limit) + 1, pageSize: limit })
              }
            />
          }
          tabs={
            <TabsEstado
              opciones={[
                EstadoOrdenCompra.Borrador,
                EstadoOrdenCompra.EnAutorizacionJefeCompras,
                EstadoOrdenCompra.EnAutorizacionDireccion,
                EstadoOrdenCompra.Autorizada,
              ].map((valor) => ({ valor, label: estadoOcToString(valor) }))}
              valor={search.estado}
              conteos={undefined}
              onChange={(estado) =>
                setSearch({
                  ...search,
                  estado: estado as PartidasAbiertasSearch['estado'],
                  page: 1,
                })
              }
            />
          }
          filtros={
            <BarraFiltros
              masFiltros={<FiltrosPartidasAbiertas search={search} onChange={setSearch} />}
              total={`${itemsQuery.data?.totalCount ?? 0} órdenes`}
            />
          }
        >
          <RenderTabla
            query={itemsQuery}
            resolverProveedor={resolverProveedor}
            resolverUsuario={resolverUsuario}
          />
        </ListadoModulo>

        {search.proveedorId && (
          <HistorialIncidenciasProveedor
            proveedorId={search.proveedorId}
            proveedorNombre={resolverProveedor(search.proveedorId)}
          />
        )}
      </div>
    </div>
  );
}

// ─── Tabla ────────────────────────────────────────────────────────

interface RenderTablaProps {
  query: ReturnType<typeof usePartidasAbiertas>;
  resolverProveedor: (id: string) => string;
  resolverUsuario: (id: string) => string;
}

function RenderTabla({ query, resolverProveedor, resolverUsuario }: RenderTablaProps) {
  if (query.isLoading) {
    return <TableSkeleton rows={8} />;
  }
  if (query.isError) {
    return (
      <ErrorState
        title="No se pudo cargar el reporte"
        problem={esApiError(query.error) ? query.error.problem : undefined}
        onRetry={() => {
          void query.refetch();
        }}
      />
    );
  }
  const data = query.data;
  if (data == null || data.items.length === 0) {
    return (
      <EmptyState
        title="No hay partidas abiertas con los filtros aplicados."
        description="Ajusta o limpia los filtros para consultar otras partidas."
      />
    );
  }

  return (
    <>
      <div className="overflow-x-auto rounded-md border bg-surface-card">
        <Table className="w-full min-w-full text-sm">
          <TableHeader className="bg-surface-muted/50 text-xs uppercase tracking-wide text-ink-muted">
            <TableRow>
              <TableHead className="px-3 py-2 text-left font-medium">Folio</TableHead>
              <TableHead className="px-3 py-2 text-left font-medium">Proveedor</TableHead>
              <TableHead className="px-3 py-2 text-left font-medium">Comprador</TableHead>
              <TableHead className="px-3 py-2 text-left font-medium">F. doc.</TableHead>
              <TableHead className="px-3 py-2 text-left font-medium">F. entrega</TableHead>
              <TableHead className="px-3 py-2 text-left font-medium">Atraso</TableHead>
              <TableHead className="px-3 py-2 text-left font-medium">Incidencias</TableHead>
              <TableHead className="px-3 py-2 text-left font-medium">Estado</TableHead>
              <TableHead className="px-3 py-2 text-left font-medium">
                Avance (recepción · facturación · pago)
              </TableHead>
              <TableHead className="px-3 py-2 text-right font-medium">Acción</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody className="[&_tr]:h-12">
            {data.items.map((p, i) => (
              <TableRow
                key={p.id}
                className={cn(i % 2 === 1 ? 'bg-surface-muted/20' : undefined)}
                data-oc={p.id}
              >
                <TableCell className="px-3 py-2 font-mono text-xs">
                  <Link
                    className="font-mono text-xs font-medium text-brand"
                    to="/compras/ordenes/$id"
                    params={{ id: p.id }}
                  >
                    {p.folio}
                  </Link>
                </TableCell>
                <TableCell className="px-3 py-2">{resolverProveedor(p.proveedorId)}</TableCell>
                <TableCell className="px-3 py-2 text-ink-muted">
                  {resolverUsuario(p.compradorTitularId)}
                </TableCell>
                <TableCell className="px-3 py-2">
                  <DateTimeDisplay value={p.fechaDocumento} />
                </TableCell>
                <TableCell className="px-3 py-2">
                  {p.fechaEntregaEsperada ? (
                    <DateTimeDisplay value={p.fechaEntregaEsperada} />
                  ) : (
                    <span className="text-ink-muted">—</span>
                  )}
                </TableCell>
                <TableCell className="px-3 py-2">
                  <DiasAtrasadosBadge diasAtrasados={p.diasAtrasados} />
                </TableCell>
                <TableCell className="px-3 py-2">
                  {p.incidenciasAbiertas > 0 ? (
                    <Badge variant={p.incidenciasVencidas > 0 ? 'danger' : 'warning'}>
                      {p.incidenciasVencidas > 0
                        ? `${p.incidenciasVencidas} vencida${p.incidenciasVencidas === 1 ? '' : 's'}`
                        : `${p.incidenciasAbiertas} abierta${p.incidenciasAbiertas === 1 ? '' : 's'}`}
                    </Badge>
                  ) : (
                    <span className="text-ink-muted">—</span>
                  )}
                </TableCell>
                <TableCell className="px-3 py-2">
                  <EstadoBadge tipo="orden-compra" estado={p.estado} />
                </TableCell>
                <TableCell className="px-3 py-2">
                  <HoverSubEstados
                    recepcion={p.subEstadoRecepcion}
                    facturacion={p.subEstadoFacturacion}
                    pago={p.subEstadoPago}
                  />
                </TableCell>
                <TableCell className="px-3 py-2 text-right">
                  <Button asChild size="sm" variant="ghost">
                    <Link to="/compras/ordenes/$id" params={{ id: p.id }} data-action="ver-detalle">
                      Ver
                    </Link>
                  </Button>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
    </>
  );
}
