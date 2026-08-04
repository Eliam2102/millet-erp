import { useMemo, useState } from 'react';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { Inbox, Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  EmptyState,
  ErrorState,
  TableSkeleton,
  EstadoBadge,
  DateTimeDisplay,
  SortableHeader,
  compareItemsBy,
  type SortState,
} from '@/components/erp';
import { useRequisiciones } from '@/features/compras/api';
import { OrigenBadge } from '@/features/compras/components/OrigenBadge';
import {
  departamentoLabel,
  requisitanteLabel,
} from '@/features/compras/lib/nombres';
import { esApiError } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { FiltrosBandeja } from '@/features/compras/components/FiltrosBandeja';
import { useNuevaRequisicion } from '@/features/compras/components/nueva-requisicion-context';
import type { BandejaSearch } from '@/features/compras/lib/bandeja-search-schema';

// El path "from" para useSearch/useNavigate es el `id` interno de la
// ruta en el routeTree (incluye el layout `_app`), NO el path público
// que se usa en `<Link to>`. Confusing pero es el shape de TanStack
// Router file-based.
const BANDEJA_FROM = '/_app/compras/requisiciones/' as const;

/**
 * <c>P1 Bandeja general de Requisiciones</c> (doc 05 §5).
 *
 * <para>Lista paginada con filtros + búsqueda por folio (client-side
 * dentro de la página actual mientras <c>GlobalSearch</c> backend no
 * exista, doc 05 §13.4) + estados loading/empty/error.</para>
 *
 * <para>Los filtros viven en <c>search params</c> de la URL y se
 * preservan al volver del detalle (doc 05 §13.9, breadcrumb
 * "Requisiciones" del detalle hace <c>useSearch</c> y manda los
 * mismos params al volver).</para>
 *
 * <para>Los nombres de requisitante y departamento vienen resueltos del
 * backend (ADR-0042: <c>requisitanteNombre</c>, <c>departamentoNombre</c>,
 * <c>departamentoClave</c>); el front ya no lee los catálogos completos.
 * Fallback al id si el backend no resolvió la clave.</para>
 */
export function BandejaRequisiciones() {
  const search = useSearch({ from: BANDEJA_FROM });
  const nuevaRequisicion = useNuevaRequisicion();
  // useNavigate sin `from`: con `from` el typing pide ParamsReducerFn
  // (function form), confuso para escribir. Usando `to` explícito en
  // setSearch podemos pasar el objeto search directo.
  const navigate = useNavigate();

  const canCrear = useHasPermission(PermisosCanonicos.ComprasRequisicionesCrear);

  const requisicionesQuery = useRequisiciones({
    estado: search.estado,
    departamentoId: search.departamentoId,
    requisitanteId: search.requisitanteId,
    offset: search.offset,
    limit: search.limit,
  });

  // Búsqueda por folio: client-side sobre la página actual.
  const itemsFiltrados = useMemo(() => {
    const items = requisicionesQuery.data?.items ?? [];
    if (!search.q) return items;
    const needle = search.q.toLowerCase();
    return items.filter((r) => r.folio.toLowerCase().includes(needle));
  }, [requisicionesQuery.data, search.q]);

  function setSearch(next: BandejaSearch) {
    // Navegación explícita a la propia ruta para que TanStack Router
    // valide `search` contra el shape de la ruta destino (no el
    // ParamsReducerFn que pide `from`).
    navigate({
      to: '/compras/requisiciones',
      search: next,
      replace: false,
    });
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h1 className="text-2xl font-semibold tracking-tight">
          Bandeja de requisiciones
        </h1>
        {canCrear && (
          <Button onClick={() => nuevaRequisicion.abrir()}>
            <Plus className="mr-2 h-4 w-4" />
            Nueva requisición
          </Button>
        )}
      </div>

      <FiltrosBandeja search={search} onChange={setSearch} />

      <RenderTabla
        query={requisicionesQuery}
        itemsFiltrados={itemsFiltrados}
        canCrear={canCrear}
        search={search}
        onChangeSearch={setSearch}
      />
    </div>
  );
}

interface RenderTablaProps {
  query: ReturnType<typeof useRequisiciones>;
  itemsFiltrados: ReturnType<typeof useRequisiciones>['data'] extends
    | { items: infer I }
    | undefined
    ? I
    : never;
  canCrear: boolean;
  search: BandejaSearch;
  onChangeSearch: (next: BandejaSearch) => void;
}

type SortKey = 'folio' | 'fechaSolicitud' | 'estado';

function RenderTabla({
  query,
  itemsFiltrados,
  canCrear,
  search,
  onChangeSearch,
}: RenderTablaProps) {
  const [sort, setSort] = useState<SortState<SortKey> | null>(null);
  const nuevaRequisicion = useNuevaRequisicion();

  const itemsOrdenados = useMemo(() => {
    if (sort == null) return itemsFiltrados;
    return [...itemsFiltrados].sort((a, b) =>
      compareItemsBy(a, b, sort.key, sort.dir),
    );
  }, [itemsFiltrados, sort]);

  if (query.isLoading) {
    return (
      <TableSkeleton
        rows={8}
        columns={[
          { width: 'w-32' },
          { width: 'w-24' },
          { width: 'w-48' },
          { width: 'w-40' },
          { width: 'w-24' },
          { width: 'w-12' },
        ]}
      />
    );
  }

  if (query.isError) {
    const problem = esApiError(query.error) ? query.error.problem : undefined;
    return <ErrorState problem={problem} onRetry={() => query.refetch()} />;
  }

  const total = query.data?.total ?? 0;

  if (total === 0) {
    return (
      <EmptyState
        icon={<Inbox className="h-10 w-10" />}
        title="Aún no tienes requisiciones."
        description={
          canCrear
            ? 'Crea la primera para empezar.'
            : 'Cuando se creen, aparecerán aquí.'
        }
        action={
          canCrear ? (
            <Button onClick={() => nuevaRequisicion.abrir()}>
              <Plus className="mr-2 h-4 w-4" />
              Nueva requisición
            </Button>
          ) : undefined
        }
      />
    );
  }

  if (itemsFiltrados.length === 0 && search.q) {
    return (
      <EmptyState
        title={`Ningún folio coincide con "${search.q}".`}
        description="La búsqueda es sobre la página actual; cambia o limpia el filtro para volver a ver los resultados."
      />
    );
  }

  return (
    <div className="overflow-x-auto rounded-md border bg-card">
      <table className="w-full min-w-[720px] text-sm">
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
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="estado"
                label="Estado"
                current={sort}
                onSortChange={setSort}
              />
            </th>
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
                  <div className="flex flex-wrap items-center gap-1.5">
                    <EstadoBadge
                      tipo="requisicion"
                      estado={r.estado}
                      situacion={r.situacionSurtido}
                    />
                    <OrigenBadge origen={r.origen} />
                  </div>
                </td>
                <td className="px-3 py-2 text-right">
                  <Button asChild variant="ghost" size="sm">
                    <Link
                      to="/compras/requisiciones/$id"
                      params={{ id: r.id }}
                      // Pasamos el search actual como `state` del Link
                      // para que el detalle preserve los filtros en el
                      // breadcrumb / CTA "Volver a bandeja". Browser
                      // back ya los preserva via history; state cubre
                      // los CTAs explícitos.
                      state={{ bandejaSearch: search } as never}
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

      <Paginacion
        total={total}
        offset={search.offset ?? 0}
        limit={search.limit ?? 50}
        onChange={(offset, limit) =>
          onChangeSearch({ ...search, offset, limit })
        }
      />
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
