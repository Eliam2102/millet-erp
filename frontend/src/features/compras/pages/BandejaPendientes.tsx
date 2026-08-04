import { useMemo, useState } from 'react';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { Inbox } from 'lucide-react';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Button } from '@/components/ui/button';
import {
  EmptyState,
  ErrorState,
  TableSkeleton,
  EstadoBadge,
  NivelPendienteBadge,
  DateTimeDisplay,
  SortableHeader,
  compareItemsBy,
  type SortState,
} from '@/components/erp';
import { usePendientesAutorizacion } from '@/features/compras/api/usePendientesAutorizacion';
import { useDepartamentos, mapById } from '@/features/catalogos/api';
import {
  departamentoLabel,
  requisitanteLabel,
} from '@/features/compras/lib/nombres';
import { esApiError } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import {
  DEFAULT_PENDIENTES_SEARCH,
  type PendientesSearch,
} from '@/features/compras/lib/pendientes-search-schema';

const FROM = '/_app/compras/pendientes/' as const;
const SENTINEL_ALL = '__all__';

/**
 * <c>P2 Bandeja de pendientes de autorización</c> (doc 05 §5).
 *
 * <para>Misma estructura que P1 (bandeja general) pero filtrada
 * server-side por <c>estado=EnAutorizacion</c> (el endpoint
 * <c>/pendientes-autorizacion</c> es atajo). Filtro depto gateado
 * por <c>ver-todos-departamentos</c>; búsqueda por folio
 * client-side; paginación offset.</para>
 *
 * <para>El sub-item del sidebar "Pendientes de autorización" se gate
 * con <c>useHasAnyPermission(['autorizar-nivel1', 'autorizar-nivel2'])</c>;
 * sin alguno de los dos no tiene sentido entrar.</para>
 */
export function BandejaPendientes() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const verTodosDepartamentos = useHasPermission(
    PermisosCanonicos.ComprasRequisicionesVerTodosDepartamentos,
  );

  const query = usePendientesAutorizacion({
    departamentoId: search.departamentoId,
    nivelPendiente: search.nivelPendiente,
    offset: search.offset,
    limit: search.limit,
  });
  // Solo para poblar el dropdown de filtro por departamento (gateado por
  // ver-todos-departamentos). Los nombres de las filas vienen del backend.
  const departamentosQuery = useDepartamentos();

  const deptosMap = useMemo(
    () => mapById(departamentosQuery.data?.items),
    [departamentosQuery.data],
  );

  const itemsFiltrados = useMemo(() => {
    const items = query.data?.items ?? [];
    if (!search.q) return items;
    const needle = search.q.toLowerCase();
    return items.filter((r) => r.folio.toLowerCase().includes(needle));
  }, [query.data, search.q]);

  const [sort, setSort] =
    useState<SortState<'folio' | 'fechaSolicitud'> | null>(null);
  const itemsOrdenados = useMemo(() => {
    if (sort == null) return itemsFiltrados;
    return [...itemsFiltrados].sort((a, b) =>
      compareItemsBy(a, b, sort.key, sort.dir),
    );
  }, [itemsFiltrados, sort]);

  function setSearch(next: PendientesSearch) {
    navigate({
      to: '/compras/pendientes',
      search: next,
      replace: false,
    });
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h1 className="text-2xl font-semibold tracking-tight">
          Pendientes de autorización
        </h1>
      </div>

      <div
        className="flex flex-wrap items-center gap-2"
        role="search"
        aria-label="Filtros de bandeja de pendientes"
      >
        {verTodosDepartamentos && (
          <Select
            value={search.departamentoId ?? SENTINEL_ALL}
            onValueChange={(v) =>
              setSearch({
                ...search,
                departamentoId: v === SENTINEL_ALL ? undefined : v,
                offset: 0,
              })
            }
          >
            <SelectTrigger
              aria-label="Filtrar por departamento"
              className="h-9 w-auto min-w-48 gap-1 font-medium"
            >
              <SelectValue placeholder="Todos los departamentos" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={SENTINEL_ALL}>
                Todos los departamentos
              </SelectItem>
              {Array.from(deptosMap.values()).map((d) => (
                <SelectItem key={d.id} value={d.id}>
                  {d.clave} · {d.nombre}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        )}

        {/* Filtro por nivel pendiente (PR-A). No gateado: cualquier
            autorizador puede acotar a lo que le toca firmar. */}
        <Select
          value={
            search.nivelPendiente != null
              ? String(search.nivelPendiente)
              : SENTINEL_ALL
          }
          onValueChange={(v) =>
            setSearch({
              ...search,
              nivelPendiente: v === SENTINEL_ALL ? undefined : Number(v),
              offset: 0,
            })
          }
        >
          <SelectTrigger
            aria-label="Filtrar por nivel pendiente"
            className="h-9 w-auto min-w-40 gap-1 font-medium"
          >
            <SelectValue placeholder="Todos los niveles" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={SENTINEL_ALL}>Todos los niveles</SelectItem>
            <SelectItem value="1">Falta N1</SelectItem>
            <SelectItem value="2">Falta N2</SelectItem>
          </SelectContent>
        </Select>

        {(search.q || search.departamentoId || search.nivelPendiente) && (
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={() =>
              setSearch({ offset: 0, limit: search.limit ?? 50 })
            }
          >
            Limpiar filtros
          </Button>
        )}
      </div>

      {query.isLoading ? (
        <TableSkeleton
          rows={6}
          columns={[
            { width: 'w-32' },
            { width: 'w-24' },
            { width: 'w-48' },
            { width: 'w-40' },
            { width: 'w-24' },
            { width: 'w-20' },
            { width: 'w-12' },
          ]}
        />
      ) : query.isError ? (
        <ErrorState
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : (query.data?.total ?? 0) === 0 ? (
        <EmptyState
          icon={<Inbox className="h-10 w-10" />}
          title="No hay requisiciones pendientes de tu autorización."
          description="Cuando un capturador transmita una RQ, aparecerá aquí."
        />
      ) : itemsFiltrados.length === 0 && search.q ? (
        <EmptyState
          title={`Ningún folio coincide con "${search.q}".`}
          description="La búsqueda es sobre la página actual; cambia o limpia el filtro."
        />
      ) : (
        <div className="overflow-x-auto rounded-md border bg-card">
          {/* Tabla — desktop md+ */}
          <table className="hidden w-full min-w-[720px] text-sm md:table">
            <thead className="bg-muted/50 text-xs uppercase tracking-wide text-muted-foreground">
              <tr>
                <th className="px-3 py-2 text-left">
                  <SortableHeader
                    columnKey="folio"
                    label="Folio"
                    current={sort}
                    onSortChange={setSort}
                  />
                </th>
                <th className="px-3 py-2 text-left">
                  <SortableHeader
                    columnKey="fechaSolicitud"
                    label="Fecha"
                    current={sort}
                    onSortChange={setSort}
                  />
                </th>
                <th className="px-3 py-2 text-left font-medium">Requisitante</th>
                <th className="px-3 py-2 text-left font-medium">Departamento</th>
                <th className="px-3 py-2 text-left font-medium">Estado</th>
                <th className="px-3 py-2 text-left font-medium">Nivel</th>
                <th className="px-3 py-2 text-right font-medium">Ver</th>
              </tr>
            </thead>
            <tbody>
              {itemsOrdenados.map((r, i) => {
                const requisitante = requisitanteLabel(r);
                const deptoLabel = departamentoLabel(r);
                return (
                  <tr
                    key={r.id}
                    className={i % 2 === 1 ? 'bg-muted/20' : undefined}
                  >
                    <td className="px-3 py-2 font-mono">{r.folio}</td>
                    <td className="px-3 py-2">
                      <DateTimeDisplay value={r.fechaSolicitud} />
                    </td>
                    <td className="px-3 py-2">{requisitante}</td>
                    <td className="px-3 py-2">{deptoLabel}</td>
                    <td className="px-3 py-2">
                      <EstadoBadge tipo="requisicion" estado={r.estado} />
                    </td>
                    <td className="px-3 py-2">
                      {r.nivelPendiente != null && (
                        <NivelPendienteBadge nivel={r.nivelPendiente} />
                      )}
                    </td>
                    <td className="px-3 py-2 text-right">
                      <Button asChild variant="ghost" size="sm">
                        <Link
                          to="/compras/pendientes/$id"
                          params={{ id: r.id }}
                          // Pasamos la search actual de la bandeja P2
                          // para que la lista master del detalle abra
                          // con los mismos filtros y el botón "Cerrar"
                          // del detalle vuelva sin perderlos.
                          search={search}
                          state={{ pendientesSearch: search } as never}
                        >
                          Ver
                        </Link>
                      </Button>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>

          {/* Cards apilables — mobile sub-md (UF7-PR3 responsive) */}
          <ul className="divide-y md:hidden">
            {itemsOrdenados.map((r) => {
              const requisitante = requisitanteLabel(r);
              const deptoLabel = departamentoLabel(r);
              return (
                <li key={r.id} className="p-3 text-sm">
                  <div className="flex items-start justify-between gap-3">
                    <div className="min-w-0 flex-1 space-y-1">
                      <div className="flex flex-wrap items-center gap-2">
                        <span className="font-mono text-xs">{r.folio}</span>
                        <EstadoBadge tipo="requisicion" estado={r.estado} />
                        {r.nivelPendiente != null && (
                          <NivelPendienteBadge nivel={r.nivelPendiente} />
                        )}
                      </div>
                      <div className="text-xs text-muted-foreground">
                        <DateTimeDisplay value={r.fechaSolicitud} />
                      </div>
                      <div className="truncate">
                        <span className="font-medium">{requisitante}</span>
                        <span className="text-muted-foreground"> · {deptoLabel}</span>
                      </div>
                    </div>
                    <Button asChild variant="outline" size="sm" className="shrink-0">
                      <Link
                        to="/compras/pendientes/$id"
                        params={{ id: r.id }}
                        search={search}
                        state={{ pendientesSearch: search } as never}
                      >
                        Ver
                      </Link>
                    </Button>
                  </div>
                </li>
              );
            })}
          </ul>

          <Paginacion
            total={query.data?.total ?? 0}
            offset={search.offset ?? 0}
            limit={search.limit ?? 50}
            onChange={(offset, limit) =>
              setSearch({ ...search, offset, limit })
            }
          />
        </div>
      )}
    </div>
  );
}

interface PaginacionProps {
  total: number;
  offset: number;
  limit: number;
  onChange: (offset: number, limit: number) => void;
}

function Paginacion({ total, offset, limit, onChange }: PaginacionProps) {
  const desde = total === 0 ? 0 : offset + 1;
  const hasta = Math.min(offset + limit, total);
  const tieneAnterior = offset > 0;
  const tieneSiguiente = offset + limit < total;

  return (
    <div className="flex flex-wrap items-center justify-between gap-3 border-t bg-muted/30 px-3 py-2 text-xs text-muted-foreground">
      <span>
        Mostrando {desde}–{hasta} de {total}
      </span>
      <div className="flex items-center gap-2">
        <label className="flex items-center gap-1.5">
          Tamaño página:
          <select
            value={limit}
            onChange={(e) => onChange(0, Number(e.target.value))}
            className="rounded border bg-background px-1.5 py-0.5 text-xs"
            aria-label="Tamaño de página"
          >
            <option value={50}>50</option>
            <option value={100}>100</option>
            <option value={200}>200</option>
          </select>
        </label>
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={!tieneAnterior}
          onClick={() => onChange(Math.max(0, offset - limit), limit)}
        >
          Anterior
        </Button>
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={!tieneSiguiente}
          onClick={() => onChange(offset + limit, limit)}
        >
          Siguiente
        </Button>
      </div>
    </div>
  );
}

// `DEFAULT_PENDIENTES_SEARCH` se exporta desde el schema; lo
// re-exportamos aquí para que la ruta lo use como default si llega
// con search params faltantes (TanStack Router no auto-aplica los
// defaults de Zod si la URL no los menciona explícitamente).
export { DEFAULT_PENDIENTES_SEARCH };
