import { useMemo, useState } from 'react';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { Ban, Pencil, Plus, RotateCcw } from 'lucide-react';
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
  EmptyState,
  ErrorState,
  SortableHeader,
  TableSkeleton,
  compareItemsBy,
  type SortState,
} from '@/components/erp';
import { SubAlmacenSelector } from '@/components/erp/selectors/SubAlmacenSelector';
import {
  useDesactivarUbicacion,
  useReactivarUbicacion,
  useUbicaciones,
} from '@/features/almacen/api/useUbicaciones';
import {
  EstatusCatalogo,
  EstatusCatalogoLabels,
  type UbicacionListItem,
} from '@/features/almacen/api/types';
import type { UbicacionesSearch } from '@/features/almacen/lib/catalogo-search-schemas';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { UbicacionSheet } from '@/features/almacen/components/UbicacionSheet';
import { cn } from '@/lib/utils';

const FROM = '/_app/almacen/ubicaciones' as const;
const SENTINEL_ALL = '__all__';

type SortKey = 'subAlmacen' | 'clave' | 'nombre' | 'estatus';

/**
 * <c>P2 — Bandeja de ubicaciones N4</c> (racks/pasillos, ADR-0047 PR C7.1). El
 * admin / rol de almacén crea la estructura física real. Filtros: sub-almacén y
 * estatus. La ubicación "ÚNICA" (<c>esDefault</c>) va protegida: se puede editar
 * su clave/nombre pero NO desactivar (es el destino del trigger de saldos hasta
 * C7.2). Gateada por <c>almacen.ubicaciones.leer</c>; crear/editar/desactivar
 * requiere <c>almacen.ubicaciones.administrar</c>.
 */
export function UbicacionesPage() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const puedeAdministrar = useHasPermission(
    PermisosCanonicos.AlmacenUbicacionesAdministrar,
  );

  const [sheetAbierto, setSheetAbierto] = useState(false);
  const [editando, setEditando] = useState<UbicacionListItem | null>(null);
  const [confirmDesactivar, setConfirmDesactivar] =
    useState<UbicacionListItem | null>(null);

  const query = useUbicaciones({
    subAlmacenId: search.subAlmacenId,
    estatus: search.estatus,
    limit: 500,
  });

  const desactivar = useDesactivarUbicacion();
  const reactivar = useReactivarUbicacion();

  function actualizarSearch(parcial: Partial<UbicacionesSearch>) {
    navigate({ to: '/almacen/ubicaciones', search: { ...search, ...parcial } });
  }

  function abrirCrear() {
    setEditando(null);
    setSheetAbierto(true);
  }

  function abrirEditar(u: UbicacionListItem) {
    setEditando(u);
    setSheetAbierto(true);
  }

  function confirmarDesactivar(u: UbicacionListItem) {
    desactivar.mutate(
      { id: u.id, idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: () => {
          toast.success('Ubicación desactivada');
          setConfirmDesactivar(null);
        },
        onError: (error) => {
          if (esApiError(error) && error.code === 'UBICACION_EN_USO_CON_SALDO') {
            toast.error(
              'No se puede desactivar: la ubicación tiene existencia. Agótala primero.',
            );
            return;
          }
          if (
            esApiError(error) &&
            error.code === 'UBICACION_DEFAULT_NO_DESACTIVABLE'
          ) {
            toast.error(
              'No se puede desactivar la ubicación default del sub-almacén.',
            );
            return;
          }
          toast.error(
            esApiError(error)
              ? error.problem.title
              : 'No se pudo desactivar la ubicación.',
          );
        },
      },
    );
  }

  function onReactivar(u: UbicacionListItem) {
    reactivar.mutate(
      { id: u.id, idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: () => toast.success('Ubicación reactivada'),
        onError: (error) =>
          toast.error(
            esApiError(error)
              ? error.problem.title
              : 'No se pudo reactivar la ubicación.',
          ),
      },
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Ubicaciones</h1>
          <p className="text-sm text-muted-foreground">
            Ubicaciones físicas (racks, pasillos) dentro de cada sub-almacén.
          </p>
        </div>
        {puedeAdministrar && (
          <Button onClick={abrirCrear}>
            <Plus className="mr-2 h-4 w-4" />
            Nueva ubicación
          </Button>
        )}
      </div>

      <FiltrosToolbar search={search} onChange={actualizarSearch} />

      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar las ubicaciones"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={6}
          columns={[
            { width: 'w-48' },
            { width: 'w-32' },
            { width: 'w-64' },
            { width: 'w-24' },
            { width: 'w-20' },
          ]}
        />
      ) : (query.data?.items ?? []).length === 0 ? (
        <EmptyState
          title="Sin ubicaciones"
          description={
            puedeAdministrar
              ? 'No hay ubicaciones que coincidan con los filtros. Usa "Nueva ubicación" para crear una.'
              : 'No hay ubicaciones registradas.'
          }
        />
      ) : (
        <TablaUbicaciones
          items={query.data!.items}
          puedeAdministrar={puedeAdministrar}
          onEditar={abrirEditar}
          onDesactivar={setConfirmDesactivar}
          onReactivar={onReactivar}
          reactivarPendiente={reactivar.isPending}
        />
      )}

      <UbicacionSheet
        open={sheetAbierto}
        onOpenChange={setSheetAbierto}
        editando={editando}
        subAlmacenIdInicial={search.subAlmacenId}
      />

      <AlertDialog
        open={confirmDesactivar != null}
        onOpenChange={(open) => {
          if (!open) setConfirmDesactivar(null);
        }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Desactivar ubicación</AlertDialogTitle>
            <AlertDialogDescription>
              ¿Confirmas desactivar esta ubicación? Solo se puede si no tiene
              existencia de ningún artículo. La acción es reversible
              (reactivarla después).
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={desactivar.isPending}>
              Cancelar
            </AlertDialogCancel>
            <AlertDialogAction
              onClick={() => {
                if (confirmDesactivar != null)
                  confirmarDesactivar(confirmDesactivar);
              }}
              disabled={desactivar.isPending}
            >
              {desactivar.isPending ? 'Desactivando…' : 'Desactivar'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}

interface FiltrosToolbarProps {
  search: UbicacionesSearch;
  onChange: (parcial: Partial<UbicacionesSearch>) => void;
}

function FiltrosToolbar({ search, onChange }: FiltrosToolbarProps) {
  const hayFiltros = search.subAlmacenId != null || search.estatus != null;
  return (
    <div className="flex flex-wrap items-end gap-3">
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Sub-almacén</label>
        <div className="w-72">
          <SubAlmacenSelector
            value={search.subAlmacenId ?? null}
            onChange={(id) => onChange({ subAlmacenId: id ?? undefined })}
            placeholder="Todos los sub-almacenes"
          />
        </div>
      </div>
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Estatus</label>
        <Select
          value={search.estatus != null ? String(search.estatus) : SENTINEL_ALL}
          onValueChange={(v) =>
            onChange({
              estatus:
                v === SENTINEL_ALL ? undefined : (Number(v) as EstatusCatalogo),
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
          onClick={() =>
            onChange({ subAlmacenId: undefined, estatus: undefined })
          }
        >
          Limpiar filtros
        </Button>
      )}
    </div>
  );
}

interface TablaUbicacionesProps {
  items: readonly UbicacionListItem[];
  puedeAdministrar: boolean;
  onEditar: (u: UbicacionListItem) => void;
  onDesactivar: (u: UbicacionListItem) => void;
  onReactivar: (u: UbicacionListItem) => void;
  reactivarPendiente: boolean;
}

function TablaUbicaciones({
  items,
  puedeAdministrar,
  onEditar,
  onDesactivar,
  onReactivar,
  reactivarPendiente,
}: TablaUbicacionesProps) {
  const [sort, setSort] = useState<SortState<SortKey> | null>(null);

  const enriquecidos = useMemo(
    () =>
      items.map((u) => ({
        ...u,
        subAlmacen: `${u.almacenClave} › ${u.subAlmacenClave}`,
      })),
    [items],
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
          {ordenados.map((u) => (
            <tr key={u.id} className="border-t">
              <td className="px-3 py-2 font-mono text-xs">{u.subAlmacen}</td>
              <td className="px-3 py-2">
                <span className="font-mono text-xs">{u.clave}</span>
                {u.esDefault && (
                  <span className="ml-2 inline-flex items-center rounded-full bg-sky-100 px-2 py-0.5 text-[10px] font-medium text-sky-800">
                    default de enrutamiento
                  </span>
                )}
              </td>
              <td className="px-3 py-2">{u.nombre}</td>
              <td className="px-3 py-2">
                <EstatusBadge estatus={u.estatus} />
              </td>
              {puedeAdministrar && (
                <td className="px-3 py-2 text-right">
                  <div className="flex items-center justify-end gap-1">
                    <Button
                      type="button"
                      size="sm"
                      variant="ghost"
                      onClick={() => onEditar(u)}
                      aria-label={`Editar ubicación ${u.clave}`}
                      title="Editar clave/nombre"
                    >
                      <Pencil className="h-4 w-4" />
                    </Button>
                    {/* La ÚNICA (default) se blinda: sin desactivar (destino del
                        trigger de saldos). Editar clave/nombre sí. */}
                    {!u.esDefault &&
                      u.estatus === EstatusCatalogo.Activo && (
                        <Button
                          type="button"
                          size="sm"
                          variant="ghost"
                          onClick={() => onDesactivar(u)}
                          aria-label={`Desactivar ubicación ${u.clave}`}
                          title="Desactivar (bloqueado si tiene existencia)"
                        >
                          <Ban className="h-4 w-4" />
                        </Button>
                      )}
                    {u.estatus === EstatusCatalogo.Inactivo && (
                      <Button
                        type="button"
                        size="sm"
                        variant="ghost"
                        onClick={() => onReactivar(u)}
                        disabled={reactivarPendiente}
                        aria-label={`Reactivar ubicación ${u.clave}`}
                        title="Reactivar"
                      >
                        <RotateCcw className="h-4 w-4" />
                      </Button>
                    )}
                  </div>
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
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
        estatus === EstatusCatalogo.Activo && 'bg-emerald-100 text-emerald-800',
        estatus === EstatusCatalogo.Inactivo && 'bg-slate-200 text-slate-700',
        estatus === EstatusCatalogo.Borrador && 'bg-amber-100 text-amber-800',
      )}
    >
      {EstatusCatalogoLabels[estatus]}
    </span>
  );
}
