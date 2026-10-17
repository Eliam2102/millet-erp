import { PageHeader } from '@/components/erp/PageHeader';
import { useMemo, useState } from 'react';
import { Link, useNavigate, useSearch } from '@tanstack/react-router';
import { Plus } from 'lucide-react';
import { Button } from '@/components/ui/button';
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
  EmptyState,
  ErrorState,
  PaginacionTabla,
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
import { EstadoAlmacenBadge } from '@/features/almacen/components/EstadoAlmacenBadge';
import { Field } from '@/features/almacen/components/internal/Field';
import { BusquedaFolio } from '@/features/almacen/components/internal/BusquedaFolio';
import { offsetLimitAPagina, paginaAOffsetLimit } from '@/features/almacen/lib/paginacion';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';

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
    q: search.q,
    ...paginaAOffsetLimit(search),
  });

  function actualizarSearch(parcial: Partial<ConteosSearch>) {
    // Cambiar un filtro regresa a la página 1; la paginación pasa su propia página.
    navigate({
      to: '/almacen/inventarios',
      search: { ...search, page: undefined, ...parcial },
    });
  }

  return (
    <div className="space-y-4">
      <PageHeader
        titulo="Inventario físico"
        descripcion="Conteos rotativos y anuales. Captura sin sesgo (A6) — el contador NO ve la cantidad teórica."
        acciones={
          <>
            {puedeCrear && (
              <Button onClick={() => setSheetAbierto(true)}>
                <Plus className="mr-2 h-4 w-4" />
                Nuevo conteo
              </Button>
            )}
          </>
        }
      />

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
            search.q || search.estado != null || search.tipo != null || search.subAlmacenId
              ? 'No hay conteos que coincidan con los filtros aplicados.'
              : puedeCrear
              ? 'No hay conteos. Usa "Nuevo conteo" para planificar uno.'
              : 'No hay conteos registrados.'
          }
        />
      ) : (
        <div className="overflow-hidden rounded-lg border border-line-row bg-surface-card">
          <TablaConteos items={query.data!.items} subAlmacenesMap={subAlmacenesMap} />
          <PaginacionTabla
            total={query.data!.total}
            offset={query.data!.offset}
            limit={query.data!.limit}
            onChange={(offset, limit) => actualizarSearch(offsetLimitAPagina(offset, limit))}
          />
        </div>
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
    search.q ||
    search.estado != null || search.tipo != null || search.subAlmacenId;

  return (
    <div className="flex flex-wrap items-end gap-3">
      <Field label="Familia" className="gap-1">
        <BusquedaFolio
          aria-label="Buscar por familia"
          valor={search.q}
          onCommit={(q) => onChange({ q })}
          placeholder="Buscar por familia…"
          className="w-56"
        />
      </Field>
      <Field label="Estado" className="gap-1">
        {(c) => (
        <Select
          value={search.estado != null ? String(search.estado) : SENTINEL_ALL}
          onValueChange={(v) =>
            onChange({
              estado:
                v === SENTINEL_ALL ? undefined : (Number(v) as EstadoConteo),
            })
          }
        >
          <SelectTrigger {...c} className="w-44">
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
        )}
      </Field>
      <Field label="Tipo" className="gap-1">
        {(c) => (
        <Select
          value={search.tipo != null ? String(search.tipo) : SENTINEL_ALL}
          onValueChange={(v) =>
            onChange({
              tipo:
                v === SENTINEL_ALL ? undefined : (Number(v) as TipoConteo),
            })
          }
        >
          <SelectTrigger {...c} className="w-40">
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
        )}
      </Field>
      <Field label="Sub-almacén" className="gap-1">
        {(c) => (
        <Select
          value={search.subAlmacenId ?? SENTINEL_ALL}
          onValueChange={(v) =>
            onChange({ subAlmacenId: v === SENTINEL_ALL ? undefined : v })
          }
        >
          <SelectTrigger {...c} className="w-56">
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
        )}
      </Field>
      {algun && (
        <Button
          variant="ghost"
          onClick={() =>
            onChange({
              q: undefined,
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
    <TableContainer>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>
              <SortableHeader
                columnKey="tipo"
                label="Tipo"
                current={sort}
                onSortChange={setSort}
              />
            </TableHead>
            <TableHead>
              <SortableHeader
                columnKey="estado"
                label="Estado"
                current={sort}
                onSortChange={setSort}
              />
            </TableHead>
            <TableHead>
              <SortableHeader
                columnKey="fechaPlanificada"
                label="Planificado"
                current={sort}
                onSortChange={setSort}
              />
            </TableHead>
            <TableHead>
              <SortableHeader
                columnKey="subAlmacen"
                label="Sub-almacén"
                current={sort}
                onSortChange={setSort}
              />
            </TableHead>
            <TableHead>Familia</TableHead>
            <TableHead className="text-right">
              <SortableHeader
                columnKey="capturadas"
                label="Capturadas / Total"
                current={sort}
                onSortChange={setSort}
              />
            </TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {ordenados.map((c) => (
            <TableRow key={c.id} className="hover:bg-surface-subtle focus-within:bg-surface-subtle">
              <TableCell>
                <Link
                  to="/almacen/inventarios/$id"
                  params={{ id: c.id }}
                  className="font-medium text-brand hover:underline"
                >
                  {TipoConteoLabels[c.tipo]}
                </Link>
              </TableCell>
              <TableCell>
                <EstadoAlmacenBadge tipo="conteo" estado={c.estado} />
              </TableCell>
              <TableCell className="whitespace-nowrap">
                <DateTimeDisplay value={c.fechaPlanificada} variant="corta" />
              </TableCell>
              <TableCell className="font-mono text-xs">{c.subAlmacen}</TableCell>
              <TableCell className="text-xs text-ink-muted">
                {c.filtroFamilia ?? '—'}
              </TableCell>
              <TableCell className="text-right font-mono tabular-nums">
                {c.cantidadCapturadas} / {c.cantidadLineas}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </TableContainer>
  );
}
