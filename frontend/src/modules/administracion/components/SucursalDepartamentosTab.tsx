import { useMemo, useState } from 'react';
import { Building2, Plus, PowerOff, RotateCcw, Search } from 'lucide-react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
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
import { useDepartamentos } from '@/features/catalogos/api';
import { EstatusCatalogo } from '@/modules/administracion/api/types';
import type { SucursalDepartamentoResponse } from '@/modules/administracion/api/types';
import {
  useAsignarDepartamentoASucursal,
  useDepartamentosDeSucursal,
  useDesactivarAsignacionSucursalDepartamento,
  useReactivarAsignacionSucursalDepartamento,
} from '@/modules/administracion/api';
import { esApiError } from '@/lib/api';
import { SucursalTabErrorState } from '@/modules/administracion/components/SucursalTabErrorState';

export interface SucursalDepartamentosTabProps {
  sucursalId: string;
  canGestionar: boolean;
}

export function SucursalDepartamentosTab({
  sucursalId,
  canGestionar,
}: SucursalDepartamentosTabProps) {
  const catalogoQuery = useDepartamentos();
  const asignadosQuery = useDepartamentosDeSucursal(sucursalId);

  const [filtro, setFiltro] = useState('');
  const [modalAsignarOpen, setModalAsignarOpen] = useState(false);

  const asignados = asignadosQuery.data?.items ?? [];

  const asignadosFiltrados = useMemo(() => {
    const q = filtro.trim().toLowerCase();
    if (!q) return asignados;
    return asignados.filter(
      (d) =>
        d.departamentoClave.toLowerCase().includes(q) ||
        d.departamentoNombre.toLowerCase().includes(q),
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

  const isLoading = asignadosQuery.isLoading;
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
      {/* Barra superior de acciones y filtro */}
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex items-center gap-2">
          <div className="relative w-full sm:w-72">
            <Search className="absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" />
            <Input
              placeholder="Buscar en asignados..."
              value={filtro}
              onChange={(e) => setFiltro(e.target.value)}
              className="pl-8"
              aria-label="Buscar departamentos asignados"
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
            <Plus className="mr-1.5 h-4 w-4" /> Asignar departamento
          </Button>
        )}
      </div>

      {/* Lista de asignados o empty states */}
      {asignados.length === 0 ? (
        <div className="flex flex-col items-center justify-center rounded-lg border border-dashed bg-muted/20 px-6 py-10 text-center">
          <Building2 className="mb-3 h-10 w-10 text-muted-foreground/60" />
          <h3 className="text-sm font-semibold">No hay departamentos asignados</h3>
          <p className="mt-1 text-xs text-muted-foreground max-w-sm">
            Esta sucursal aún no tiene departamentos vinculados. Asigna departamentos
            del catálogo maestro para habilitar puestos y operaciones.
          </p>
          {canGestionar && (
            <Button
              variant="outline"
              size="sm"
              className="mt-4"
              onClick={() => setModalAsignarOpen(true)}
            >
              <Plus className="mr-1.5 h-4 w-4" /> Asignar primer departamento
            </Button>
          )}
        </div>
      ) : asignadosFiltrados.length === 0 ? (
        <div className="rounded-md border border-dashed bg-muted/10 px-4 py-8 text-center text-sm text-muted-foreground">
          No se encontraron departamentos asignados que coincidan con &ldquo;{filtro}&rdquo;.
        </div>
      ) : (
        <ul className="divide-y rounded-md border bg-card">
          {asignadosFiltrados.map((item) => (
            <FilaDepartamentoAsignado
              key={item.departamentoId}
              sucursalId={sucursalId}
              item={item}
              canGestionar={canGestionar}
            />
          ))}
        </ul>
      )}

      {/* Modal para asignar nuevos departamentos */}
      <AsignarDepartamentoModal
        open={modalAsignarOpen}
        onOpenChange={setModalAsignarOpen}
        sucursalId={sucursalId}
        asignados={asignados}
        catalogoQuery={catalogoQuery}
      />
    </div>
  );
}

interface FilaDepartamentoAsignadoProps {
  sucursalId: string;
  item: SucursalDepartamentoResponse;
  canGestionar: boolean;
}

function FilaDepartamentoAsignado({
  sucursalId,
  item,
  canGestionar,
}: FilaDepartamentoAsignadoProps) {
  const desactivar = useDesactivarAsignacionSucursalDepartamento();
  const reactivar = useReactivarAsignacionSucursalDepartamento();
  const [confirmDesactivar, setConfirmDesactivar] = useState(false);

  const activa = item.estatus === EstatusCatalogo.Activo;
  const inactiva = item.estatus === EstatusCatalogo.Inactivo;

  function handleConfirmarDesactivar() {
    desactivar.mutate(
      {
        sucursalId,
        departamentoId: item.departamentoId,
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => {
          toast.success(`${item.departamentoClave} desactivado`);
          setConfirmDesactivar(false);
        },
        onError: (error) => {
          handleError(`desactivar ${item.departamentoClave}`)(error);
          setConfirmDesactivar(false);
        },
      },
    );
  }

  function handleReactivar() {
    reactivar.mutate(
      {
        sucursalId,
        departamentoId: item.departamentoId,
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => toast.success(`${item.departamentoClave} reactivado`),
        onError: handleError(`reactivar ${item.departamentoClave}`),
      },
    );
  }

  const pending = desactivar.isPending || reactivar.isPending;

  return (
    <li className="flex items-center justify-between gap-3 px-4 py-3">
      <div className="flex flex-wrap items-center gap-3">
        <span className="font-mono text-sm font-semibold text-primary">
          {item.departamentoClave}
        </span>
        <span className="text-sm font-medium text-foreground">
          {item.departamentoNombre}
        </span>
        {activa && (
          <Badge variant="secondary" className="bg-emerald-500/10 text-emerald-700 dark:text-emerald-400">
            Activo
          </Badge>
        )}
        {inactiva && (
          <Badge variant="outline" className="text-muted-foreground">
            Inactivo
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
            aria-label={`Desactivar departamento ${item.departamentoClave}`}
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
            aria-label={`Reactivar departamento ${item.departamentoClave}`}
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
            <AlertDialogTitle>Desactivar departamento</AlertDialogTitle>
            <AlertDialogDescription>
              ¿Confirmas desactivar{' '}
              <span className="font-mono font-semibold">{item.departamentoClave}</span> en
              esta sucursal? Bloquea nuevas asignaciones de puestos y requisiciones con
              esta combinación pero NO afecta las existentes.
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

interface AsignarDepartamentoModalProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  sucursalId: string;
  asignados: SucursalDepartamentoResponse[];
  catalogoQuery: ReturnType<typeof useDepartamentos>;
}

function AsignarDepartamentoModal({
  open,
  onOpenChange,
  sucursalId,
  asignados,
  catalogoQuery,
}: AsignarDepartamentoModalProps) {
  const [departamentoSeleccionadoId, setDepartamentoSeleccionadoId] = useState('');
  const [busquedaCatalogo, setBusquedaCatalogo] = useState('');
  const asignar = useAsignarDepartamentoASucursal();

  // Filtrar departamentos del catálogo que no estén asignados a esta sucursal
  const disponibles = useMemo(() => {
    const catalogo = catalogoQuery.data?.items ?? [];
    const asignadosIds = new Set(asignados.map((a) => a.departamentoId));
    return catalogo
      .filter((d) => !asignadosIds.has(d.id))
      .sort((a, b) => a.clave.localeCompare(b.clave));
  }, [catalogoQuery.data, asignados]);

  const disponiblesFiltrados = useMemo(() => {
    const q = busquedaCatalogo.trim().toLowerCase();
    if (!q) return disponibles;
    return disponibles.filter(
      (d) =>
        d.clave.toLowerCase().includes(q) ||
        d.nombre.toLowerCase().includes(q),
    );
  }, [disponibles, busquedaCatalogo]);

  function handleAsignar() {
    if (!departamentoSeleccionadoId) return;
    const depto = disponibles.find((d) => d.id === departamentoSeleccionadoId);

    asignar.mutate(
      {
        sucursalId,
        departamentoId: departamentoSeleccionadoId,
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => {
          toast.success(
            depto ? `Departamento ${depto.clave} asignado` : 'Departamento asignado',
          );
          setDepartamentoSeleccionadoId('');
          setBusquedaCatalogo('');
          onOpenChange(false);
        },
        onError: handleError('asignar departamento'),
      },
    );
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Asignar departamento a la sucursal</DialogTitle>
          <DialogDescription>
            Selecciona un departamento del catálogo maestro para habilitarlo en esta
            sucursal.
          </DialogDescription>
        </DialogHeader>

        {catalogoQuery.isLoading ? (
          <div className="space-y-3 py-2">
            <Skeleton className="h-9 w-full" />
            <Skeleton className="h-9 w-full" />
          </div>
        ) : disponibles.length === 0 ? (
          <div className="rounded-md border border-dashed bg-muted/20 p-4 text-center text-sm text-muted-foreground">
            Todos los departamentos del catálogo maestro ya están asignados a esta sucursal.
          </div>
        ) : (
          <div className="space-y-3 py-2">
            {disponibles.length > 8 && (
              <div className="relative">
                <Search className="absolute left-2.5 top-2.5 h-3.5 w-3.5 text-muted-foreground" />
                <Input
                  placeholder="Filtrar departamentos disponibles..."
                  value={busquedaCatalogo}
                  onChange={(e) => setBusquedaCatalogo(e.target.value)}
                  className="h-8 pl-8 text-xs"
                />
              </div>
            )}

            <div className="space-y-1.5">
              <Label htmlFor="select-departamento-catalogo" className="text-xs font-medium">
                Departamento *
              </Label>
              <select
                id="select-departamento-catalogo"
                aria-label="Departamento a asignar"
                value={departamentoSeleccionadoId}
                onChange={(e) => setDepartamentoSeleccionadoId(e.target.value)}
                className="flex h-9 w-full rounded-md border border-input bg-transparent px-3 py-1 text-sm shadow-sm transition-colors focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50"
              >
                <option value="">-- Selecciona un departamento ({disponiblesFiltrados.length} disponibles) --</option>
                {disponiblesFiltrados.map((d) => (
                  <option key={d.id} value={d.id}>
                    {d.clave} - {d.nombre}
                  </option>
                ))}
              </select>
            </div>
          </div>
        )}

        <DialogFooter className="gap-2 sm:gap-0">
          <Button
            type="button"
            variant="outline"
            onClick={() => onOpenChange(false)}
            disabled={asignar.isPending}
          >
            Cancelar
          </Button>
          <Button
            type="button"
            onClick={handleAsignar}
            disabled={!departamentoSeleccionadoId || asignar.isPending || disponibles.length === 0}
          >
            {asignar.isPending ? 'Asignando…' : 'Asignar a sucursal'}
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
        description: error.traceId ? `Código: ${error.traceId}` : undefined,
      });
    } else {
      toast.error(`Error al ${accion}.`);
    }
  };
}
