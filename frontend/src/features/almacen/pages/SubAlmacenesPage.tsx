import { usePaginacionLocal } from '../lib/use-paginacion-local';
import { PaginacionTabla, BarraFiltros } from '@/components/erp';
import { TabsEstado as TabsCatalogo } from '@/components/erp';
import { ESTATUS_CATALOGO } from '../lib/estado-variante';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableCell,
  TableHead,
} from '@/components/ui/table';
import { PageHeader } from '@/components/erp/PageHeader';
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
import { useAlmacenes, useSubAlmacenes } from '@/features/almacen/api/useAlmacenes';
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
import { EstadoAlmacenBadge } from '@/features/almacen/components/EstadoAlmacenBadge';

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
  const puedeAdministrar = useHasPermission(PermisosCanonicos.AlmacenAlmacenesAdministrar);

  const [sheetAbierto, setSheetAbierto] = useState(false);
  const [editando, setEditando] = useState<SubAlmacenListItem | null>(null);

  const almacenesQuery = useAlmacenes({ limit: 500 });
  const almacenesMap = useMemo(() => mapById(almacenesQuery.data?.items), [almacenesQuery.data]);

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
    <div className="space-y-5">
      <PageHeader
        titulo="Sub-almacenes"
        descripcion="Subdivisión física de cada almacén por tipo de material."
        acciones={
          <>
            {puedeAdministrar && (
              <Button onClick={abrirCrear}>
                <Plus className="mr-2 size-3.5" />
                Nuevo sub-almacén
              </Button>
            )}
          </>
        }
      />

      <ListadoModulo
        tabs={
          <TabsCatalogo
            opciones={Object.entries(ESTATUS_CATALOGO).map(([valor, e]) => ({
              valor: Number(valor),
              label: e.etiqueta,
            }))}
            valor={search.estatus}
            conteos={undefined}
            onChange={(estatus) => actualizarSearch({ estatus: estatus as typeof search.estatus })}
          />
        }
        filtros={
          <FiltrosToolbar
            search={search}
            almacenes={almacenesQuery.data?.items ?? []}
            onChange={actualizarSearch}
          />
        }
      >
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
      </ListadoModulo>

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

function FiltrosToolbar({ search, almacenes, onChange }: FiltrosToolbarProps) {
  return (
    <BarraFiltros
      filtros={
        <div className="flex flex-wrap items-end gap-3">
          <div className="space-y-1">
            <label className="text-xs text-ink-muted">Buscar</label>
            <Input
              value={search.q ?? ''}
              onChange={(e) => onChange({ q: e.target.value || undefined })}
              placeholder="Clave o nombre…"
              className="w-64"
            />
          </div>
          <div className="space-y-1">
            <label className="text-xs text-ink-muted">Almacén</label>
            <Select
              value={search.almacenId ?? SENTINEL_ALL}
              onValueChange={(v) => onChange({ almacenId: v === SENTINEL_ALL ? undefined : v })}
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
            <label className="text-xs text-ink-muted">Tipo</label>
            <Select
              value={search.tipo != null ? String(search.tipo) : SENTINEL_ALL}
              onValueChange={(v) =>
                onChange({
                  tipo: v === SENTINEL_ALL ? undefined : (Number(v) as TipoSubAlmacen),
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
            <label className="text-xs text-ink-muted">Estatus</label>
            <Select
              value={search.estatus != null ? String(search.estatus) : SENTINEL_ALL}
              onValueChange={(v) =>
                onChange({
                  estatus: v === SENTINEL_ALL ? undefined : (Number(v) as EstatusCatalogo),
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
          {(search.q || search.almacenId || search.tipo != null || search.estatus != null) && (
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
      }
    />
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
        almacen: almacenesMap.get(s.almacenId)?.clave ?? s.almacenId,
      })),
    [items, almacenesMap],
  );

  const ordenados = useMemo(() => {
    if (sort == null) return enriquecidos;
    return [...enriquecidos].sort((a, b) => compareItemsBy(a, b, sort.key, sort.dir));
  }, [enriquecidos, sort]);

  const paginacion = usePaginacionLocal(ordenados, JSON.stringify(ordenados.map((r) => r.id)));
  return (
    <>
      <div className="overflow-x-auto rounded-md border">
        <Table className="w-full text-sm">
          <TableHeader className="bg-surface-muted/50">
            <TableRow>
              <TableHead className="px-3 py-2 text-left">
                <SortableHeader
                  columnKey="almacen"
                  label="Almacén"
                  current={sort}
                  onSortChange={setSort}
                />
              </TableHead>
              <TableHead className="px-3 py-2 text-left">
                <SortableHeader
                  columnKey="clave"
                  label="Clave"
                  current={sort}
                  onSortChange={setSort}
                />
              </TableHead>
              <TableHead className="px-3 py-2 text-left">
                <SortableHeader
                  columnKey="nombre"
                  label="Nombre"
                  current={sort}
                  onSortChange={setSort}
                />
              </TableHead>
              <TableHead className="px-3 py-2 text-left">
                <SortableHeader
                  columnKey="tipo"
                  label="Tipo"
                  current={sort}
                  onSortChange={setSort}
                />
              </TableHead>
              <TableHead className="px-3 py-2 text-left">
                <SortableHeader
                  columnKey="estatus"
                  label="Estatus"
                  current={sort}
                  onSortChange={setSort}
                />
              </TableHead>
              {puedeAdministrar && (
                <TableHead className="px-3 py-2 text-right font-medium">Acciones</TableHead>
              )}
            </TableRow>
          </TableHeader>
          <TableBody>
            {paginacion.visibles.map((s) => (
              <TableRow key={s.id} className="border-t">
                <TableCell className="px-3 py-2 font-mono text-xs">{s.almacen}</TableCell>
                <TableCell className="px-3 py-2 font-mono text-xs">{s.clave}</TableCell>
                <TableCell className="px-3 py-2">{s.nombre}</TableCell>
                <TableCell className="px-3 py-2">{TipoSubAlmacenLabels[s.tipo]}</TableCell>
                <TableCell className="px-3 py-2">
                  <EstadoAlmacenBadge tipo="catalogo" estado={s.estatus} />
                </TableCell>
                {puedeAdministrar && (
                  <TableCell className="px-3 py-2 text-right">
                    <Button
                      type="button"
                      size="sm"
                      variant="ghost"
                      onClick={() => onEditar(s)}
                      aria-label={`Editar sub-almacén ${s.clave}`}
                    >
                      <Pencil className="h-4 w-4" />
                    </Button>
                  </TableCell>
                )}
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
      <PaginacionTabla
        total={ordenados.length}
        offset={paginacion.offset}
        limit={10}
        tamanos={[]}
        onChange={paginacion.onChange}
      />
    </>
  );
}
