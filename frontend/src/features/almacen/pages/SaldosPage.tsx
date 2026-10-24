import { textoMedida } from '@/features/almacen/lib/medida-hoja';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { PageHeader } from '@/components/erp/PageHeader';
import { useMemo, useState } from 'react';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { Checkbox } from '@/components/ui/checkbox';
import {
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
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
  BarraFiltros,
  ErrorState,
  PaginacionTabla,
  SortableHeader,
  TableSkeleton,
  compareItemsBy,
  type SortState,
} from '@/components/erp';
import { mapById } from '@/features/catalogos/api';
import { useSaldos, useSaldosTotales } from '@/features/almacen/api/useSaldosCierreReportes';
import { useSubAlmacenes } from '@/features/almacen/api/useAlmacenes';
import type { SaldoListItem } from '@/features/almacen/api/types';
import { esApiError } from '@/lib/api';
import type { SaldosSearch } from '@/features/almacen/lib/saldos-search-schema';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { formatCantidad } from '@/lib/format-cantidad';
import { offsetLimitAPagina, paginaAOffsetLimit } from '@/components/erp/data/paginacion';

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
    ...paginaAOffsetLimit(search),
  });
  // Totales del servidor sobre TODO el conjunto filtrado (no solo la página).
  const totalesQuery = useSaldosTotales({
    subAlmacenId: search.subAlmacenId,
    articuloId: search.articuloId,
    soloConStock: search.soloConStock,
  });
  const totales = totalesQuery.data;

  function actualizarSearch(parcial: Partial<SaldosSearch>) {
    // Cambiar un filtro regresa a la página 1; la paginación pasa su propia página.
    navigate({ to: '/almacen/saldos', search: { ...search, page: undefined, ...parcial } });
  }

  const items = useMemo(() => query.data?.items ?? [], [query.data]);

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
    <div className="space-y-5">
      <PageHeader
        titulo="Saldos"
        descripcion="Existencias materializadas por sub-almacén y artículo, con costo promedio ponderado y valor de inventario."
      />

      <ListadoModulo
        filtros={
          <FiltrosToolbar
            search={search}
            subAlmacenes={subAlmacenesQuery.data?.items ?? []}
            articuloInitialLabel={articuloFiltradoLabel}
            onChange={actualizarSearch}
          />
        }
        pie={
          <PaginacionTabla
            total={query.data?.total ?? 0}
            offset={query.data?.offset ?? 0}
            limit={query.data?.limit ?? paginaAOffsetLimit(search).limit}
            onChange={(offset, limit) => actualizarSearch(offsetLimitAPagina(offset, limit))}
          />
        }
      >
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
              { width: 'w-40' },
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
            <div className="overflow-hidden rounded-lg border border-line-row bg-surface-card">
              <TablaSaldos items={items} subAlmacenesMap={subAlmacenesMap} />
            </div>
            {totales && (
              <div
                className="flex justify-end gap-6 rounded-md border border-line-row bg-surface-subtle px-4 py-2 text-sm"
                aria-label="Totales del conjunto filtrado"
              >
                {totales.cantidadTotal != null && (
                  <div>
                    <span className="text-ink-muted">Cantidad total: </span>
                    <span className="font-semibold tabular-nums">
                      {formatCantidad(totales.cantidadTotal)}
                      {totales.unidadMedida ? ` ${totales.unidadMedida}` : ''}
                    </span>
                  </div>
                )}
                <div>
                  <span className="text-ink-muted">Valor inventario: </span>
                  <MoneyDisplay amount={totales.valorInventarioMxn} currency="MXN" strong />
                </div>
              </div>
            )}
          </>
        )}
      </ListadoModulo>
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
  const algun = search.subAlmacenId || search.articuloId || search.soloConStock;

  return (
    <BarraFiltros
      aria-label="Filtros de saldos"
      filtros={
        <>
          <Select
            value={search.subAlmacenId ?? SENTINEL_ALL}
            onValueChange={(v) => onChange({ subAlmacenId: v === SENTINEL_ALL ? undefined : v })}
          >
            <SelectTrigger aria-label="Filtrar por sub-almacén" className="h-ctl-md w-48 text-xs">
              <SelectValue placeholder="Todos los sub-almacenes" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={SENTINEL_ALL}>Todos los sub-almacenes</SelectItem>
              {subAlmacenes.map((s) => (
                <SelectItem key={s.id} value={s.id}>
                  {s.clave} · {s.nombre}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <ArticuloSelector
            value={search.articuloId ?? null}
            onChange={(id) => onChange({ articuloId: id ?? undefined })}
            initialLabel={articuloInitialLabel}
            placeholder="Todos los artículos"
            className="h-ctl-md w-64 text-xs"
          />
        </>
      }
      masFiltros={
        <div className="flex items-center gap-2">
          <Checkbox
            id="solo-con-stock"
            checked={search.soloConStock ?? false}
            onCheckedChange={(checked) => onChange({ soloConStock: checked === true || undefined })}
          />
          <label htmlFor="solo-con-stock" className="text-sm">
            Solo con stock
          </label>
        </div>
      }
      masFiltrosActivos={search.soloConStock ? 1 : 0}
      onLimpiar={
        algun
          ? () =>
              onChange({
                subAlmacenId: undefined,
                articuloId: undefined,
                soloConStock: undefined,
              })
          : undefined
      }
    />
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
        subAlmacen: subAlmacenesMap.get(s.subAlmacenId)?.clave ?? s.subAlmacenId,
      })),
    [items, subAlmacenesMap],
  );

  const ordenados = useMemo(() => {
    if (sort == null) return enriquecidos;
    return [...enriquecidos].sort((a, b) => compareItemsBy(a, b, sort.key, sort.dir));
  }, [enriquecidos, sort]);

  return (
    <TableContainer>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>
              <SortableHeader
                columnKey="subAlmacen"
                label="Sub-almacén"
                current={sort}
                onSortChange={setSort}
              />
            </TableHead>
            <TableHead className="w-full">
              <SortableHeader
                columnKey="articuloId"
                label="Artículo"
                current={sort}
                onSortChange={setSort}
              />
            </TableHead>
            <TableHead className="whitespace-nowrap">Medida</TableHead>
            <TableHead className="text-right">
              <SortableHeader
                columnKey="cantidad"
                label="Cantidad"
                current={sort}
                onSortChange={setSort}
              />
            </TableHead>
            <TableHead className="text-right">
              <SortableHeader
                columnKey="cantidadDisponible"
                label="Disponible"
                current={sort}
                onSortChange={setSort}
              />
            </TableHead>
            <TableHead className="hidden text-right xl:table-cell">
              <SortableHeader
                columnKey="costoPromedioMxn"
                label="CPP"
                current={sort}
                onSortChange={setSort}
              />
            </TableHead>
            <TableHead className="text-right">
              <SortableHeader
                columnKey="valorInventarioMxn"
                label="Valor inventario"
                current={sort}
                onSortChange={setSort}
              />
            </TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {ordenados.map((s) => (
            <TableRow key={`${s.subAlmacenId}-${s.articuloId}-${s.productoAwVarianteId ?? ''}`}>
              <TableCell className="font-mono text-xs">{s.subAlmacen}</TableCell>
              <TableCell className="max-w-0 truncate text-xs">
                {s.articuloClave ? (
                  <>
                    <span className="font-mono text-ink-muted">{s.articuloClave}</span>
                    {s.articuloDescripcion ? ` · ${s.articuloDescripcion}` : ''}
                  </>
                ) : (
                  <span className="font-mono">{s.articuloId}</span>
                )}
              </TableCell>
              <TableCell className="whitespace-nowrap text-xs tabular-nums">
                {textoMedida(s, s.cantidad) ?? <span className="text-ink-muted">—</span>}
              </TableCell>
              <TableCell className="text-right tabular-nums">
                {formatCantidad(s.cantidad)}
              </TableCell>
              <TableCell className="text-right tabular-nums">
                {formatCantidad(s.cantidadDisponible)}
              </TableCell>
              <TableCell className="hidden text-right tabular-nums xl:table-cell">
                <MoneyDisplay amount={s.costoPromedioMxn} currency="MXN" strong />
              </TableCell>
              <TableCell className="text-right tabular-nums">
                <MoneyDisplay amount={s.valorInventarioMxn} currency="MXN" strong />
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </TableContainer>
  );
}
