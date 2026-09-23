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
import { Skeleton } from '@/components/ui/skeleton';
import { useUsuarios } from '@/modules/identidad/api';
import { EstatusCatalogo } from '@/modules/administracion/api/types';
import {
  useAsignarUsuarioASucursal,
  useUsuariosDeSucursal,
  useDesactivarAsignacionUsuarioSucursal,
  useReactivarAsignacionUsuarioSucursal,
} from '@/modules/administracion/api';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import { SucursalTabErrorState } from '@/modules/administracion/components/SucursalTabErrorState';

/**
 * Tab "Usuarios" de <c>SucursalDetalle</c> (F1-ADM-01 Fase 3). Mismo
 * patrón que Departamentos/Puestos, pero el catálogo candidato viene
 * de <c>useUsuarios()</c> (usuarios activos del sistema, módulo
 * Identidad) en vez de un catálogo de Compartido — decisión de diseño:
 * no había UI previa de scoping de usuarios por sucursal, así que se
 * replica el patrón de "catálogo global cruzado contra asignados" que
 * ya usaba <c>SheetDepartamentosDeSucursal</c>.
 *
 * <para>Solo muestra usuarios ACTIVOS del catálogo como candidatos —
 * un usuario desactivado en Identidad no debería poder asignarse a
 * una sucursal nueva (aunque el backend no lo valide explícitamente,
 * es la UX esperada).</para>
 */
export interface SucursalUsuariosTabProps {
  sucursalId: string;
  canGestionar: boolean;
}

interface RowData {
  usuarioId: string;
  email: string;
  nombre: string;
  estatusEnSucursal: EstatusCatalogo | null;
}

export function SucursalUsuariosTab({
  sucursalId,
  canGestionar,
}: SucursalUsuariosTabProps) {
  const catalogoQuery = useUsuarios({ soloActivos: true });
  const asignadosQuery = useUsuariosDeSucursal(sucursalId);

  const rows = useMemo<RowData[]>(() => {
    const catalogo = catalogoQuery.data?.items ?? [];
    const asignados = asignadosQuery.data?.items ?? [];
    const mapAsig = new Map<string, EstatusCatalogo>(
      asignados.map((a) => [a.usuarioId, a.estatus as EstatusCatalogo]),
    );
    return [...catalogo]
      .sort((a, b) => {
        const aAsignado = mapAsig.has(a.id);
        const bAsignado = mapAsig.has(b.id);
        if (aAsignado !== bAsignado) return aAsignado ? -1 : 1;
        return a.email.localeCompare(b.email);
      })
      .map((u) => ({
        usuarioId: u.id,
        email: u.email,
        nombre: u.nombre,
        estatusEnSucursal: mapAsig.get(u.id) ?? null,
      }));
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

  const isLoading = catalogoQuery.isLoading || asignadosQuery.isLoading;
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
        No hay usuarios activos en el sistema.
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
          <FilaUsuario
            key={row.usuarioId}
            sucursalId={sucursalId}
            row={row}
            canGestionar={canGestionar}
          />
        ))}
      </ul>
    </div>
  );
}

interface FilaUsuarioProps {
  sucursalId: string;
  row: RowData;
  canGestionar: boolean;
}

function FilaUsuario({ sucursalId, row, canGestionar }: FilaUsuarioProps) {
  const idempotencyKey = useFormIdempotencyKey();
  const asignar = useAsignarUsuarioASucursal();
  const desactivar = useDesactivarAsignacionUsuarioSucursal();
  const reactivar = useReactivarAsignacionUsuarioSucursal();
  const [confirmDesactivar, setConfirmDesactivar] = useState(false);

  const noAsignado = row.estatusEnSucursal == null;
  const activa = row.estatusEnSucursal === EstatusCatalogo.Activo;
  const inactiva = row.estatusEnSucursal === EstatusCatalogo.Inactivo;

  function handleAsignar() {
    asignar.mutate(
      { sucursalId, usuarioId: row.usuarioId, idempotencyKey },
      {
        onSuccess: () => toast.success(`${row.email} asignado`),
        onError: handleError(`asignar ${row.email}`),
      },
    );
  }

  function handleConfirmarDesactivar() {
    desactivar.mutate(
      { sucursalId, usuarioId: row.usuarioId, idempotencyKey },
      {
        onSuccess: () => {
          toast.success(`${row.email} desasignado`);
          setConfirmDesactivar(false);
        },
        onError: (error) => {
          handleError(`desasignar ${row.email}`)(error);
          setConfirmDesactivar(false);
        },
      },
    );
  }

  function handleReactivar() {
    reactivar.mutate(
      { sucursalId, usuarioId: row.usuarioId, idempotencyKey },
      {
        onSuccess: () => toast.success(`${row.email} reasignado`),
        onError: handleError(`reasignar ${row.email}`),
      },
    );
  }

  const pending =
    asignar.isPending || desactivar.isPending || reactivar.isPending;

  return (
    <li className="px-3 py-2">
      <div className="flex flex-wrap items-center gap-3">
        <span className="flex-1 truncate text-sm font-medium">
          {row.nombre}
        </span>
        <span className="truncate text-xs text-muted-foreground">
          {row.email}
        </span>
        {activa && <Badge variant="secondary">Activo</Badge>}
        {inactiva && (
          <Badge variant="outline" className="text-muted-foreground">
            Inactivo
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
            onClick={handleAsignar}
            aria-label={`Asignar usuario ${row.email}`}
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
            aria-label={`Desactivar usuario ${row.email}`}
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
            aria-label={`Reactivar usuario ${row.email}`}
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
            <AlertDialogTitle>Desasignar usuario</AlertDialogTitle>
            <AlertDialogDescription>
              ¿Confirmas desasignar a{' '}
              <span className="font-mono font-semibold">{row.email}</span> de
              esta sucursal? Pierde el acceso scoped a esta sucursal; la
              acción se puede revertir reasignándolo.
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
              {desactivar.isPending ? 'Desasignando…' : 'Desasignar'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </li>
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
