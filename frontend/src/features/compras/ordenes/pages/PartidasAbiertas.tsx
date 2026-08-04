import { useMemo } from 'react';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { TrendingDown } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  EmptyState,
  ErrorState,
  TableSkeleton,
  EstadoBadge,
  DateTimeDisplay,
} from '@/components/erp';
import { useProveedores, useUsuarios, mapById } from '@/features/catalogos/api';
import { esApiError } from '@/lib/api';
import { usePartidasAbiertas } from '@/features/compras/ordenes/api/usePartidasAbiertas';
import { useKpisPartidasAbiertas } from '@/features/compras/ordenes/api/useKpisPartidasAbiertas';
import { KpiCardsPartidasAbiertas } from '@/features/compras/ordenes/components/KpiCardsPartidasAbiertas';
import { DiasAtrasadosBadge } from '@/features/compras/ordenes/components/DiasAtrasadosBadge';
import { FiltrosPartidasAbiertas } from '@/features/compras/ordenes/components/FiltrosPartidasAbiertas';
import { HoverSubEstados } from '@/features/compras/ordenes/components/HoverSubEstados';
import type { PartidasAbiertasSearch } from '@/features/compras/ordenes/lib/partidas-abiertas-search-schema';
import { cn } from '@/lib/utils';

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
  const usuariosMap = useMemo(
    () => mapById(usuariosQuery.data?.items),
    [usuariosQuery.data],
  );

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
    return usuariosMap.get(id)?.nombre ?? id.slice(0, 8) + '…';
  }

  return (
    <div
      className="grid gap-4 p-4 lg:grid-cols-[260px_1fr]"
      data-component="partidas-abiertas"
    >
      {/* Sidebar de filtros sticky */}
      <FiltrosPartidasAbiertas
        search={search}
        onChange={setSearch}
        className="lg:sticky lg:top-16 lg:self-start lg:max-h-[calc(100vh-5rem)] lg:overflow-y-auto"
      />

      {/* Contenido principal */}
      <div className="space-y-4 min-w-0">
        <header>
          <h1 className="flex items-center gap-2 text-2xl font-semibold tracking-tight">
            <TrendingDown className="h-6 w-6" />
            Partidas abiertas
          </h1>
          <p className="text-sm text-muted-foreground">
            OCs no terminales con al menos una dimensión sub-estado abierta.
            Orden FIFO por fecha de entrega esperada (atrasadas primero).
          </p>
        </header>

        <KpiCardsPartidasAbiertas
          kpis={kpisQuery.data}
          isLoading={kpisQuery.isLoading}
        />

        <RenderTabla
          query={itemsQuery}
          search={search}
          onChangeSearch={setSearch}
          resolverProveedor={resolverProveedor}
          resolverUsuario={resolverUsuario}
        />
      </div>
    </div>
  );
}

// ─── Tabla ────────────────────────────────────────────────────────

interface RenderTablaProps {
  query: ReturnType<typeof usePartidasAbiertas>;
  search: PartidasAbiertasSearch;
  onChangeSearch: (next: PartidasAbiertasSearch) => void;
  resolverProveedor: (id: string) => string;
  resolverUsuario: (id: string) => string;
}

function RenderTabla({
  query,
  search,
  onChangeSearch,
  resolverProveedor,
  resolverUsuario,
}: RenderTablaProps) {
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
        description="Prueba ajustando los filtros del sidebar o limpiando todos."
      />
    );
  }

  const totalPages = Math.max(1, Math.ceil(data.totalCount / data.pageSize));

  return (
    <>
      <div className="overflow-x-auto rounded-md border bg-card">
        <table className="w-full min-w-[1000px] text-sm">
          <thead className="bg-muted/50 text-xs uppercase tracking-wide text-muted-foreground">
            <tr>
              <th className="px-3 py-2 text-left font-medium">Folio</th>
              <th className="px-3 py-2 text-left font-medium">Proveedor</th>
              <th className="px-3 py-2 text-left font-medium">Comprador</th>
              <th className="px-3 py-2 text-left font-medium">F. doc.</th>
              <th className="px-3 py-2 text-left font-medium">F. entrega</th>
              <th className="px-3 py-2 text-left font-medium">Atraso</th>
              <th className="px-3 py-2 text-left font-medium">Estado</th>
              <th className="px-3 py-2 text-left font-medium">R/F/P</th>
              <th className="px-3 py-2 text-right font-medium">Acción</th>
            </tr>
          </thead>
          <tbody>
            {data.items.map((p, i) => (
              <tr
                key={p.id}
                className={cn(i % 2 === 1 ? 'bg-muted/20' : undefined)}
                data-oc={p.id}
              >
                <td className="px-3 py-2 font-mono text-xs">{p.folio}</td>
                <td className="px-3 py-2">{resolverProveedor(p.proveedorId)}</td>
                <td className="px-3 py-2 text-muted-foreground">
                  {resolverUsuario(p.compradorTitularId)}
                </td>
                <td className="px-3 py-2">
                  <DateTimeDisplay value={p.fechaDocumento} />
                </td>
                <td className="px-3 py-2">
                  {p.fechaEntregaEsperada ? (
                    <DateTimeDisplay value={p.fechaEntregaEsperada} />
                  ) : (
                    <span className="text-muted-foreground">—</span>
                  )}
                </td>
                <td className="px-3 py-2">
                  <DiasAtrasadosBadge diasAtrasados={p.diasAtrasados} />
                </td>
                <td className="px-3 py-2">
                  <EstadoBadge tipo="orden-compra" estado={p.estado} />
                </td>
                <td className="px-3 py-2">
                  <HoverSubEstados
                    recepcion={p.subEstadoRecepcion}
                    facturacion={p.subEstadoFacturacion}
                    pago={p.subEstadoPago}
                  />
                </td>
                <td className="px-3 py-2 text-right">
                  <Button asChild size="sm" variant="ghost">
                    <Link
                      to="/compras/ordenes/$id"
                      params={{ id: p.id }}
                      data-action="ver-detalle"
                    >
                      Ver
                    </Link>
                  </Button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* Paginación simple */}
      <div className="flex items-center justify-between text-sm text-muted-foreground">
        <span>
          Página {data.page} de {totalPages} · {data.totalCount.toLocaleString()} OCs
        </span>
        <div className="flex items-center gap-2">
          <Button
            size="sm"
            variant="outline"
            disabled={data.page <= 1}
            onClick={() =>
              onChangeSearch({ ...search, page: Math.max(1, data.page - 1) })
            }
          >
            Anterior
          </Button>
          <Button
            size="sm"
            variant="outline"
            disabled={data.page >= totalPages}
            onClick={() =>
              onChangeSearch({
                ...search,
                page: Math.min(totalPages, data.page + 1),
              })
            }
          >
            Siguiente
          </Button>
        </div>
      </div>
    </>
  );
}
