import { useMemo, useState } from 'react';
import { Briefcase, Plus, PowerOff, RotateCcw, Search } from 'lucide-react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
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
import { usePuestos } from '@/features/catalogos/api';
import { EstatusCatalogo } from '@/modules/administracion/api/types';
import type { SucursalPuestoResponse } from '@/modules/administracion/api/types';
import {
  useAsignarPuestoASucursal,
  usePuestosDeSucursal,
  useDepartamentosDeSucursal,
  useDesactivarAsignacionSucursalPuesto,
  useReactivarAsignacionSucursalPuesto,
} from '@/modules/administracion/api';
import { esApiError } from '@/lib/api';
import { SucursalTabErrorState } from '@/modules/administracion/components/SucursalTabErrorState';

export interface SucursalPuestosTabProps {
  sucursalId: string;
  canGestionar: boolean;
}

export function SucursalPuestosTab({
  sucursalId,
  canGestionar,
}: SucursalPuestosTabProps) {
  const catalogoQuery = usePuestos();
  const asignadosQuery = usePuestosDeSucursal(sucursalId);
  const deptosSucursalQuery = useDepartamentosDeSucursal(sucursalId);

  const [filtro, setFiltro] = useState('');
  const [modalAsignarOpen, setModalAsignarOpen] = useState(false);

  const asignados = asignadosQuery.data?.items ?? [];

  const deptosActivos = useMemo(() => {
    return (deptosSucursalQuery.data?.items ?? [])
      .filter((d) => d.estatus === EstatusCatalogo.Activo)
      .map((d) => ({
        id: d.departamentoId,
        clave: d.departamentoClave,
        nombre: d.departamentoNombre,
      }));
  }, [deptosSucursalQuery.data]);

  const asignadosFiltrados = useMemo(() => {
    const q = filtro.trim().toLowerCase();
    if (!q) return asignados;
    return asignados.filter(
      (p) =>
        p.puestoClave.toLowerCase().includes(q) ||
        p.puestoNombre.toLowerCase().includes(q) ||
        (p.departamentoNombre && p.departamentoNombre.toLowerCase().includes(q)),
    );
  }, [asignados, filtro]);

  if (asignadosQuery.isError) {
    return (
      <SucursalTabErrorState
        error={asignadosQuery.error}
        onRetry={() => asignadosQuery.refetch()}
      />
    );
  }
  if (deptosSucursalQuery.isError) {
    return (
      <SucursalTabErrorState
        error={deptosSucursalQuery.error}
        onRetry={() => deptosSucursalQuery.refetch()}
      />
    );
  }

  const isLoading = asignadosQuery.isLoading || deptosSucursalQuery.isLoading;

  if (isLoading) {
    return (
      <div className="space-y-2">
        <Skeleton className="h-10 w-full" />
        <Skeleton className="h-14 w-full" />
        <Skeleton className="h-14 w-full" />
      </div>
    );
  }

  return (
    <div className="space-y-4">
      {/* Barra superior de herramientas */}
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex items-center gap-2">
          <div className="relative w-full sm:w-72">
            <Search className="absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" />
            <Input
              placeholder="Buscar puesto o departamento..."
              value={filtro}
              onChange={(e) => setFiltro(e.target.value)}
              className="pl-8"
              aria-label="Buscar puestos asignados"
            />
          </div>
          <span className="text-xs text-muted-foreground whitespace-nowrap">
            {asignados.length} {asignados.length === 1 ? 'asignado' : 'asignados'}
          </span>
        </div>

        {canGestionar && (
          <Button
            onClick={() => setModalAsignarOpen(true)}
            size="sm"
            className="self-start sm:self-auto"
          >
            <Plus className="mr-1.5 h-4 w-4" /> Asignar puesto
          </Button>
        )}
      </div>

      {/* Lista de asignados o empty state */}
      {asignados.length === 0 ? (
        <div className="flex flex-col items-center justify-center rounded-lg border border-dashed bg-muted/20 px-6 py-10 text-center">
          <Briefcase className="mb-3 h-10 w-10 text-muted-foreground/60" />
          <h3 className="text-sm font-semibold">No hay puestos asignados</h3>
          <p className="mt-1 text-xs text-muted-foreground max-w-sm">
            Esta sucursal aún no tiene puestos vinculados. Asocia puestos del catálogo
            a un departamento activo de esta sucursal.
          </p>
          {canGestionar && (
            <Button
              variant="outline"
              size="sm"
              className="mt-4"
              onClick={() => setModalAsignarOpen(true)}
            >
              <Plus className="mr-1.5 h-4 w-4" /> Asignar primer puesto
            </Button>
          )}
        </div>
      ) : asignadosFiltrados.length === 0 ? (
        <div className="rounded-md border border-dashed bg-muted/10 px-4 py-8 text-center text-sm text-muted-foreground">
          No se encontraron puestos asignados que coincidan con &ldquo;{filtro}&rdquo;.
        </div>
      ) : (
        <ul className="divide-y rounded-md border bg-card">
          {asignadosFiltrados.map((item) => (
            <FilaPuestoAsignado
              key={item.puestoId}
              sucursalId={sucursalId}
              item={item}
              canGestionar={canGestionar}
            />
          ))}
        </ul>
      )}

      {/* Modal para asignar puestos */}
      <AsignarPuestoModal
        open={modalAsignarOpen}
        onOpenChange={setModalAsignarOpen}
        sucursalId={sucursalId}
        asignados={asignados}
        deptosActivos={deptosActivos}
        catalogoQuery={catalogoQuery}
      />
    </div>
  );
}

interface FilaPuestoAsignadoProps {
  sucursalId: string;
  item: SucursalPuestoResponse;
  canGestionar: boolean;
}

function FilaPuestoAsignado({
  sucursalId,
  item,
  canGestionar,
}: FilaPuestoAsignadoProps) {
  const desactivar = useDesactivarAsignacionSucursalPuesto();
  const reactivar = useReactivarAsignacionSucursalPuesto();
  const [confirmDesactivar, setConfirmDesactivar] = useState(false);

  const activa = item.estatus === EstatusCatalogo.Activo;
  const inactiva = item.estatus === EstatusCatalogo.Inactivo;

  function handleConfirmarDesactivar() {
    desactivar.mutate(
      {
        sucursalId,
        puestoId: item.puestoId,
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => {
          toast.success(`${item.puestoClave} desactivado`);
          setConfirmDesactivar(false);
        },
        onError: (error) => {
          handleError(`desactivar ${item.puestoClave}`)(error);
          setConfirmDesactivar(false);
        },
      },
    );
  }

  function handleReactivar() {
    reactivar.mutate(
      {
        sucursalId,
        puestoId: item.puestoId,
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => toast.success(`${item.puestoClave} reactivado`),
        onError: handleError(`reactivar ${item.puestoClave}`),
      },
    );
  }

  const pending = desactivar.isPending || reactivar.isPending;

  return (
    <li className="flex items-center justify-between gap-3 px-4 py-3">
      <div className="flex flex-wrap items-center gap-3">
        <span className="font-mono text-sm font-semibold text-primary">
          {item.puestoClave}
        </span>
        <span className="text-sm font-medium text-foreground">
          {item.puestoNombre}
        </span>
        <span className="text-xs text-muted-foreground">
          Depto sucursal:{' '}
          <span className="font-medium text-foreground">
            {item.departamentoNombre ?? item.departamentoId}
          </span>
        </span>
        {activa && (
          <Badge variant="secondary" className="bg-emerald-500/10 text-emerald-700 dark:text-emerald-400">
            Activa
          </Badge>
        )}
        {inactiva && (
          <Badge variant="outline" className="text-muted-foreground">
            Inactiva
          </Badge>
        )}
      </div>

      <div className="flex items-center gap-2">
        {canGestionar && activa && (
          <Button
            variant="ghost"
            size="sm"
            disabled={pending}
            onClick={() => setConfirmDesactivar(true)}
            aria-label={`Desactivar puesto ${item.puestoClave}`}
            className="text-muted-foreground hover:text-destructive"
          >
            <PowerOff className="h-4 w-4 mr-1" /> Desactivar
          </Button>
        )}
        {canGestionar && inactiva && (
          <Button
            variant="ghost"
            size="sm"
            disabled={pending}
            onClick={handleReactivar}
            aria-label={`Reactivar puesto ${item.puestoClave}`}
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
              <span className="font-mono font-semibold">{item.puestoClave}</span> en
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
  sucursalId: string;
  asignados: SucursalPuestoResponse[];
  deptosActivos: Array<{ id: string; clave: string; nombre: string }>;
  catalogoQuery: ReturnType<typeof usePuestos>;
}

function AsignarPuestoModal({
  open,
  onOpenChange,
  sucursalId,
  asignados,
  deptosActivos,
  catalogoQuery,
}: AsignarPuestoModalProps) {
  const asignar = useAsignarPuestoASucursal();
  const [puestoId, setPuestoId] = useState('');
  const [departamentoId, setDepartamentoId] = useState('');
  const [busquedaPuesto, setBusquedaPuesto] = useState('');

  // Puestos disponibles del catálogo que no estén asignados a esta sucursal
  const puestosDisponibles = useMemo(() => {
    const catalogo = catalogoQuery.data?.items ?? [];
    const asignadosIds = new Set(asignados.map((a) => a.puestoId));
    return catalogo
      .filter((p) => !asignadosIds.has(p.id))
      .sort((a, b) => a.clave.localeCompare(b.clave));
  }, [catalogoQuery.data, asignados]);

  const puestosFiltrados = useMemo(() => {
    const q = busquedaPuesto.trim().toLowerCase();
    if (!q) return puestosDisponibles;
    return puestosDisponibles.filter(
      (p) =>
        p.clave.toLowerCase().includes(q) ||
        p.nombre.toLowerCase().includes(q),
    );
  }, [puestosDisponibles, busquedaPuesto]);

  // Puesto actualmente seleccionado
  const puestoSeleccionado = useMemo(() => {
    return puestosDisponibles.find((p) => p.id === puestoId) ?? null;
  }, [puestosDisponibles, puestoId]);

  // Si cambia el puesto y tiene un departamento sugerido que esté activo en la sucursal, sugerirlo
  function handleSelectPuesto(id: string) {
    setPuestoId(id);
    const p = puestosDisponibles.find((item) => item.id === id);
    if (p && p.departamentoId && deptosActivos.some((d) => d.id === p.departamentoId)) {
      setDepartamentoId(p.departamentoId);
    } else if (deptosActivos.length > 0 && !departamentoId) {
      setDepartamentoId(deptosActivos[0].id);
    }
  }

  function handleOpenChange(next: boolean) {
    if (!next) {
      setPuestoId('');
      setDepartamentoId('');
      setBusquedaPuesto('');
    }
    onOpenChange(next);
  }

  function handleConfirmar() {
    if (!puestoId || !departamentoId) return;

    asignar.mutate(
      {
        sucursalId,
        puestoId,
        departamentoId,
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => {
          toast.success(
            puestoSeleccionado
              ? `Puesto ${puestoSeleccionado.clave} asignado exitosamente`
              : 'Puesto asignado exitosamente',
          );
          handleOpenChange(false);
        },
        onError: handleError(puestoSeleccionado ? `asignar ${puestoSeleccionado.clave}` : 'asignar puesto'),
      },
    );
  }

  const sinDeptos = deptosActivos.length === 0;

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Asignar puesto a departamento en sucursal</DialogTitle>
          <DialogDescription>
            Selecciona el puesto del catálogo y el departamento al que pertenecerá
            dentro de la sucursal.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4 py-2">
          {catalogoQuery.isLoading ? (
            <div className="space-y-3">
              <Skeleton className="h-9 w-full" />
              <Skeleton className="h-9 w-full" />
            </div>
          ) : puestosDisponibles.length === 0 ? (
            <div className="rounded-md border border-dashed bg-muted/20 p-4 text-center text-sm text-muted-foreground">
              Todos los puestos del catálogo maestro ya están asignados a esta sucursal.
            </div>
          ) : (
            <>
              {/* Filtro rápido si hay muchos puestos */}
              {puestosDisponibles.length > 8 && (
                <div className="relative">
                  <Search className="absolute left-2.5 top-2.5 h-3.5 w-3.5 text-muted-foreground" />
                  <Input
                    placeholder="Filtrar puestos disponibles..."
                    value={busquedaPuesto}
                    onChange={(e) => setBusquedaPuesto(e.target.value)}
                    className="h-8 pl-8 text-xs"
                  />
                </div>
              )}

              {/* Selector de puesto */}
              <div className="space-y-1.5">
                <label
                  htmlFor="puesto-select"
                  className="text-xs font-semibold uppercase tracking-wider text-foreground"
                >
                  Puesto *
                </label>
                <select
                  id="puesto-select"
                  aria-label="Puesto a asignar"
                  className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm shadow-xs focus:outline-hidden focus:ring-2 focus:ring-ring"
                  value={puestoId}
                  onChange={(e) => handleSelectPuesto(e.target.value)}
                  disabled={asignar.isPending}
                >
                  <option value="">
                    -- Selecciona un puesto ({puestosFiltrados.length} disponibles) --
                  </option>
                  {puestosFiltrados.map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.clave} — {p.nombre}
                    </option>
                  ))}
                </select>
              </div>

              {/* Resumen del puesto seleccionado */}
              {puestoSeleccionado?.departamentoNombre && (
                <div className="rounded-md bg-muted/50 p-2.5 text-xs text-muted-foreground">
                  Departamento sugerido por catálogo maestro:{' '}
                  <span className="font-medium text-foreground">
                    {puestoSeleccionado.departamentoNombre}
                  </span>
                </div>
              )}
            </>
          )}

          {/* Selector de Departamento en Sucursal */}
          {sinDeptos ? (
            <div className="rounded-md border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800 dark:border-amber-900/50 dark:bg-amber-950/30 dark:text-amber-300">
              Esta sucursal no tiene departamentos activos asignados. Ve a la
              pestaña &ldquo;Departamentos&rdquo; para asignar departamentos a la
              sucursal antes de asociar puestos.
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
                aria-label="Departamento asignado *"
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

        <DialogFooter className="gap-2 sm:gap-0">
          <Button
            variant="outline"
            onClick={() => handleOpenChange(false)}
            disabled={asignar.isPending}
          >
            Cancelar
          </Button>
          <Button
            onClick={handleConfirmar}
            disabled={
              asignar.isPending ||
              sinDeptos ||
              !puestoId ||
              !departamentoId ||
              puestosDisponibles.length === 0
            }
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
