import { useMemo, useState } from 'react';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';
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
import { useConteos } from '@/features/almacen/api/useConteos';
import { useSubAlmacenes } from '@/features/almacen/api/useAlmacenes';
import {
  EstadoConteo,
  EstadoConteoLabels,
  TipoConteo,
  TipoConteoLabels,
  type ConteoListItem,
} from '@/features/almacen/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { NuevoConteoSheet } from '@/features/almacen/components/NuevoConteoSheet';
import type { ConteosSearch } from '@/features/almacen/lib/conteos-search-schema';
import { cn } from '@/lib/utils';

const FROM = '/_app/almacen/inventarios/' as const;
const SENTINEL_ALL = '__all__';

type SortKey =
  | 'tipo'
  | 'estado'
  | 'fechaPlanificada'
  | 'subAlmacen'
  | 'capturadas';

/**
 * <c>P7 — Bandeja de conteos</c> (doc 07 §FE-F5-PR1). Lista los
 * conteos de inventario físico con filtros (estado, tipo,
 * sub-almacén) y abre el sheet "Nuevo conteo". El gateo de captura
 * sin sesgo (A6) vive en la pantalla de captura, no aquí.
 */
export function InventariosPage() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();

  const puedeCrear = useHasPermission(
    PermisosCanonicos.AlmacenInventariosCrear,
  );

  const [sheetAbierto, setSheetAbierto] = useState(false);

  const subAlmacenesQuery = useSubAlmacenes({ limit: 500 });
  const subAlmacenesMap = useMemo(
    () => mapById(subAlmacenesQuery.data?.items),
    [subAlmacenesQuery.data],
  );

  const query = useConteos({
    estado: search.estado,
    tipo: search.tipo,
    subAlmacenId: search.subAlmacenId,
    limit: 200,
  });

  function actualizarSearch(parcial: Partial<ConteosSearch>) {
    navigate({ to: '/almacen/inventarios', search: { ...search, ...parcial } });
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">
            Inventario físico
          </h1>
          <p className="text-sm text-muted-foreground">
            Conteos rotativos y anuales. Captura sin sesgo (A6) — el
            contador NO ve la cantidad teórica.
          </p>
        </div>
        {puedeCrear && (
          <Button onClick={() => setSheetAbierto(true)}>
            <Plus className="mr-2 h-4 w-4" />
            Nuevo conteo
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
          title="No se pudieron cargar los conteos"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={6}
          columns={[
            { width: 'w-24' },
            { width: 'w-32' },
            { width: 'w-32' },
            { width: 'w-40' },
            { width: 'w-32' },
            { width: 'w-32' },
          ]}
        />
      ) : (query.data?.items ?? []).length === 0 ? (
        <EmptyState
          title="Sin conteos"
          description={
            puedeCrear
              ? 'No hay conteos. Usa "Nuevo conteo" para planificar uno.'
              : 'No hay conteos registrados.'
          }
        />
      ) : (
        <TablaConteos
          items={query.data!.items}
          subAlmacenesMap={subAlmacenesMap}
        />
      )}

      <NuevoConteoSheet open={sheetAbierto} onOpenChange={setSheetAbierto} />
    </div>
  );
}

interface FiltrosToolbarProps {
  search: ConteosSearch;
  subAlmacenes: readonly { id: string; clave: string; nombre: string }[];
  onChange: (parcial: Partial<ConteosSearch>) => void;
}

function FiltrosToolbar({
  search,
  subAlmacenes,
  onChange,
}: FiltrosToolbarProps) {
  const algun =
    search.estado != null || search.tipo != null || search.subAlmacenId;

  return (
    <div className="flex flex-wrap items-end gap-3">
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Estado</label>
        <Select
          value={search.estado != null ? String(search.estado) : SENTINEL_ALL}
          onValueChange={(v) =>
            onChange({
              estado:
                v === SENTINEL_ALL ? undefined : (Number(v) as EstadoConteo),
            })
          }
        >
          <SelectTrigger className="w-44">
            <SelectValue placeholder="Todos" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
            {(
              [
                EstadoConteo.Planificado,
                EstadoConteo.EnCurso,
                EstadoConteo.EnConciliacion,
                EstadoConteo.Aprobado,
                EstadoConteo.Aplicado,
                EstadoConteo.Rechazado,
              ] as const
            ).map((e) => (
              <SelectItem key={e} value={String(e)}>
                {EstadoConteoLabels[e]}
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
                v === SENTINEL_ALL ? undefined : (Number(v) as TipoConteo),
            })
          }
        >
          <SelectTrigger className="w-40">
            <SelectValue placeholder="Todos" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
            <SelectItem value={String(TipoConteo.Rotativo)}>
              {TipoConteoLabels[TipoConteo.Rotativo]}
            </SelectItem>
            <SelectItem value={String(TipoConteo.Anual)}>
              {TipoConteoLabels[TipoConteo.Anual]}
            </SelectItem>
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
          <SelectTrigger className="w-56">
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
      {algun && (
        <Button
          variant="ghost"
          onClick={() =>
            onChange({
              estado: undefined,
              tipo: undefined,
              subAlmacenId: undefined,
            })
          }
        >
          Limpiar filtros
        </Button>
      )}
    </div>
  );
}

function TablaConteos({
  items,
  subAlmacenesMap,
}: {
  items: readonly ConteoListItem[];
  subAlmacenesMap: Map<string, { id: string; clave: string; nombre: string }>;
}) {
  const [sort, setSort] = useState<SortState<SortKey> | null>(null);

  const enriquecidos = useMemo(
    () =>
      items.map((c) => ({
        ...c,
        subAlmacen: c.subAlmacenId
          ? (subAlmacenesMap.get(c.subAlmacenId)?.clave ?? c.subAlmacenId)
          : '(todos)',
        capturadas: c.cantidadCapturadas,
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
                columnKey="tipo"
                label="Tipo"
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
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="fechaPlanificada"
                label="Planificado"
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
            <th className="px-3 py-2 text-left">Familia</th>
            <th className="px-3 py-2 text-right">
              <SortableHeader
                columnKey="capturadas"
                label="Capturadas / Total"
                current={sort}
                onSortChange={setSort}
              />
            </th>
          </tr>
        </thead>
        <tbody>
          {ordenados.map((c) => (
            <tr
              key={c.id}
              className="border-t hover:bg-muted/30 focus-within:bg-muted/30"
            >
              <td className="px-3 py-2">
                <Link
                  to="/almacen/inventarios/$id"
                  params={{ id: c.id }}
                  className="font-medium text-primary hover:underline"
                >
                  {TipoConteoLabels[c.tipo]}
                </Link>
              </td>
              <td className="px-3 py-2">
                <EstadoConteoBadge estado={c.estado} />
              </td>
              <td className="px-3 py-2 whitespace-nowrap">
                {c.fechaPlanificada}
              </td>
              <td className="px-3 py-2 font-mono text-xs">{c.subAlmacen}</td>
              <td className="px-3 py-2 text-xs text-muted-foreground">
                {c.filtroFamilia ?? '—'}
              </td>
              <td className="px-3 py-2 text-right font-mono">
                {c.cantidadCapturadas} / {c.cantidadLineas}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function EstadoConteoBadge({ estado }: { estado: EstadoConteo }) {
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
        estado === EstadoConteo.Planificado && 'bg-slate-200 text-slate-700',
        estado === EstadoConteo.EnCurso && 'bg-blue-100 text-blue-800',
        estado === EstadoConteo.EnConciliacion &&
          'bg-amber-100 text-amber-800',
        estado === EstadoConteo.Aprobado && 'bg-violet-100 text-violet-800',
        estado === EstadoConteo.Aplicado && 'bg-emerald-100 text-emerald-800',
        estado === EstadoConteo.Rechazado && 'bg-rose-100 text-rose-800',
      )}
    >
      {EstadoConteoLabels[estado]}
    </span>
  );
}
