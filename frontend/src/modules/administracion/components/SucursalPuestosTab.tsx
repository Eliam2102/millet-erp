import { useMemo, useState } from 'react';
import { Plus, PowerOff, RotateCcw } from 'lucide-react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
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
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Skeleton } from '@/components/ui/skeleton';
import { usePuestos, useDepartamentos } from '@/features/catalogos/api';
import { EstatusCatalogo } from '@/modules/administracion/api/types';
import type { SucursalPuestoResponse } from '@/modules/administracion/api/types';
import {
  useAsignarPuestoASucursal,
  usePuestosDeSucursal,
  useDesactivarAsignacionSucursalPuesto,
  useReactivarAsignacionSucursalPuesto,
} from '@/modules/administracion/api';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import { SucursalTabErrorState } from '@/modules/administracion/components/SucursalTabErrorState';

/**
 * Tab "Puestos" de <c>SucursalDetalle</c> (F1-ADM-01).
 * Cruza el catálogo global de puestos con las asignaciones de ESTA sucursal.
 * Cada asignación a sucursal se asocia explícitamente a un departamento activo.
 */
export interface SucursalPuestosTabProps {
  sucursalId: string;
  canGestionar: boolean;
}

interface RowData {
  puestoId: string;
  clave: string;
  nombre: string;
  departamentoCatalogoId?: string | null;
  departamentoCatalogoNombre?: string | null;
  departamentoEnSucursalId?: string | null;
  departamentoEnSucursalNombre?: string | null;
  estatusEnSucursal: EstatusCatalogo | null;
}

export function SucursalPuestosTab({
  sucursalId,
  canGestionar,
}: SucursalPuestosTabProps) {
  const catalogoQuery = usePuestos();
  const asignadosQuery = usePuestosDeSucursal(sucursalId);
  const departamentosQuery = useDepartamentos();

  const [puestoAAsignar, setPuestoAAsignar] = useState<RowData | null>(null);

  const deptosActivos = useMemo(() => {
    return (departamentosQuery.data?.items ?? []).filter(
      (d) => d.estatus === EstatusCatalogo.Activo,
    );
  }, [departamentosQuery.data]);

  const rows = useMemo<RowData[]>(() => {
    const catalogo = catalogoQuery.data?.items ?? [];
    const asignados = asignadosQuery.data?.items ?? [];
    const mapAsig = new Map<string, SucursalPuestoResponse>(
      asignados.map((a) => [a.puestoId, a]),
    );

    return [...catalogo]
      .sort((a, b) => {
        const aAsignado = mapAsig.has(a.id);
        const bAsignado = mapAsig.has(b.id);
        if (aAsignado !== bAsignado) return aAsignado ? -1 : 1;
        return a.clave.localeCompare(b.clave);
      })
      .map((p) => {
        const asig = mapAsig.get(p.id);
        return {
          puestoId: p.id,
          clave: p.clave,
          nombre: p.nombre,
          departamentoCatalogoId: p.departamentoId ?? null,
          departamentoCatalogoNombre: p.departamentoNombre ?? null,
          departamentoEnSucursalId: asig?.departamentoId ?? null,
          departamentoEnSucursalNombre: asig?.departamentoNombre ?? null,
          estatusEnSucursal: (asig?.estatus as EstatusCatalogo) ?? null,
        };
      });
  }, [catalogoQuery.data, asignadosQuery.data]);

  if (asignadosQuery.isError) {
    return (
      <SucursalTabErrorState
        error={asignadosQuery.error}
        onRetry={() => asignadosQuery.refetch()}
      />
    );
  }
  if (catalogoQuery.isError) {
    return (
      <SucursalTabErrorState
        error={catalogoQuery.error}
        onRetry={() => catalogoQuery.refetch()}
      />
    );
  }
  if (departamentosQuery.isError) {
    return (
      <SucursalTabErrorState
        error={departamentosQuery.error}
        onRetry={() => departamentosQuery.refetch()}
      />
    );
  }

  const isLoading =
    catalogoQuery.isLoading ||
    asignadosQuery.isLoading ||
    departamentosQuery.isLoading;

  if (isLoading) {
    return (
      <div className="space-y-2">
        <Skeleton className="h-12 w-full" />
        <Skeleton className="h-12 w-full" />
        <Skeleton className="h-12 w-full" />
      </div>
    );
  }

  if (rows.length === 0) {
    return (
      <div className="rounded-md border border-dashed bg-muted/20 px-4 py-6 text-center text-sm text-muted-foreground">
        El catálogo de puestos está vacío.
      </div>
    );
  }

  return (
    <div className="space-y-3">
      <div className="text-xs text-muted-foreground">
        Total: {rows.length} · Asignados:{' '}
        {rows.filter((r) => r.estatusEnSucursal != null).length}
      </div>
      <ul className="divide-y rounded-md border bg-card">
        {rows.map((row) => (
          <FilaPuesto
            key={row.puestoId}
            sucursalId={sucursalId}
            row={row}
            canGestionar={canGestionar}
            onAsignarClick={() => setPuestoAAsignar(row)}
          />
        ))}
      </ul>

      <AsignarPuestoModal
        open={puestoAAsignar != null}
        onOpenChange={(open) => {
          if (!open) setPuestoAAsignar(null);
        }}
        puesto={puestoAAsignar}
        sucursalId={sucursalId}
        deptosActivos={deptosActivos}
      />
    </div>
  );
}

interface FilaPuestoProps {
  sucursalId: string;
  row: RowData;
  canGestionar: boolean;
  onAsignarClick: () => void;
}

function FilaPuesto({
  sucursalId,
  row,
  canGestionar,
  onAsignarClick,
}: FilaPuestoProps) {
  const idempotencyKey = useFormIdempotencyKey();
  const desactivar = useDesactivarAsignacionSucursalPuesto();
  const reactivar = useReactivarAsignacionSucursalPuesto();
  const [confirmDesactivar, setConfirmDesactivar] = useState(false);

  const noAsignado = row.estatusEnSucursal == null;
  const activa = row.estatusEnSucursal === EstatusCatalogo.Activo;
  const inactiva = row.estatusEnSucursal === EstatusCatalogo.Inactivo;

  function handleConfirmarDesactivar() {
    desactivar.mutate(
      { sucursalId, puestoId: row.puestoId, idempotencyKey },
      {
        onSuccess: () => {
          toast.success(`${row.clave} desactivado`);
          setConfirmDesactivar(false);
        },
        onError: (error) => {
          handleError(`desactivar ${row.clave}`)(error);
          setConfirmDesactivar(false);
        },
      },
    );
  }

  function handleReactivar() {
    reactivar.mutate(
      { sucursalId, puestoId: row.puestoId, idempotencyKey },
      {
        onSuccess: () => toast.success(`${row.clave} reactivado`),
        onError: handleError(`reactivar ${row.clave}`),
      },
    );
  }

  const pending = desactivar.isPending || reactivar.isPending;

  return (
    <li className="px-3 py-2">
      <div className="flex flex-wrap items-center gap-3">
        <span className="font-mono text-sm font-semibold">{row.clave}</span>
        <div className="flex flex-1 flex-col truncate">
          <span className="truncate text-sm font-medium">{row.nombre}</span>
          {row.estatusEnSucursal != null ? (
            <span className="text-xs text-muted-foreground">
              Depto sucursal:{' '}
              <span className="font-medium text-foreground">
                {row.departamentoEnSucursalNombre ?? 'Sin departamento'}
              </span>
            </span>
          ) : row.departamentoCatalogoNombre ? (
            <span className="text-xs text-muted-foreground">
              Depto sugerido en catálogo:{' '}
              <span className="font-medium text-muted-foreground">
                {row.departamentoCatalogoNombre}
              </span>
            </span>
          ) : (
            <span className="text-xs italic text-muted-foreground/60">
              Sin departamento en catálogo
            </span>
          )}
        </div>
        {activa && <Badge variant="secondary">Activa</Badge>}
        {inactiva && (
          <Badge variant="outline" className="text-muted-foreground">
            Inactiva
          </Badge>
        )}
        {noAsignado && (
          <Badge variant="outline" className="text-muted-foreground">
            Sin asignar
          </Badge>
        )}

        {canGestionar && noAsignado && (
          <Button
            variant="ghost"
            size="sm"
            disabled={pending}
            onClick={onAsignarClick}
            aria-label={`Asignar puesto ${row.clave}`}
          >
            <Plus className="mr-1 h-3.5 w-3.5" /> Asignar
          </Button>
        )}
        {canGestionar && activa && (
          <Button
            variant="ghost"
            size="sm"
            disabled={pending}
            onClick={() => setConfirmDesactivar(true)}
            aria-label={`Desactivar puesto ${row.clave}`}
          >
            <PowerOff className="h-3.5 w-3.5" />
          </Button>
        )}
        {canGestionar && inactiva && (
          <Button
            variant="ghost"
            size="sm"
            disabled={pending}
            onClick={handleReactivar}
            aria-label={`Reactivar puesto ${row.clave}`}
          >
            <RotateCcw className="mr-1 h-3.5 w-3.5" /> Reactivar
          </Button>
        )}
      </div>

      <AlertDialog
        open={confirmDesactivar}
        onOpenChange={(open) => {
          if (!open) setConfirmDesactivar(false);
        }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Desactivar puesto</AlertDialogTitle>
            <AlertDialogDescription>
              ¿Confirmas desactivar{' '}
              <span className="font-mono font-semibold">{row.clave}</span> en
              esta sucursal? Bloquea nuevas asignaciones con esta combinación pero
              NO afecta las existentes.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={desactivar.isPending}>
              Cancelar
            </AlertDialogCancel>
            <AlertDialogAction
              onClick={handleConfirmarDesactivar}
              disabled={desactivar.isPending}
            >
              {desactivar.isPending ? 'Desactivando…' : 'Desactivar'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </li>
  );
}

interface AsignarPuestoModalProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  puesto: RowData | null;
  sucursalId: string;
  deptosActivos: Array<{ id: string; clave: string; nombre: string }>;
}

function AsignarPuestoModal({
  open,
  onOpenChange,
  puesto,
  sucursalId,
  deptosActivos,
}: AsignarPuestoModalProps) {
  const idempotencyKey = useFormIdempotencyKey();
  const asignar = useAsignarPuestoASucursal();
  // Selección manual del admin, ligada al puesto para que se descarte al
  // abrir el modal con otro puesto.
  const [seleccion, setSeleccion] = useState<{
    puestoId: string;
    departamentoId: string;
  } | null>(null);

  // Prioridad: depto sugerido de catálogo maestro si coincide con los activos
  const departamentoSugerido = useMemo(() => {
    if (!puesto) return '';
    const coincideCatalogo =
      puesto.departamentoCatalogoId &&
      deptosActivos.some((d) => d.id === puesto.departamentoCatalogoId);
    if (coincideCatalogo && puesto.departamentoCatalogoId) {
      return puesto.departamentoCatalogoId;
    }
    return deptosActivos[0]?.id ?? '';
  }, [puesto, deptosActivos]);

  const departamentoId =
    seleccion && seleccion.puestoId === puesto?.puestoId
      ? seleccion.departamentoId
      : departamentoSugerido;

  function setDepartamentoId(id: string) {
    if (!puesto) return;
    setSeleccion({ puestoId: puesto.puestoId, departamentoId: id });
  }

  function handleOpenChange(next: boolean) {
    if (!next) setSeleccion(null);
    onOpenChange(next);
  }

  function handleConfirmar() {
    if (!puesto || !departamentoId) return;

    asignar.mutate(
      {
        sucursalId,
        puestoId: puesto.puestoId,
        departamentoId,
        idempotencyKey,
      },
      {
        onSuccess: () => {
          toast.success(
            `Puesto ${puesto.clave} asignado a la sucursal exitosamente`,
          );
          handleOpenChange(false);
        },
        onError: handleError(`asignar ${puesto.clave}`),
      },
    );
  }

  const sinDeptos = deptosActivos.length === 0;

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Asignar puesto a departamento en sucursal</DialogTitle>
          <DialogDescription>
            Selecciona el departamento al que pertenecerá este puesto dentro de
            la sucursal.
          </DialogDescription>
        </DialogHeader>

        {puesto && (
          <div className="space-y-4 py-2">
            <div className="rounded-md bg-muted/50 p-3 text-sm">
              <div>
                <span className="font-semibold text-foreground">Puesto:</span>{' '}
                <span className="font-mono font-medium">{puesto.clave}</span> —{' '}
                {puesto.nombre}
              </div>
              {puesto.departamentoCatalogoNombre && (
                <div className="mt-1 text-xs text-muted-foreground">
                  Departamento sugerido por catálogo maestro:{' '}
                  <span className="font-medium text-foreground">
                    {puesto.departamentoCatalogoNombre}
                  </span>
                </div>
              )}
            </div>

            {sinDeptos ? (
              <div className="rounded-md border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800 dark:border-amber-900/50 dark:bg-amber-950/30 dark:text-amber-300">
                No hay departamentos activos disponibles en la empresa. Crea o
                activa un departamento antes de asignar puestos a la sucursal.
              </div>
            ) : (
              <div className="space-y-1.5">
                <label
                  htmlFor="depto-sucursal-select"
                  className="text-xs font-semibold uppercase tracking-wider text-foreground"
                >
                  Departamento asignado *
                </label>
                <select
                  id="depto-sucursal-select"
                  className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm shadow-xs focus:outline-hidden focus:ring-2 focus:ring-ring"
                  value={departamentoId}
                  onChange={(e) => setDepartamentoId(e.target.value)}
                  disabled={asignar.isPending}
                >
                  <option value="" disabled>
                    Selecciona un departamento activo...
                  </option>
                  {deptosActivos.map((d) => (
                    <option key={d.id} value={d.id}>
                      {d.clave} — {d.nombre}
                    </option>
                  ))}
                </select>
              </div>
            )}
          </div>
        )}

        <DialogFooter>
          <Button
            variant="outline"
            onClick={() => handleOpenChange(false)}
            disabled={asignar.isPending}
          >
            Cancelar
          </Button>
          <Button
            onClick={handleConfirmar}
            disabled={asignar.isPending || sinDeptos || !departamentoId}
          >
            {asignar.isPending ? 'Asignando…' : 'Asignar puesto'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function handleError(accion: string) {
  return (error: unknown) => {
    if (esApiError(error)) {
      toast.error(error.problem.title, {
        description:
          error.problem.detail ||
          (error.traceId ? `Código: ${error.traceId}` : undefined),
      });
    } else {
      toast.error(`Error al ${accion}.`);
    }
  };
}
