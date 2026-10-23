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
import { ArrowLeft, Ban, Plus } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
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
import { rutaUbicacion } from '@/features/almacen/lib/ubicacion-label';
import { useUbicaciones } from '@/features/almacen/api/useUbicaciones';
import { useAsignacionesList, useDesasignar } from '@/features/almacen/api/useAsignaciones';
import {
  EstatusCatalogo,
  EstatusCatalogoLabels,
  type AsignacionListItem,
} from '@/features/almacen/api/types';
import type { AsignacionesSearch } from '@/features/almacen/lib/catalogo-search-schemas';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { AsignacionSheet } from '@/features/almacen/components/AsignacionSheet';
import { EstadoAlmacenBadge } from '@/features/almacen/components/EstadoAlmacenBadge';

const FROM = '/_app/almacen/asignaciones' as const;
const SENTINEL_ALL = '__all__';

type SortKey = 'articulo' | 'ubicacion' | 'estatus';

/**
 * <c>P1 — Ubicación de artículos</c> (asignación N4, ADR-0047 PR C). Tabla con
 * filtros por artículo y estatus. "Asignar artículo" abre <c>AsignacionSheet</c>;
 * el botón de bloqueo quita la asignación (guardrail si hay existencia). Gateada
 * por <c>almacen.asignaciones.leer</c>; asignar/quitar requiere
 * <c>almacen.asignaciones.administrar</c>.
 */
export function AsignacionesPage() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const puedeAdministrar = useHasPermission(PermisosCanonicos.AlmacenAsignacionesAdministrar);

  const [sheetAbierto, setSheetAbierto] = useState(false);
  const [confirmQuitar, setConfirmQuitar] = useState<AsignacionListItem | null>(null);

  // Nombre de la ubicación resuelto en el FE (el DTO trae solo ubicacionId).
  // Catálogo chico (eager); el padre distingue las "ÚNICA".
  const ubicacionesQuery = useUbicaciones({ limit: 500 });
  const ubicacionesMap = useMemo(
    () => mapById(ubicacionesQuery.data?.items),
    [ubicacionesQuery.data],
  );

  const query = useAsignacionesList({
    articuloId: search.articuloId,
    estatus: search.estatus,
    limit: 500,
  });

  const desasignar = useDesasignar();

  function actualizarSearch(parcial: Partial<AsignacionesSearch>) {
    navigate({ to: '/almacen/asignaciones', search: { ...search, ...parcial } });
  }

  function resolverUbicacion(item: AsignacionListItem): string {
    const u = ubicacionesMap.get(item.ubicacionId);
    return u ? rutaUbicacion(u) : item.ubicacionId;
  }

  function confirmarQuitar(item: AsignacionListItem) {
    desasignar.mutate(
      { id: item.id, idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: () => {
          toast.success('Artículo quitado de la ubicación');
          setConfirmQuitar(null);
        },
        onError: (error) => {
          // Guardrail: existencia > 0 → suavizado a "quitar" (ver copy PR C).
          if (esApiError(error) && error.code === 'ASIGNACION_EN_USO_CON_SALDO') {
            toast.error(
              'No se puede quitar: la ubicación tiene existencia del artículo. Agótala primero.',
            );
            return;
          }
          toast.error(esApiError(error) ? error.problem.title : 'No se pudo quitar la asignación.');
        },
      },
    );
  }

  // Atajo con retorno: solo si se llegó por el sentinel `desde=articulo` (no
  // por filtrado manual) y hay artículo al cual volver. El estado del retorno
  // vive solo en la URL — el click limpia los query params.
  const volverAlArticulo =
    search.desde === 'articulo' && search.articuloId
      ? { articuloId: search.articuloId, etiqueta: search.articuloEtiqueta }
      : null;

  return (
    <div className="space-y-5">
      {volverAlArticulo && (
        <Button
          variant="ghost"
          size="sm"
          className="-ml-2 h-auto py-1 text-ink-muted"
          onClick={() =>
            navigate({
              to: '/admin/datos-maestros/articulos/$id',
              params: { id: volverAlArticulo.articuloId },
            })
          }
        >
          <ArrowLeft className="mr-1 size-3.5" />
          {volverAlArticulo.etiqueta
            ? `Volver a ${volverAlArticulo.etiqueta}`
            : 'Volver al artículo'}
        </Button>
      )}

      <PageHeader
        titulo="Ubicación de artículos"
        descripcion="Define en qué ubicación vive cada artículo."
        acciones={
          <>
            {puedeAdministrar && (
              <Button onClick={() => setSheetAbierto(true)}>
                <Plus className="mr-2 size-3.5" />
                Asignar artículo
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
        filtros={<FiltrosToolbar search={search} onChange={actualizarSearch} />}
      >
        {query.isError ? (
          <ErrorState
            title="No se pudieron cargar las asignaciones"
            problem={esApiError(query.error) ? query.error.problem : undefined}
            onRetry={() => query.refetch()}
          />
        ) : query.isLoading ? (
          <TableSkeleton
            rows={6}
            columns={[{ width: 'w-64' }, { width: 'w-56' }, { width: 'w-20' }, { width: 'w-16' }]}
          />
        ) : (query.data?.items ?? []).length === 0 ? (
          <EmptyState
            title="Sin asignaciones"
            description={
              puedeAdministrar
                ? 'Ningún artículo asignado a una ubicación todavía. Usa "Asignar artículo" para empezar.'
                : 'Ningún artículo asignado a una ubicación todavía.'
            }
          />
        ) : (
          <TablaAsignaciones
            items={query.data!.items}
            resolverUbicacion={resolverUbicacion}
            puedeAdministrar={puedeAdministrar}
            onQuitar={setConfirmQuitar}
          />
        )}
      </ListadoModulo>

      <AsignacionSheet open={sheetAbierto} onOpenChange={setSheetAbierto} />

      <AlertDialog
        open={confirmQuitar != null}
        onOpenChange={(open) => {
          if (!open) setConfirmQuitar(null);
        }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Quitar de la ubicación</AlertDialogTitle>
            <AlertDialogDescription>
              ¿Confirmas quitar este artículo de la ubicación? Solo se puede si no tiene existencia
              ahí. La acción es reversible (volver a asignarlo).
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={desasignar.isPending}>Cancelar</AlertDialogCancel>
            <AlertDialogAction
              onClick={() => {
                if (confirmQuitar != null) confirmarQuitar(confirmQuitar);
              }}
              disabled={desasignar.isPending}
            >
              {desasignar.isPending ? 'Quitando…' : 'Quitar'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}

interface FiltrosToolbarProps {
  search: AsignacionesSearch;
  onChange: (parcial: Partial<AsignacionesSearch>) => void;
}

function FiltrosToolbar({ search, onChange }: FiltrosToolbarProps) {
  const hayFiltros = search.articuloId != null || search.estatus != null;
  return (
    <BarraFiltros
      filtros={
        <div className="flex flex-wrap items-end gap-3">
          <div className="space-y-1">
            <label className="text-xs text-ink-muted">Artículo</label>
            <div className="w-64">
              <ArticuloSelector
                value={search.articuloId ?? null}
                onChange={(id) => onChange({ articuloId: id ?? undefined })}
              />
            </div>
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
              </SelectContent>
            </Select>
          </div>
          {hayFiltros && (
            <Button
              variant="ghost"
              onClick={() => onChange({ articuloId: undefined, estatus: undefined })}
            >
              Limpiar filtros
            </Button>
          )}
        </div>
      }
    />
  );
}

interface TablaAsignacionesProps {
  items: readonly AsignacionListItem[];
  resolverUbicacion: (item: AsignacionListItem) => string;
  puedeAdministrar: boolean;
  onQuitar: (item: AsignacionListItem) => void;
}

function TablaAsignaciones({
  items,
  resolverUbicacion,
  puedeAdministrar,
  onQuitar,
}: TablaAsignacionesProps) {
  const [sort, setSort] = useState<SortState<SortKey> | null>(null);

  const enriquecidos = useMemo(
    () =>
      items.map((item) => ({
        item,
        articulo:
          [item.articuloClave, item.articuloDescripcion].filter(Boolean).join(' · ') ||
          item.articuloId,
        ubicacion: resolverUbicacion(item),
        estatus: item.estatus,
      })),
    [items, resolverUbicacion],
  );

  const ordenados = useMemo(() => {
    if (sort == null) return enriquecidos;
    return [...enriquecidos].sort((a, b) => compareItemsBy(a, b, sort.key, sort.dir));
  }, [enriquecidos, sort]);

  const paginacion = usePaginacionLocal(ordenados, JSON.stringify(ordenados.map((r) => r.item.id)));
  return (
    <>
      <div className="overflow-x-auto rounded-md border">
        <Table className="w-full text-sm">
          <TableHeader className="bg-surface-muted/50">
            <TableRow>
              <TableHead className="px-3 py-2 text-left">
                <SortableHeader
                  columnKey="articulo"
                  label="Artículo"
                  current={sort}
                  onSortChange={setSort}
                />
              </TableHead>
              <TableHead className="px-3 py-2 text-left">
                <SortableHeader
                  columnKey="ubicacion"
                  label="Ubicación"
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
            {paginacion.visibles.map(({ item, articulo, ubicacion }) => (
              <TableRow key={item.id} className="border-t">
                <TableCell className="px-3 py-2">{articulo}</TableCell>
                <TableCell className="px-3 py-2 font-mono text-xs">{ubicacion}</TableCell>
                <TableCell className="px-3 py-2">
                  <EstadoAlmacenBadge tipo="catalogo" estado={item.estatus} />
                </TableCell>
                {puedeAdministrar && (
                  <TableCell className="px-3 py-2 text-right">
                    {item.estatus === EstatusCatalogo.Activo && (
                      <Button
                        type="button"
                        size="sm"
                        variant="ghost"
                        onClick={() => onQuitar(item)}
                        aria-label={`Quitar ${articulo} de la ubicación`}
                        title="Quitar el artículo de esta ubicación"
                      >
                        <Ban className="h-4 w-4" />
                      </Button>
                    )}
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
