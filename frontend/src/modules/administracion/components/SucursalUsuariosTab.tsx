import { useMemo, useState } from 'react';
import { Plus, PowerOff, RotateCcw, Search, UserCheck, Users } from 'lucide-react';
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
import { useUsuarios } from '@/modules/identidad/api';
import { EstatusCatalogo } from '@/modules/administracion/api/types';
import type { UsuarioSucursalResponse } from '@/modules/administracion/api/types';
import {
  useAsignarUsuarioASucursal,
  useUsuariosDeSucursal,
  useDesactivarAsignacionUsuarioSucursal,
  useReactivarAsignacionUsuarioSucursal,
} from '@/modules/administracion/api';
import { esApiError } from '@/lib/api';
import { SucursalTabErrorState } from '@/modules/administracion/components/SucursalTabErrorState';

export interface SucursalUsuariosTabProps {
  sucursalId: string;
  canGestionar: boolean;
}

export function SucursalUsuariosTab({
  sucursalId,
  canGestionar,
}: SucursalUsuariosTabProps) {
  const catalogoQuery = useUsuarios({ soloActivos: true });
  const asignadosQuery = useUsuariosDeSucursal(sucursalId);

  const [filtro, setFiltro] = useState('');
  const [modalAsignarOpen, setModalAsignarOpen] = useState(false);

  const asignados = asignadosQuery.data?.items ?? [];

  const asignadosFiltrados = useMemo(() => {
    const q = filtro.trim().toLowerCase();
    if (!q) return asignados;
    return asignados.filter(
      (u) =>
        u.usuarioEmail.toLowerCase().includes(q) ||
        u.usuarioNombre.toLowerCase().includes(q),
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
      {/* Barra superior de herramientas */}
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex items-center gap-2">
          <div className="relative w-full sm:w-72">
            <Search className="absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" />
            <Input
              placeholder="Buscar por nombre o correo..."
              value={filtro}
              onChange={(e) => setFiltro(e.target.value)}
              className="pl-8"
              aria-label="Buscar usuarios asignados"
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
            <Plus className="mr-1.5 h-4 w-4" /> Asignar usuario
          </Button>
        )}
      </div>

      {/* Lista de asignados o empty state */}
      {asignados.length === 0 ? (
        <div className="flex flex-col items-center justify-center rounded-lg border border-dashed bg-muted/20 px-6 py-10 text-center">
          <Users className="mb-3 h-10 w-10 text-muted-foreground/60" />
          <h3 className="text-sm font-semibold">No hay usuarios asignados</h3>
          <p className="mt-1 text-xs text-muted-foreground max-w-sm">
            Esta sucursal aún no tiene usuarios vinculados. Asocia usuarios del
            sistema para permitirles operar dentro de esta sucursal.
          </p>
          {canGestionar && (
            <Button
              variant="outline"
              size="sm"
              className="mt-4"
              onClick={() => setModalAsignarOpen(true)}
            >
              <Plus className="mr-1.5 h-4 w-4" /> Asignar primer usuario
            </Button>
          )}
        </div>
      ) : asignadosFiltrados.length === 0 ? (
        <div className="rounded-md border border-dashed bg-muted/10 px-4 py-8 text-center text-sm text-muted-foreground">
          No se encontraron usuarios asignados que coincidan con &ldquo;{filtro}&rdquo;.
        </div>
      ) : (
        <ul className="divide-y rounded-md border bg-card">
          {asignadosFiltrados.map((item) => (
            <FilaUsuarioAsignado
              key={item.usuarioId}
              sucursalId={sucursalId}
              item={item}
              canGestionar={canGestionar}
            />
          ))}
        </ul>
      )}

      {/* Modal para asignar usuarios */}
      <AsignarUsuarioModal
        open={modalAsignarOpen}
        onOpenChange={setModalAsignarOpen}
        sucursalId={sucursalId}
        asignados={asignados}
        catalogoQuery={catalogoQuery}
      />
    </div>
  );
}

interface FilaUsuarioAsignadoProps {
  sucursalId: string;
  item: UsuarioSucursalResponse;
  canGestionar: boolean;
}

function FilaUsuarioAsignado({
  sucursalId,
  item,
  canGestionar,
}: FilaUsuarioAsignadoProps) {
  const desactivar = useDesactivarAsignacionUsuarioSucursal();
  const reactivar = useReactivarAsignacionUsuarioSucursal();
  const [confirmDesactivar, setConfirmDesactivar] = useState(false);

  const activa = item.estatus === EstatusCatalogo.Activo;
  const inactiva = item.estatus === EstatusCatalogo.Inactivo;

  function handleConfirmarDesactivar() {
    desactivar.mutate(
      {
        sucursalId,
        usuarioId: item.usuarioId,
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => {
          toast.success(`${item.usuarioEmail} desactivado`);
          setConfirmDesactivar(false);
        },
        onError: (error) => {
          handleError(`desactivar ${item.usuarioEmail}`)(error);
          setConfirmDesactivar(false);
        },
      },
    );
  }

  function handleReactivar() {
    reactivar.mutate(
      {
        sucursalId,
        usuarioId: item.usuarioId,
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => toast.success(`${item.usuarioEmail} reactivado`),
        onError: handleError(`reactivar ${item.usuarioEmail}`),
      },
    );
  }

  const pending = desactivar.isPending || reactivar.isPending;

  return (
    <li className="flex items-center justify-between gap-3 px-4 py-3">
      <div className="flex flex-wrap items-center gap-3">
        <span className="text-sm font-semibold text-foreground">
          {item.usuarioNombre}
        </span>
        <span className="font-mono text-xs text-muted-foreground">
          {item.usuarioEmail}
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
            aria-label={`Desactivar usuario ${item.usuarioEmail}`}
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
            aria-label={`Reactivar usuario ${item.usuarioEmail}`}
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
            <AlertDialogTitle>Desactivar usuario</AlertDialogTitle>
            <AlertDialogDescription>
              ¿Confirmas desactivar al usuario{' '}
              <span className="font-semibold">{item.usuarioEmail}</span> en esta
              sucursal? Ya no podrá operar dentro de esta sucursal pero conservará su
              cuenta global y su acceso a otras sucursales.
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

interface AsignarUsuarioModalProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  sucursalId: string;
  asignados: UsuarioSucursalResponse[];
  catalogoQuery: ReturnType<typeof useUsuarios>;
}

function AsignarUsuarioModal({
  open,
  onOpenChange,
  sucursalId,
  asignados,
  catalogoQuery,
}: AsignarUsuarioModalProps) {
  const asignar = useAsignarUsuarioASucursal();
  const [usuarioId, setUsuarioId] = useState('');
  const [busquedaUsuario, setBusquedaUsuario] = useState('');

  // Usuarios disponibles del sistema que no estén asignados a esta sucursal
  const usuariosDisponibles = useMemo(() => {
    const catalogo = catalogoQuery.data?.items ?? [];
    const asignadosIds = new Set(asignados.map((a) => a.usuarioId));
    return catalogo
      .filter((u) => !asignadosIds.has(u.id))
      .sort((a, b) => a.email.localeCompare(b.email));
  }, [catalogoQuery.data, asignados]);

  const usuariosFiltrados = useMemo(() => {
    const q = busquedaUsuario.trim().toLowerCase();
    if (!q) return usuariosDisponibles;
    return usuariosDisponibles.filter(
      (u) =>
        u.email.toLowerCase().includes(q) ||
        u.nombre.toLowerCase().includes(q),
    );
  }, [usuariosDisponibles, busquedaUsuario]);

  const usuarioSeleccionado = useMemo(() => {
    return usuariosDisponibles.find((u) => u.id === usuarioId) ?? null;
  }, [usuariosDisponibles, usuarioId]);

  function handleOpenChange(next: boolean) {
    if (!next) {
      setUsuarioId('');
      setBusquedaUsuario('');
    }
    onOpenChange(next);
  }

  function handleConfirmar() {
    if (!usuarioId) return;

    asignar.mutate(
      {
        sucursalId,
        usuarioId,
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => {
          toast.success(
            usuarioSeleccionado
              ? `Usuario ${usuarioSeleccionado.email} asignado a la sucursal`
              : 'Usuario asignado a la sucursal',
          );
          handleOpenChange(false);
        },
        onError: handleError(usuarioSeleccionado ? `asignar ${usuarioSeleccionado.email}` : 'asignar usuario'),
      },
    );
  }

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Asignar usuario a la sucursal</DialogTitle>
          <DialogDescription>
            Selecciona un usuario del sistema para concederle acceso operativo a esta sucursal.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4 py-2">
          {catalogoQuery.isLoading ? (
            <div className="space-y-3">
              <Skeleton className="h-9 w-full" />
              <Skeleton className="h-9 w-full" />
            </div>
          ) : usuariosDisponibles.length === 0 ? (
            <div className="rounded-md border border-dashed bg-muted/20 p-4 text-center text-sm text-muted-foreground">
              Todos los usuarios activos del sistema ya están asignados a esta sucursal.
            </div>
          ) : (
            <>
              {usuariosDisponibles.length > 8 && (
                <div className="relative">
                  <Search className="absolute left-2.5 top-2.5 h-3.5 w-3.5 text-muted-foreground" />
                  <Input
                    placeholder="Filtrar usuarios disponibles..."
                    value={busquedaUsuario}
                    onChange={(e) => setBusquedaUsuario(e.target.value)}
                    className="h-8 pl-8 text-xs"
                  />
                </div>
              )}

              <div className="space-y-1.5">
                <label
                  htmlFor="select-usuario"
                  className="text-xs font-semibold uppercase tracking-wider text-foreground"
                >
                  Usuario *
                </label>
                <select
                  id="select-usuario"
                  aria-label="Usuario a asignar"
                  className="w-full rounded-md border border-input bg-background px-3 py-2 text-sm shadow-xs focus:outline-hidden focus:ring-2 focus:ring-ring"
                  value={usuarioId}
                  onChange={(e) => setUsuarioId(e.target.value)}
                  disabled={asignar.isPending}
                >
                  <option value="">
                    -- Selecciona un usuario ({usuariosFiltrados.length} disponibles) --
                  </option>
                  {usuariosFiltrados.map((u) => (
                    <option key={u.id} value={u.id}>
                      {u.nombre} ({u.email})
                    </option>
                  ))}
                </select>
              </div>

              {usuarioSeleccionado && (
                <div className="flex items-center gap-2 rounded-md bg-muted/50 p-2.5 text-xs text-muted-foreground">
                  <UserCheck className="h-4 w-4 text-primary" />
                  <span>
                    Se asignará <strong className="text-foreground">{usuarioSeleccionado.nombre}</strong> con correo <strong className="text-foreground">{usuarioSeleccionado.email}</strong>.
                  </span>
                </div>
              )}
            </>
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
            disabled={asignar.isPending || !usuarioId || usuariosDisponibles.length === 0}
          >
            {asignar.isPending ? 'Asignando…' : 'Asignar usuario'}
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
