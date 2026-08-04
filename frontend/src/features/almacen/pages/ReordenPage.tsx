import { useMemo, useState } from 'react';
import { useNavigate, useSearch } from '@tanstack/react-router';
import { Ban, Pencil, Plus, Power } from 'lucide-react';
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
import { mapById, useAlmacenes, useSucursales } from '@/features/catalogos/api';
import {
  useReordenList,
  useDesactivarReorden,
} from '@/features/almacen/api/useReorden';
import {
  useActualizarAlmacenSettings,
  useAlmacenSettings,
} from '@/features/almacen/api/useAlmacenSettings';
import {
  EstatusCatalogo,
  EstatusCatalogoLabels,
  NivelReorden,
  NivelReordenLabels,
  ObjetivoReposicion,
  type ConfiguracionReordenListItem,
} from '@/features/almacen/api/types';
import type { ReordenSearch } from '@/features/almacen/lib/catalogo-search-schemas';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { ReordenSheet } from '@/features/almacen/components/ReordenSheet';
import { cn } from '@/lib/utils';

const FROM = '/_app/almacen/reorden' as const;
const SENTINEL_ALL = '__all__';

type SortKey = 'articulo' | 'nivel' | 'entidad' | 'estatus';

/**
 * <c>P1 — Bandeja de reabasto</c> (código = reorden, ADR-0047 PR5.A/5.F).
 * Tabla con filtros por artículo, nivel y estatus. "Nuevo reabasto" abre
 * <c>ReordenSheet</c>; el lápiz abre el mismo sheet en edición; el botón de
 * bloqueo desactiva la configuración. Gateada por <c>almacen.reorden.leer</c>;
 * crear/editar/desactivar requiere <c>almacen.reorden.administrar</c>.
 */
export function ReordenPage() {
  const search = useSearch({ from: FROM });
  const navigate = useNavigate();
  const puedeAdministrar = useHasPermission(
    PermisosCanonicos.AlmacenReordenAdministrar,
  );

  const [sheetAbierto, setSheetAbierto] = useState(false);
  const [editando, setEditando] = useState<ConfiguracionReordenListItem | null>(
    null,
  );
  const [confirmDesactivar, setConfirmDesactivar] =
    useState<ConfiguracionReordenListItem | null>(null);
  // Confirmación del interruptor del motor: guarda el valor destino
  // (true=prender, false=apagar) mientras el diálogo está abierto.
  const [confirmMotor, setConfirmMotor] = useState<boolean | null>(null);

  const settingsQuery = useAlmacenSettings();
  const actualizarSettings = useActualizarAlmacenSettings();

  // Nombre de la entidad (sucursal N1 / almacén N2) resuelto en el FE:
  // el backend solo manda el id crudo. Ambos catálogos son chicos (eager).
  const sucursalesQuery = useSucursales();
  const almacenesQuery = useAlmacenes();
  const sucursalesMap = useMemo(
    () => mapById(sucursalesQuery.data?.items),
    [sucursalesQuery.data],
  );
  const almacenesMap = useMemo(
    () => mapById(almacenesQuery.data?.items),
    [almacenesQuery.data],
  );

  const query = useReordenList({
    articuloId: search.articuloId,
    nivel: search.nivel,
    estatus: search.estatus,
    limit: 500,
  });

  const desactivar = useDesactivarReorden();

  function actualizarSearch(parcial: Partial<ReordenSearch>) {
    navigate({ to: '/almacen/reorden', search: { ...search, ...parcial } });
  }

  function resolverEntidad(item: ConfiguracionReordenListItem): string {
    const mapa =
      item.nivel === NivelReorden.Sucursal ? sucursalesMap : almacenesMap;
    return mapa.get(item.entidadId)?.nombre ?? item.entidadId;
  }

  function confirmarCambioMotor(valor: boolean) {
    actualizarSettings.mutate(
      { reabastoAutomaticoActivo: valor, idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: () => {
          toast.success(
            valor
              ? 'Reabasto automático encendido — surte efecto en el siguiente ciclo del motor'
              : 'Reabasto automático apagado — surte efecto en el siguiente ciclo del motor',
          );
          setConfirmMotor(null);
        },
        onError: (error) => {
          toast.error(
            esApiError(error)
              ? error.problem.title
              : 'No se pudo cambiar el reabasto automático.',
          );
        },
      },
    );
  }

  function confirmarDesactivar(item: ConfiguracionReordenListItem) {
    desactivar.mutate(
      { id: item.id, idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: () => {
          toast.success('Reabasto desactivado');
          setConfirmDesactivar(null);
        },
        onError: (error) => {
          toast.error(
            esApiError(error)
              ? error.problem.title
              : 'No se pudo desactivar el reabasto.',
          );
        },
      },
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Reabasto</h1>
          <p className="text-sm text-muted-foreground">
            Puntos de reabasto por artículo en una sucursal o almacén. El motor
            propone requisiciones cuando la existencia cae por debajo del
            objetivo.
          </p>
        </div>
        {puedeAdministrar && (
          <Button
            onClick={() => {
              setEditando(null);
              setSheetAbierto(true);
            }}
          >
            <Plus className="mr-2 h-4 w-4" />
            Nuevo reabasto
          </Button>
        )}
      </div>

      {/* Interruptor operativo del motor de reorden (AlmacenSettings).
          El estado lo ve quien puede leer la bandeja; cambiarlo exige
          almacen.reorden.administrar. */}
      {settingsQuery.data && (
        <div
          className={cn(
            'flex flex-wrap items-center justify-between gap-3 rounded-md border px-4 py-3',
            settingsQuery.data.reabastoAutomaticoActivo
              ? 'border-emerald-300 bg-emerald-50'
              : 'border-amber-300 bg-amber-50',
          )}
        >
          <div className="flex items-center gap-3">
            <Power
              className={cn(
                'h-5 w-5 shrink-0',
                settingsQuery.data.reabastoAutomaticoActivo
                  ? 'text-emerald-700'
                  : 'text-amber-700',
              )}
            />
            <div>
              <p className="text-sm font-semibold">
                Reabasto automático:{' '}
                {settingsQuery.data.reabastoAutomaticoActivo
                  ? 'Encendido'
                  : 'Apagado'}
              </p>
              <p className="text-xs text-muted-foreground">
                {settingsQuery.data.reabastoAutomaticoActivo
                  ? 'El motor genera borradores de requisición con estas configuraciones en cada ciclo.'
                  : 'El motor no genera borradores. Las configuraciones de esta bandeja se conservan.'}
              </p>
            </div>
          </div>
          {puedeAdministrar && (
            <Button
              type="button"
              variant={
                settingsQuery.data.reabastoAutomaticoActivo
                  ? 'outline'
                  : 'default'
              }
              onClick={() =>
                setConfirmMotor(!settingsQuery.data!.reabastoAutomaticoActivo)
              }
            >
              {settingsQuery.data.reabastoAutomaticoActivo
                ? 'Apagar'
                : 'Encender'}
            </Button>
          )}
        </div>
      )}

      <FiltrosToolbar search={search} onChange={actualizarSearch} />

      {query.isError ? (
        <ErrorState
          title="No se pudo cargar el reabasto"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={6}
          columns={[
            { width: 'w-64' },
            { width: 'w-24' },
            { width: 'w-40' },
            { width: 'w-16' },
            { width: 'w-16' },
            { width: 'w-20' },
            { width: 'w-16' },
            { width: 'w-20' },
          ]}
        />
      ) : (query.data?.items ?? []).length === 0 ? (
        <EmptyState
          title="Sin configuraciones de reabasto"
          description={
            puedeAdministrar
              ? 'No hay reabastos que coincidan con los filtros. Usa "Nuevo reabasto" para crear el primero.'
              : 'No hay reabastos configurados.'
          }
        />
      ) : (
        <TablaReorden
          items={query.data!.items}
          resolverEntidad={resolverEntidad}
          puedeAdministrar={puedeAdministrar}
          onEditar={(item) => {
            setEditando(item);
            setSheetAbierto(true);
          }}
          onDesactivar={setConfirmDesactivar}
        />
      )}

      <ReordenSheet
        open={sheetAbierto}
        onOpenChange={setSheetAbierto}
        editando={editando}
      />

      <AlertDialog
        open={confirmDesactivar != null}
        onOpenChange={(open) => {
          if (!open) setConfirmDesactivar(null);
        }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Desactivar reabasto</AlertDialogTitle>
            <AlertDialogDescription>
              ¿Confirmas desactivar este reabasto? El motor dejará de proponer
              requisiciones para esta llave. La acción es reversible (volver a
              crearlo lo reactiva).
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={desactivar.isPending}>
              Cancelar
            </AlertDialogCancel>
            <AlertDialogAction
              onClick={() => {
                if (confirmDesactivar != null) {
                  confirmarDesactivar(confirmDesactivar);
                }
              }}
              disabled={desactivar.isPending}
            >
              {desactivar.isPending ? 'Desactivando…' : 'Desactivar'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      <AlertDialog
        open={confirmMotor != null}
        onOpenChange={(open) => {
          if (!open) setConfirmMotor(null);
        }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>
              {confirmMotor
                ? '¿Encender el reabasto automático?'
                : '¿Apagar el reabasto automático?'}
            </AlertDialogTitle>
            <AlertDialogDescription>
              {confirmMotor
                ? 'El motor comenzará a generar borradores de requisición con las ' +
                  'configuraciones de esta bandeja. El cambio puede tardar hasta ' +
                  '1 hora en surtir efecto (el siguiente ciclo del motor).'
                : 'El motor dejará de generar borradores de requisición. El cambio ' +
                  'puede tardar hasta 1 hora en surtir efecto (un ciclo ya iniciado ' +
                  'termina). Los borradores ya generados quedan vivos como ' +
                  'requisiciones normales; cancélalos en Compras si no proceden.'}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={actualizarSettings.isPending}>
              Cancelar
            </AlertDialogCancel>
            <AlertDialogAction
              onClick={() => {
                if (confirmMotor != null) {
                  confirmarCambioMotor(confirmMotor);
                }
              }}
              disabled={actualizarSettings.isPending}
            >
              {actualizarSettings.isPending
                ? 'Aplicando…'
                : confirmMotor
                  ? 'Encender'
                  : 'Apagar'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}

interface FiltrosToolbarProps {
  search: ReordenSearch;
  onChange: (parcial: Partial<ReordenSearch>) => void;
}

function FiltrosToolbar({ search, onChange }: FiltrosToolbarProps) {
  const hayFiltros =
    search.articuloId != null || search.nivel != null || search.estatus != null;
  return (
    <div className="flex flex-wrap items-end gap-3">
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Artículo</label>
        <div className="w-64">
          <ArticuloSelector
            value={search.articuloId ?? null}
            onChange={(id) => onChange({ articuloId: id ?? undefined })}
          />
        </div>
      </div>
      <div className="space-y-1">
        <label className="text-xs text-muted-foreground">Nivel</label>
        <Select
          value={search.nivel != null ? String(search.nivel) : SENTINEL_ALL}
          onValueChange={(v) =>
            onChange({
              nivel:
                v === SENTINEL_ALL ? undefined : (Number(v) as NivelReorden),
            })
          }
        >
          <SelectTrigger className="w-44">
            <SelectValue placeholder="Todos" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={SENTINEL_ALL}>Todos</SelectItem>
            <SelectItem value={String(NivelReorden.Sucursal)}>
              {NivelReordenLabels[NivelReorden.Sucursal]}
            </SelectItem>
            <SelectItem value={String(NivelReorden.Almacen)}>
              {NivelReordenLabels[NivelReorden.Almacen]}
            </SelectItem>
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
          </SelectContent>
        </Select>
      </div>
      {hayFiltros && (
        <Button
          variant="ghost"
          onClick={() =>
            onChange({
              articuloId: undefined,
              nivel: undefined,
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

interface TablaReordenProps {
  items: readonly ConfiguracionReordenListItem[];
  resolverEntidad: (item: ConfiguracionReordenListItem) => string;
  puedeAdministrar: boolean;
  onEditar: (item: ConfiguracionReordenListItem) => void;
  onDesactivar: (item: ConfiguracionReordenListItem) => void;
}

function TablaReorden({
  items,
  resolverEntidad,
  puedeAdministrar,
  onEditar,
  onDesactivar,
}: TablaReordenProps) {
  const [sort, setSort] = useState<SortState<SortKey> | null>(null);

  const enriquecidos = useMemo(
    () =>
      items.map((item) => ({
        item,
        articulo:
          [item.articuloClave, item.articuloDescripcion]
            .filter(Boolean)
            .join(' · ') || item.articuloId,
        nivel: NivelReordenLabels[item.nivel],
        entidad: resolverEntidad(item),
        estatus: item.estatus,
      })),
    [items, resolverEntidad],
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
                columnKey="articulo"
                label="Artículo"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="nivel"
                label="Nivel"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-left">
              <SortableHeader
                columnKey="entidad"
                label="Entidad"
                current={sort}
                onSortChange={setSort}
              />
            </th>
            <th className="px-3 py-2 text-right font-medium">Mín</th>
            <th className="px-3 py-2 text-right font-medium">Máx</th>
            <th className="px-3 py-2 text-right font-medium">P. reorden</th>
            <th className="px-3 py-2 text-center font-medium">Auto</th>
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
          {ordenados.map(({ item, articulo, entidad }) => (
            <tr key={item.id} className="border-t">
              <td className="px-3 py-2">{articulo}</td>
              <td className="px-3 py-2">
                <NivelBadge nivel={item.nivel} />
              </td>
              <td className="px-3 py-2">{entidad}</td>
              <ValorCelda
                valor={item.minimo}
                activo={item.objetivo === ObjetivoReposicion.Minimo}
              />
              <ValorCelda
                valor={item.maximo}
                activo={item.objetivo === ObjetivoReposicion.Maximo}
              />
              <ValorCelda
                valor={item.puntoReorden}
                activo={item.objetivo === ObjetivoReposicion.Reorden}
              />
              <td className="px-3 py-2 text-center">
                {item.autoRequisicion ? 'Sí' : 'No'}
              </td>
              <td className="px-3 py-2">
                <EstatusBadge estatus={item.estatus} />
              </td>
              {puedeAdministrar && (
                <td className="px-3 py-2 text-right">
                  <Button
                    type="button"
                    size="sm"
                    variant="ghost"
                    onClick={() => onEditar(item)}
                    aria-label={`Editar reabasto ${articulo}`}
                  >
                    <Pencil className="h-4 w-4" />
                  </Button>
                  {item.estatus === EstatusCatalogo.Activo && (
                    <Button
                      type="button"
                      size="sm"
                      variant="ghost"
                      onClick={() => onDesactivar(item)}
                      aria-label={`Desactivar reabasto ${articulo}`}
                    >
                      <Ban className="h-4 w-4" />
                    </Button>
                  )}
                </td>
              )}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/** Celda numérica; resalta el valor que dicta el Objetivo (meta activa). */
function ValorCelda({ valor, activo }: { valor: number; activo: boolean }) {
  return (
    <td
      className={cn(
        'px-3 py-2 text-right tabular-nums',
        activo && 'font-semibold text-foreground',
      )}
      title={activo ? 'Objetivo de reabasto' : undefined}
    >
      {valor}
    </td>
  );
}

function NivelBadge({ nivel }: { nivel: NivelReorden }) {
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
        nivel === NivelReorden.Sucursal
          ? 'bg-sky-100 text-sky-800'
          : 'bg-violet-100 text-violet-800',
      )}
    >
      {NivelReordenLabels[nivel]}
    </span>
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
