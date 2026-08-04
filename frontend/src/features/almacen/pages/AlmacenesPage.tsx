import { useMemo, useState } from 'react';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { Pencil, Plus } from 'lucide-react';
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
  compareItemsBy,
  type SortState,
} from '@/components/erp';
import { mapById, useSucursales } from '@/features/catalogos/api';
import { useAlmacenes } from '@/features/almacen/api/useAlmacenes';
import {
  EstatusCatalogo,
  EstatusCatalogoLabels,
  type AlmacenListItem,
} from '@/features/almacen/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { AlmacenSheet } from '@/features/almacen/components/AlmacenSheet';
import type { AlmacenesSearch } from '@/features/almacen/lib/catalogo-search-schemas';
import { cn } from '@/lib/utils';

const FROM = '/_app/almacen/almacenes' as const;
const SENTINEL_ALL = '__all__';

type SortKey = 'clave' | 'nombre' | 'sucursal' | 'estatus';

/**
 * <c>P1 — Bandeja de almacenes</c> (doc 07 §FE-F1-PR1). Tabla con
 * búsqueda por texto + filtros por estatus y sucursal. Botón
 * "Nuevo almacén" abre <c>AlmacenSheet</c>; click en fila abre
 * el mismo sheet en modo edición. Gateada por
 * <c>almacen.almacenes.leer</c>; el botón de crear/editar requiere
 * <c>almacen.almacenes.administrar</c>.
 */
export function AlmacenesPage() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const puedeAdministrar = useHasPermission(
    PermisosCanonicos.AlmacenAlmacenesAdministrar,
  );

  const [sheetAbierto, setSheetAbierto] = useState(false);
  const [editando, setEditando] = useState<AlmacenListItem | null>(null);

  const sucursalesQuery = useSucursales();
  const sucursalesMap = useMemo(
    () => mapById(sucursalesQuery.data?.items),
    [sucursalesQuery.data],
  );

  const query = useAlmacenes({
    q: search.q,
    estatus: search.estatus,
    sucursalId: search.sucursalId,
    limit: 500,
  });

  function actualizarSearch(parcial: Partial<AlmacenesSearch>) {
    navigate({
      to: '/almacen/almacenes',
      search: { ...search, ...parcial },
    });
  }

  function abrirCrear() {
    setEditando(null);
    setSheetAbierto(true);
  }

  function abrirEditar(a: AlmacenListItem) {
    setEditando(a);
    setSheetAbierto(true);
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Almacenes</h1>
          <p className="text-sm text-muted-foreground">
            Catálogo principal del módulo. Cada almacén agrupa uno o más
            sub-almacenes por tipo (insumos, materiales directos, etc.).
          </p>
        </div>
        {puedeAdministrar && (
          <Button onClick={abrirCrear}>
            <Plus className="mr-2 h-4 w-4" />
            Nuevo almacén
          </Button>
        )}
      </div>

      <FiltrosToolbar
        search={search}
        sucursales={sucursalesQuery.data?.items ?? []}
        onChange={actualizarSearch}
      />

      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar los almacenes"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={6}
          columns={[
            { width: 'w-32' },
            { width: 'w-64' },
            { width: 'w-48' },
            { width: 'w-24' },
            { width: 'w-16' },
          ]}
        />
      ) : (query.data?.items ?? []).length === 0 ? (
        <EmptyState
          title="Sin almacenes"
          description={
            puedeAdministrar
              ? 'No hay almacenes que coincidan con los filtros. Usa "Nuevo almacén" para crear el primero.'
              : 'No hay almacenes registrados.'
          }
        />
      ) : (
        <TablaAlmacenes
          items={query.data!.items}
          sucursalesMap={sucursalesMap}
          puedeAdministrar={puedeAdministrar}
          onEditar={abrirEditar}
        />
      )}

      <AlmacenSheet
        open={sheetAbierto}
        onOpenChange={setSheetAbierto}
        editando={editando}
      />
    </div>
  );
}

interface FiltrosToolbarProps {
  search: AlmacenesSearch;
  sucursales: readonly { id: string; clave: string; nombre: string }[];
  onChange: (parcial: Partial<AlmacenesSearch>) => void;
}

function FiltrosToolbar({ search, sucursales, onChange }: FiltrosToolbarProps) {
  return (
    <div className="flex flex-wrap items-end gap-3">
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Buscar</label>
        <Input
          value={search.q ?? ''}
          onChange={(e) => onChange({ q: e.target.value || undefined })}
          placeholder="Clave o nombre…"
          className="w-64"
        />
      </div>
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Estatus</label>
        <Select
          value={search.estatus != null ? String(search.estatus) : SENTINEL_ALL}
          onValueChange={(v) =>
            onChange({
              estatus:
                v === SENTINEL_ALL
                  ? undefined
                  : (Number(v) as EstatusCatalogo),
            })
          }
        >
          <SelectTrigger className="w-44">
            <SelectValue placeholder="Todos" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
            <SelectItem value={String(EstatusCatalogo.Activo)}>
              {EstatusCatalogoLabels[EstatusCatalogo.Activo]}
            </SelectItem>
            <SelectItem value={String(EstatusCatalogo.Inactivo)}>
              {EstatusCatalogoLabels[EstatusCatalogo.Inactivo]}
            </SelectItem>
            <SelectItem value={String(EstatusCatalogo.Borrador)}>
              {EstatusCatalogoLabels[EstatusCatalogo.Borrador]}
            </SelectItem>
          </SelectContent>
        </Select>
      </div>
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Sucursal</label>
        <Select
          value={search.sucursalId ?? SENTINEL_ALL}
          onValueChange={(v) =>
            onChange({ sucursalId: v === SENTINEL_ALL ? undefined : v })
          }
        >
          <SelectTrigger className="w-56">
            <SelectValue placeholder="Todas" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={SENTINEL_ALL}>Todas</SelectItem>
            {sucursales.map((s) => (
              <SelectItem key={s.id} value={s.id}>
                {s.clave} · {s.nombre}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      {(search.q || search.estatus != null || search.sucursalId) && (
        <Button
          variant="ghost"
          onClick={() =>
            onChange({ q: undefined, estatus: undefined, sucursalId: undefined })
          }
        >
          Limpiar filtros
        </Button>
      )}
    </div>
  );
}

interface TablaAlmacenesProps {
  items: readonly AlmacenListItem[];
  sucursalesMap: Map<string, { id: string; clave: string; nombre: string }>;
  puedeAdministrar: boolean;
  onEditar: (a: AlmacenListItem) => void;
}

function TablaAlmacenes({
  items,
  sucursalesMap,
  puedeAdministrar,
  onEditar,
}: TablaAlmacenesProps) {
  const [sort, setSort] = useState<SortState<SortKey> | null>(null);

  const enriquecidos = useMemo(
    () =>
      items.map((a) => ({
        ...a,
        sucursal:
          sucursalesMap.get(a.sucursalId)?.nombre ?? a.sucursalId,
      })),
    [items, sucursalesMap],
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
                columnKey="clave"
                label="Clave"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="nombre"
                label="Nombre"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="sucursal"
                label="Sucursal"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="estatus"
                label="Estatus"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            {puedeAdministrar && (
              <th className="px-3 py-2 text-right font-medium">Acciones</th>
            )}
          </tr>
        </thead>
        <tbody>
          {ordenados.map((a) => (
            <tr key={a.id} className="border-t">
              <td className="px-3 py-2 font-mono text-xs">{a.clave}</td>
              <td className="px-3 py-2">{a.nombre}</td>
              <td className="px-3 py-2">{a.sucursal}</td>
              <td className="px-3 py-2">
                <EstatusBadge estatus={a.estatus} />
              </td>
              {puedeAdministrar && (
                <td className="px-3 py-2 text-right">
                  <Button
                    type="button"
                    size="sm"
                    variant="ghost"
                    onClick={() => onEditar(a)}
                    aria-label={`Editar almacén ${a.clave}`}
                  >
                    <Pencil className="h-4 w-4" />
                  </Button>
                </td>
              )}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function EstatusBadge({ estatus }: { estatus: EstatusCatalogo }) {
  const label = EstatusCatalogoLabels[estatus];
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
        estatus === EstatusCatalogo.Activo &&
          'bg-emerald-100 text-emerald-800',
        estatus === EstatusCatalogo.Inactivo && 'bg-slate-200 text-slate-700',
        estatus === EstatusCatalogo.Borrador &&
          'bg-amber-100 text-amber-800',
      )}
    >
      {label}
    </span>
  );
}
