import { useMemo, useState } from 'react';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  EmptyState,
  ErrorState,
  SortableHeader,
  TableSkeleton,
  clickableRowProps,
  compareItemsBy,
  type SortState,
} from '@/components/erp';
import { mapById } from '@/features/catalogos/api';
import { useRecepciones } from '@/features/almacen/api/useRecepciones';
import { useSubAlmacenes } from '@/features/almacen/api/useAlmacenes';
import {
  EstadoMovimiento,
  EstadoMovimientoLabels,
  type RecepcionListItem,
} from '@/features/almacen/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { NuevaRecepcionSheet } from '@/features/almacen/components/NuevaRecepcionSheet';
import type { RecepcionesSearch } from '@/features/almacen/lib/recepciones-search-schema';
import { cn } from '@/lib/utils';

const FROM = '/_app/almacen/recepciones/' as const;
const SENTINEL_ALL = '__all__';

type SortKey =
  | 'folio'
  | 'fechaMovimiento'
  | 'subAlmacen'
  | 'ordenCompraId'
  | 'montoTotalMxn'
  | 'estado';

/**
 * <c>P1 — Bandeja de recepciones</c> (doc 07 §FE-F2-PR1). Tabla con
 * filtros (estado, sub-almacén, OC, rango de fechas, búsqueda por
 * folio) y link a detalle por fila. Botón "Nueva recepción" abre el
 * sheet (próximo commit del PR) si el usuario tiene
 * <c>almacen.entradas.registrar</c>; oculto si no.
 *
 * <para>Gateada por <c>almacen.entradas.leer</c> a nivel ruta.</para>
 */
export function RecepcionesPage() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const puedeRegistrar = useHasPermission(
    PermisosCanonicos.AlmacenEntradasRegistrar,
  );
  const [sheetAbierto, setSheetAbierto] = useState(false);

  const subAlmacenesQuery = useSubAlmacenes({ limit: 500 });
  const subAlmacenesMap = useMemo(
    () => mapById(subAlmacenesQuery.data?.items),
    [subAlmacenesQuery.data],
  );

  const query = useRecepciones({
    estado: search.estado,
    subAlmacenId: search.subAlmacenId,
    ordenCompraId: search.ordenCompraId,
    desde: search.desde,
    hasta: search.hasta,
    limit: 200,
  });

  function actualizarSearch(parcial: Partial<RecepcionesSearch>) {
    navigate({
      to: '/almacen/recepciones',
      search: { ...search, ...parcial },
    });
  }

  // Búsqueda por folio: client-side sobre la página actual
  // (alineado al patrón de BandejaRequisiciones de Compras).
  const itemsFiltrados = useMemo(() => {
    const items = query.data?.items ?? [];
    if (!search.q) return items;
    const needle = search.q.toLowerCase();
    return items.filter((r) => r.folio.toLowerCase().includes(needle));
  }, [query.data, search.q]);

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            Recepciones
          </h1>
          <p className="text-sm text-muted-foreground">
            Bandeja de entradas. Variante A (con factura) para insumos;
            Variante B (con packing list) para materiales directos.
          </p>
        </div>
        {puedeRegistrar && (
          <Button onClick={() => setSheetAbierto(true)}>
            <Plus className="mr-2 h-4 w-4" />
            Nueva recepción
          </Button>
        )}
      </div>

      <FiltrosToolbar
        search={search}
        subAlmacenes={subAlmacenesQuery.data?.items ?? []}
        onChange={actualizarSearch}
      />

      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar las recepciones"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={8}
          columns={[
            { width: 'w-32' },
            { width: 'w-24' },
            { width: 'w-40' },
            { width: 'w-32' },
            { width: 'w-32' },
            { width: 'w-24' },
          ]}
        />
      ) : itemsFiltrados.length === 0 ? (
        <EmptyState
          title="Sin recepciones"
          description={
            search.q ||
            search.estado != null ||
            search.subAlmacenId ||
            search.ordenCompraId ||
            search.desde ||
            search.hasta
              ? 'No hay recepciones que coincidan con los filtros aplicados.'
              : 'Aún no se han registrado recepciones. Usa "Nueva recepción" para capturar la primera.'
          }
        />
      ) : (
        <TablaRecepciones
          items={itemsFiltrados}
          subAlmacenesMap={subAlmacenesMap}
        />
      )}

      <NuevaRecepcionSheet
        open={sheetAbierto}
        onOpenChange={setSheetAbierto}
      />
    </div>
  );
}

interface FiltrosToolbarProps {
  search: RecepcionesSearch;
  subAlmacenes: readonly { id: string; clave: string; nombre: string }[];
  onChange: (parcial: Partial<RecepcionesSearch>) => void;
}

function FiltrosToolbar({
  search,
  subAlmacenes,
  onChange,
}: FiltrosToolbarProps) {
  const algunFiltro =
    search.q ||
    search.estado != null ||
    search.subAlmacenId ||
    search.ordenCompraId ||
    search.desde ||
    search.hasta;

  return (
    <div className="flex flex-wrap items-end gap-3">
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Folio</label>
        <Input
          aria-label="Buscar por folio"
          value={search.q ?? ''}
          onChange={(e) => onChange({ q: e.target.value || undefined })}
          placeholder="Buscar por folio…"
          className="w-56"
        />
      </div>
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Estado</label>
        <Select
          value={search.estado != null ? String(search.estado) : SENTINEL_ALL}
          onValueChange={(v) =>
            onChange({
              estado:
                v === SENTINEL_ALL
                  ? undefined
                  : (Number(v) as EstadoMovimiento),
            })
          }
        >
          <SelectTrigger aria-label="Filtrar por estado" className="w-44">
            <SelectValue placeholder="Todos" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
            {(
              [
                EstadoMovimiento.Borrador,
                EstadoMovimiento.Validado,
                EstadoMovimiento.Registrado,
                EstadoMovimiento.Cancelado,
              ] as const
            ).map((e) => (
              <SelectItem key={e} value={String(e)}>
                {EstadoMovimientoLabels[e]}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
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
        <label className="text-xs text-muted-foreground">Desde</label>
        <Input
          aria-label="Fecha desde"
          type="date"
          value={search.desde ?? ''}
          onChange={(e) => onChange({ desde: e.target.value || undefined })}
          className="w-40"
        />
      </div>
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Hasta</label>
        <Input
          aria-label="Fecha hasta"
          type="date"
          value={search.hasta ?? ''}
          onChange={(e) => onChange({ hasta: e.target.value || undefined })}
          className="w-40"
        />
      </div>
      {algunFiltro && (
        <Button
          variant="ghost"
          onClick={() =>
            onChange({
              q: undefined,
              estado: undefined,
              subAlmacenId: undefined,
              ordenCompraId: undefined,
              desde: undefined,
              hasta: undefined,
            })
          }
        >
          Limpiar filtros
        </Button>
      )}
    </div>
  );
}

interface TablaRecepcionesProps {
  items: readonly RecepcionListItem[];
  subAlmacenesMap: Map<string, { id: string; clave: string; nombre: string }>;
}

function TablaRecepciones({ items, subAlmacenesMap }: TablaRecepcionesProps) {
  const navigate = useNavigate();
  const [sort, setSort] = useState<SortState<SortKey> | null>(null);

  const enriquecidos = useMemo(
    () =>
      items.map((r) => ({
        ...r,
        subAlmacen:
          subAlmacenesMap.get(r.subAlmacenId)?.clave ?? r.subAlmacenId,
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
                columnKey="folio"
                label="Folio"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="fechaMovimiento"
                label="Fecha"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="subAlmacen"
                label="Sub-almacén"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-left">OC</th>
            <th className="px-3 py-2 text-right">
              <SortableHeader
                columnKey="montoTotalMxn"
                label="Monto"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="estado"
                label="Estado"
                current={sort}
                onSortChange={setSort}
              />
            </th>
          </tr>
        </thead>
        <tbody>
          {ordenados.map((r) => {
            const rowProps = clickableRowProps(() =>
              navigate({
                to: '/almacen/recepciones/$id',
                params: { id: r.id },
              }),
            );
            return (
            <tr
              key={r.id}
              {...rowProps}
              className={`border-t hover:bg-muted/30 ${rowProps.className}`}
              aria-label={`Abrir recepción ${r.folio}`}
            >
              <td className="px-3 py-2 font-mono text-xs text-primary">
                {r.folio}
              </td>
              <td className="px-3 py-2 whitespace-nowrap">
                {r.fechaMovimiento}
              </td>
              <td className="px-3 py-2 font-mono text-xs">{r.subAlmacen}</td>
              <td className="px-3 py-2 text-xs">
                {r.ordenCompraFolio ? (
                  <span className="font-mono">{r.ordenCompraFolio}</span>
                ) : r.ordenCompraId ? (
                  <span className="font-mono text-muted-foreground">
                    {truncarId(r.ordenCompraId)}
                  </span>
                ) : (
                  <span className="italic text-muted-foreground">—</span>
                )}
              </td>
              <td className="px-3 py-2 text-right font-mono">
                {formatearMonto(r.montoTotalMxn)}
              </td>
              <td className="px-3 py-2">
                <EstadoMovimientoBadge estado={r.estado} />
              </td>
            </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function EstadoMovimientoBadge({ estado }: { estado: EstadoMovimiento }) {
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
        estado === EstadoMovimiento.Borrador && 'bg-slate-200 text-slate-700',
        estado === EstadoMovimiento.Validado && 'bg-blue-100 text-blue-800',
        estado === EstadoMovimiento.Registrado &&
          'bg-emerald-100 text-emerald-800',
        estado === EstadoMovimiento.Cancelado && 'bg-rose-100 text-rose-800',
      )}
    >
      {EstadoMovimientoLabels[estado]}
    </span>
  );
}

function formatearMonto(v: number): string {
  return new Intl.NumberFormat('es-MX', {
    style: 'currency',
    currency: 'MXN',
    minimumFractionDigits: 2,
  }).format(v);
}

function truncarId(id: string): string {
  return id.length > 12 ? `${id.slice(0, 8)}…` : id;
}
