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
import { useDepartamentos } from '@/features/catalogos/api';
import { EstatusCatalogo } from '@/modules/administracion/api/types';
import {
  useAsignarDepartamentoASucursal,
  useDepartamentosDeSucursal,
  useDesactivarAsignacionSucursalDepartamento,
  useReactivarAsignacionSucursalDepartamento,
} from '@/modules/administracion/api';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import { SucursalTabErrorState } from '@/modules/administracion/components/SucursalTabErrorState';

/**
 * Tab "Departamentos" de <c>SucursalDetalle</c> (F1-ADM-01 Fase 3).
 * Cruza el catálogo global de departamentos con las asignaciones de
 * ESTA sucursal — mismo patrón que <c>SheetDepartamentosDeSucursal</c>
 * (PR-A3) pero inline (sin Sheet, ya estamos en una página dedicada).
 *
 * <para>Asociar/desasociar un departamento EXISTENTE — no crea uno
 * nuevo. Fila "no asignado" → botón Asignar; fila Activa → Desactivar;
 * fila Inactiva → Reactivar.</para>
 */
export interface SucursalDepartamentosTabProps {
  sucursalId: string;
  canGestionar: boolean;
}

interface RowData {
  departamentoId: string;
  clave: string;
  nombre: string;
  estatusEnSucursal: EstatusCatalogo | null;
}

export function SucursalDepartamentosTab({
  sucursalId,
  canGestionar,
}: SucursalDepartamentosTabProps) {
  const catalogoQuery = useDepartamentos();
  const asignadosQuery = useDepartamentosDeSucursal(sucursalId);

  const rows = useMemo<RowData[]>(() => {
    const catalogo = catalogoQuery.data?.items ?? [];
    const asignados = asignadosQuery.data?.items ?? [];
    const mapAsig = new Map<string, EstatusCatalogo>(
      asignados.map((a) => [a.departamentoId, a.estatus as EstatusCatalogo]),
    );
    return [...catalogo]
      .sort((a, b) => {
        const aAsignado = mapAsig.has(a.id);
        const bAsignado = mapAsig.has(b.id);
        if (aAsignado !== bAsignado) return aAsignado ? -1 : 1;
        return a.clave.localeCompare(b.clave);
      })
      .map((d) => ({
        departamentoId: d.id,
        clave: d.clave,
        nombre: d.nombre,
        estatusEnSucursal: mapAsig.get(d.id) ?? null,
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
        El catálogo de departamentos está vacío.
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
          <FilaDepartamento
            key={row.departamentoId}
            sucursalId={sucursalId}
            row={row}
            canGestionar={canGestionar}
          />
        ))}
      </ul>
    </div>
  );
}

interface FilaDepartamentoProps {
  sucursalId: string;
  row: RowData;
  canGestionar: boolean;
}

function FilaDepartamento({
  sucursalId,
  row,
  canGestionar,
}: FilaDepartamentoProps) {
  const idempotencyKey = useFormIdempotencyKey();
  const asignar = useAsignarDepartamentoASucursal();
  const desactivar = useDesactivarAsignacionSucursalDepartamento();
  const reactivar = useReactivarAsignacionSucursalDepartamento();
  const [confirmDesactivar, setConfirmDesactivar] = useState(false);

  const noAsignado = row.estatusEnSucursal == null;
  const activa = row.estatusEnSucursal === EstatusCatalogo.Activo;
  const inactiva = row.estatusEnSucursal === EstatusCatalogo.Inactivo;

  function handleAsignar() {
    asignar.mutate(
      { sucursalId, departamentoId: row.departamentoId, idempotencyKey },
      {
        onSuccess: () => toast.success(`${row.clave} asignado`),
        onError: handleError(`asignar ${row.clave}`),
      },
    );
  }

  function handleConfirmarDesactivar() {
    desactivar.mutate(
      { sucursalId, departamentoId: row.departamentoId, idempotencyKey },
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
      { sucursalId, departamentoId: row.departamentoId, idempotencyKey },
      {
        onSuccess: () => toast.success(`${row.clave} reactivado`),
        onError: handleError(`reactivar ${row.clave}`),
      },
    );
  }

  const pending =
    asignar.isPending || desactivar.isPending || reactivar.isPending;

  return (
    <li className="px-3 py-2">
      <div className="flex flex-wrap items-center gap-3">
        <span className="font-mono text-sm font-semibold">{row.clave}</span>
        <span className="flex-1 truncate text-sm">{row.nombre}</span>
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
            onClick={handleAsignar}
            aria-label={`Asignar departamento ${row.clave}`}
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
            aria-label={`Desactivar departamento ${row.clave}`}
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
            aria-label={`Reactivar departamento ${row.clave}`}
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
              <span className="font-mono font-semibold">{row.clave}</span> en
              esta sucursal? Bloquea nuevas requisiciones con esta
              combinación pero NO afecta las existentes.
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
