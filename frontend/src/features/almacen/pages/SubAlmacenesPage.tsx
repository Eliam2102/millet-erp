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
import { mapById } from '@/features/catalogos/api';
import {
  useAlmacenes,
  useSubAlmacenes,
} from '@/features/almacen/api/useAlmacenes';
import {
  EstatusCatalogo,
  EstatusCatalogoLabels,
  TipoSubAlmacen,
  TipoSubAlmacenLabels,
  type SubAlmacenListItem,
} from '@/features/almacen/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { SubAlmacenSheet } from '@/features/almacen/components/SubAlmacenSheet';
import type { SubAlmacenesSearch } from '@/features/almacen/lib/catalogo-search-schemas';
import { cn } from '@/lib/utils';

const FROM = '/_app/almacen/sub-almacenes' as const;
const SENTINEL_ALL = '__all__';

type SortKey = 'almacen' | 'clave' | 'nombre' | 'tipo' | 'estatus';

/**
 * <c>P2 — Bandeja de sub-almacenes</c> (doc 07 §FE-F1-PR1).
 * Filtros: almacén padre, tipo (Insumos/MD/MAT-REV/Transitorio),
 * estatus, búsqueda libre. Crear/editar abre <c>SubAlmacenSheet</c>.
 */
export function SubAlmacenesPage() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const puedeAdministrar = useHasPermission(
    PermisosCanonicos.AlmacenAlmacenesAdministrar,
  );

  const [sheetAbierto, setSheetAbierto] = useState(false);
  const [editando, setEditando] = useState<SubAlmacenListItem | null>(null);

  const almacenesQuery = useAlmacenes({ limit: 500 });
  const almacenesMap = useMemo(
    () => mapById(almacenesQuery.data?.items),
    [almacenesQuery.data],
  );

  const query = useSubAlmacenes({
    q: search.q,
    almacenId: search.almacenId,
    tipo: search.tipo,
    estatus: search.estatus,
    limit: 500,
  });

  function actualizarSearch(parcial: Partial<SubAlmacenesSearch>) {
    navigate({
      to: '/almacen/sub-almacenes',
      search: { ...search, ...parcial },
    });
  }

  function abrirCrear() {
    setEditando(null);
    setSheetAbierto(true);
  }

  function abrirEditar(s: SubAlmacenListItem) {
    setEditando(s);
    setSheetAbierto(true);
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            Sub-almacenes
          </h1>
          <p className="text-sm text-muted-foreground">
            Subdivisión física de cada almacén por tipo de material.
          </p>
        </div>
        {puedeAdministrar && (
          <Button onClick={abrirCrear}>
            <Plus className="mr-2 h-4 w-4" />
            Nuevo sub-almacén
          </Button>
        )}
      </div>

      <FiltrosToolbar
        search={search}
        almacenes={almacenesQuery.data?.items ?? []}
        onChange={actualizarSearch}
      />

      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar los sub-almacenes"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={6}
          columns={[
            { width: 'w-40' },
            { width: 'w-32' },
            { width: 'w-64' },
            { width: 'w-40' },
            { width: 'w-24' },
            { width: 'w-16' },
          ]}
        />
      ) : (query.data?.items ?? []).length === 0 ? (
        <EmptyState
          title="Sin sub-almacenes"
          description={
            puedeAdministrar
              ? 'No hay sub-almacenes que coincidan con los filtros. Usa "Nuevo sub-almacén" para crear uno.'
              : 'No hay sub-almacenes registrados.'
          }
        />
      ) : (
        <TablaSubAlmacenes
          items={query.data!.items}
          almacenesMap={almacenesMap}
          puedeAdministrar={puedeAdministrar}
          onEditar={abrirEditar}
        />
      )}

      <SubAlmacenSheet
        open={sheetAbierto}
        onOpenChange={setSheetAbierto}
        editando={editando}
        almacenIdInicial={search.almacenId}
      />
    </div>
  );
}

interface FiltrosToolbarProps {
  search: SubAlmacenesSearch;
  almacenes: readonly { id: string; clave: string; nombre: string }[];
  onChange: (parcial: Partial<SubAlmacenesSearch>) => void;
}

function FiltrosToolbar({
  search,
  almacenes,
  onChange,
}: FiltrosToolbarProps) {
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
        <label className="text-xs text-muted-foreground">Almacén</label>
        <Select
          value={search.almacenId ?? SENTINEL_ALL}
          onValueChange={(v) =>
            onChange({ almacenId: v === SENTINEL_ALL ? undefined : v })
          }
        >
          <SelectTrigger className="w-56">
            <SelectValue placeholder="Todos" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
            {almacenes.map((a) => (
              <SelectItem key={a.id} value={a.id}>
                {a.clave} · {a.nombre}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Tipo</label>
        <Select
          value={search.tipo != null ? String(search.tipo) : SENTINEL_ALL}
          onValueChange={(v) =>
            onChange({
              tipo:
                v === SENTINEL_ALL
                  ? undefined
                  : (Number(v) as TipoSubAlmacen),
            })
          }
        >
          <SelectTrigger className="w-52">
            <SelectValue placeholder="Todos los tipos" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
            {(
              [
                TipoSubAlmacen.Insumos,
                TipoSubAlmacen.MaterialesDirectos,
                TipoSubAlmacen.MaterialEnRevision,
                TipoSubAlmacen.Transitorio,
              ] as const
            ).map((t) => (
              <SelectItem key={t} value={String(t)}>
                {TipoSubAlmacenLabels[t]}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
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
      {(search.q ||
        search.almacenId ||
        search.tipo != null ||
        search.estatus != null) && (
        <Button
          variant="ghost"
          onClick={() =>
            onChange({
              q: undefined,
              almacenId: undefined,
              tipo: undefined,
              estatus: undefined,
            })
          }
        >
          Limpiar filtros
        </Button>
      )}
    </div>
  );
}

interface TablaSubAlmacenesProps {
  items: readonly SubAlmacenListItem[];
  almacenesMap: Map<string, { id: string; clave: string; nombre: string }>;
  puedeAdministrar: boolean;
  onEditar: (s: SubAlmacenListItem) => void;
}

function TablaSubAlmacenes({
  items,
  almacenesMap,
  puedeAdministrar,
  onEditar,
}: TablaSubAlmacenesProps) {
  const [sort, setSort] = useState<SortState<SortKey> | null>(null);

  const enriquecidos = useMemo(
    () =>
      items.map((s) => ({
        ...s,
        almacen:
          almacenesMap.get(s.almacenId)?.clave ?? s.almacenId,
      })),
    [items, almacenesMap],
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
                columnKey="almacen"
                label="Almacén"
                current={sort}
                onSortChange={setSort}
              />
            </th>
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
                columnKey="tipo"
                label="Tipo"
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
          {ordenados.map((s) => (
            <tr key={s.id} className="border-t">
              <td className="px-3 py-2 font-mono text-xs">{s.almacen}</td>
              <td className="px-3 py-2 font-mono text-xs">{s.clave}</td>
              <td className="px-3 py-2">{s.nombre}</td>
              <td className="px-3 py-2">{TipoSubAlmacenLabels[s.tipo]}</td>
              <td className="px-3 py-2">
                <EstatusBadge estatus={s.estatus} />
              </td>
              {puedeAdministrar && (
                <td className="px-3 py-2 text-right">
                  <Button
                    type="button"
                    size="sm"
                    variant="ghost"
                    onClick={() => onEditar(s)}
                    aria-label={`Editar sub-almacén ${s.clave}`}
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
