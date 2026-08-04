import { useMemo, useState } from 'react';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { Button } from '@/components/ui/button';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  ArticuloSelector,
  EmptyState,
  ErrorState,
  SortableHeader,
  TableSkeleton,
  compareItemsBy,
  type SortState,
} from '@/components/erp';
import { mapById } from '@/features/catalogos/api';
import { useSaldos } from '@/features/almacen/api/useSaldosCierreReportes';
import { useSubAlmacenes } from '@/features/almacen/api/useAlmacenes';
import type { SaldoListItem } from '@/features/almacen/api/types';
import { esApiError } from '@/lib/api';
import type { SaldosSearch } from '@/features/almacen/lib/saldos-search-schema';

const FROM = '/_app/almacen/saldos' as const;
const SENTINEL_ALL = '__all__';

type SortKey =
  | 'subAlmacen'
  | 'articuloId'
  | 'cantidad'
  | 'cantidadDisponible'
  | 'costoPromedioMxn'
  | 'valorInventarioMxn';

/**
 * <c>P11 — Saldos materializados</c> (doc 07 §FE-F6-PR1). Lista de
 * existencias por sub-almacén + artículo, con costo promedio
 * ponderado y valor de inventario. Filtros: sub-almacén, artículo,
 * "solo con stock". Gateada por <c>almacen.almacenes.leer</c>.
 */
export function SaldosPage() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();

  const subAlmacenesQuery = useSubAlmacenes({ limit: 500 });
  const subAlmacenesMap = useMemo(
    () => mapById(subAlmacenesQuery.data?.items),
    [subAlmacenesQuery.data],
  );

  const query = useSaldos({
    subAlmacenId: search.subAlmacenId,
    articuloId: search.articuloId,
    soloConStock: search.soloConStock,
    limit: 500,
  });

  function actualizarSearch(parcial: Partial<SaldosSearch>) {
    navigate({ to: '/almacen/saldos', search: { ...search, ...parcial } });
  }

  const items = useMemo(() => query.data?.items ?? [], [query.data]);
  const totales = useMemo(() => {
    const totalCantidad = items.reduce((a, s) => a + s.cantidad, 0);
    const totalValor = items.reduce((a, s) => a + s.valorInventarioMxn, 0);
    return { totalCantidad, totalValor };
  }, [items]);

  // Etiqueta del artículo filtrado para el trigger del selector tras un reload
  // con `articuloId` en la URL: se deriva de los saldos ya cargados (que vienen
  // enriquecidos con clave·nombre). Si la fila no está en la página, el selector
  // cae al id en el trigger hasta re-buscar.
  const articuloFiltradoLabel = useMemo(() => {
    if (!search.articuloId) return undefined;
    const m = items.find((i) => i.articuloId === search.articuloId);
    if (!m?.articuloClave) return undefined;
    return m.articuloDescripcion
      ? `${m.articuloClave} · ${m.articuloDescripcion}`
      : m.articuloClave;
  }, [items, search.articuloId]);

  return (
    <div className="space-y-4">
      <header>
        <h1 className="text-2xl font-semibold tracking-tight">Saldos</h1>
        <p className="text-sm text-muted-foreground">
          Existencias materializadas por sub-almacén y artículo, con costo
          promedio ponderado y valor de inventario.
        </p>
      </header>

      <FiltrosToolbar
        search={search}
        subAlmacenes={subAlmacenesQuery.data?.items ?? []}
        articuloInitialLabel={articuloFiltradoLabel}
        onChange={actualizarSearch}
      />

      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar los saldos"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={10}
          columns={[
            { width: 'w-40' },
            { width: 'w-64' },
            { width: 'w-24' },
            { width: 'w-24' },
            { width: 'w-24' },
            { width: 'w-32' },
            { width: 'w-32' },
          ]}
        />
      ) : items.length === 0 ? (
        <EmptyState
          title="Sin saldos"
          description={
            search.subAlmacenId || search.articuloId || search.soloConStock
              ? 'No hay saldos que coincidan con los filtros.'
              : 'Aún no se han registrado movimientos que generen saldos.'
          }
        />
      ) : (
        <>
          <TablaSaldos items={items} subAlmacenesMap={subAlmacenesMap} />
          <div className="flex justify-end gap-6 rounded-md border bg-muted/30 px-4 py-2 text-sm">
            <div>
              <span className="text-muted-foreground">
                Cantidad total:{' '}
              </span>
              <span className="font-mono font-semibold">
                {totales.totalCantidad.toLocaleString('es-MX', {
                  minimumFractionDigits: 2,
                  maximumFractionDigits: 4,
                })}
              </span>
            </div>
            <div>
              <span className="text-muted-foreground">
                Valor inventario:{' '}
              </span>
              <span className="font-mono font-semibold">
                {new Intl.NumberFormat('es-MX', {
                  style: 'currency',
                  currency: 'MXN',
                  minimumFractionDigits: 2,
                }).format(totales.totalValor)}
              </span>
            </div>
          </div>
        </>
      )}
    </div>
  );
}

interface FiltrosToolbarProps {
  search: SaldosSearch;
  subAlmacenes: readonly { id: string; clave: string; nombre: string }[];
  /** Etiqueta clave·nombre del artículo filtrado (reload con id en la URL). */
  articuloInitialLabel?: string;
  onChange: (parcial: Partial<SaldosSearch>) => void;
}

function FiltrosToolbar({
  search,
  subAlmacenes,
  articuloInitialLabel,
  onChange,
}: FiltrosToolbarProps) {
  const algun =
    search.subAlmacenId || search.articuloId || search.soloConStock;

  return (
    <div className="flex flex-wrap items-end gap-3">
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Sub-almacén</label>
        <Select
          value={search.subAlmacenId ?? SENTINEL_ALL}
          onValueChange={(v) =>
            onChange({ subAlmacenId: v === SENTINEL_ALL ? undefined : v })
          }
        >
          <SelectTrigger aria-label="Filtrar por sub-almacén" className="w-56">
            <SelectValue placeholder="Todos" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
            {subAlmacenes.map((s) => (
              <SelectItem key={s.id} value={s.id}>
                {s.clave} · {s.nombre}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Artículo</label>
        <ArticuloSelector
          value={search.articuloId ?? null}
          onChange={(id) => onChange({ articuloId: id ?? undefined })}
          initialLabel={articuloInitialLabel}
          placeholder="Todos"
          className="w-72"
        />
      </div>
      <div className="flex items-center gap-2 pb-1">
        <input
          id="solo-con-stock"
          type="checkbox"
          checked={search.soloConStock ?? false}
          onChange={(e) =>
            onChange({ soloConStock: e.target.checked || undefined })
          }
          className="h-4 w-4"
        />
        <label htmlFor="solo-con-stock" className="text-sm">
          Solo con stock
        </label>
      </div>
      {algun && (
        <Button
          variant="ghost"
          onClick={() =>
            onChange({
              subAlmacenId: undefined,
              articuloId: undefined,
              soloConStock: undefined,
            })
          }
        >
          Limpiar filtros
        </Button>
      )}
    </div>
  );
}

function TablaSaldos({
  items,
  subAlmacenesMap,
}: {
  items: readonly SaldoListItem[];
  subAlmacenesMap: Map<string, { id: string; clave: string; nombre: string }>;
}) {
  const [sort, setSort] = useState<SortState<SortKey> | null>(null);

  const enriquecidos = useMemo(
    () =>
      items.map((s) => ({
        ...s,
        subAlmacen:
          subAlmacenesMap.get(s.subAlmacenId)?.clave ?? s.subAlmacenId,
      })),
    [items, subAlmacenesMap],
  );

  const ordenados = useMemo(() => {
    if (sort == null) return enriquecidos;
    return [...enriquecidos].sort((a, b) =>
      compareItemsBy(a, b, sort.key, sort.dir),
    );
  }, [enriquecidos, sort]);

  return (
    <div className="overflow-x-auto rounded-md border">
      <table className="w-full text-sm">
        <thead className="bg-muted/50">
          <tr>
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="subAlmacen"
                label="Sub-almacén"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="articuloId"
                label="Artículo"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-right">
              <SortableHeader
                columnKey="cantidad"
                label="Cantidad"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-right">
              <SortableHeader
                columnKey="cantidadDisponible"
                label="Disponible"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-right">
              <SortableHeader
                columnKey="costoPromedioMxn"
                label="CPP"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-right">
              <SortableHeader
                columnKey="valorInventarioMxn"
                label="Valor inventario"
                current={sort}
                onSortChange={setSort}
              />
            </th>
          </tr>
        </thead>
        <tbody>
          {ordenados.map((s) => (
            <tr
              key={`${s.subAlmacenId}-${s.articuloId}`}
              className="border-t"
            >
              <td className="px-3 py-2 font-mono text-xs">{s.subAlmacen}</td>
              <td className="px-3 py-2 text-xs">
                {s.articuloClave ? (
                  <>
                    <span className="font-mono text-muted-foreground">
                      {s.articuloClave}
                    </span>
                    {s.articuloDescripcion ? ` · ${s.articuloDescripcion}` : ''}
                  </>
                ) : (
                  <span className="font-mono">{s.articuloId}</span>
                )}
              </td>
              <td className="px-3 py-2 text-right font-mono">
                {s.cantidad.toLocaleString('es-MX', {
                  minimumFractionDigits: 2,
                  maximumFractionDigits: 4,
                })}
              </td>
              <td className="px-3 py-2 text-right font-mono">
                {s.cantidadDisponible.toLocaleString('es-MX', {
                  minimumFractionDigits: 2,
                  maximumFractionDigits: 4,
                })}
              </td>
              <td className="px-3 py-2 text-right font-mono">
                {formatearMonto(s.costoPromedioMxn)}
              </td>
              <td className="px-3 py-2 text-right font-mono">
                {formatearMonto(s.valorInventarioMxn)}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function formatearMonto(v: number): string {
  return new Intl.NumberFormat('es-MX', {
    style: 'currency',
    currency: 'MXN',
    minimumFractionDigits: 2,
  }).format(v);
}
