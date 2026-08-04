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
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { Skeleton } from '@/components/ui/skeleton';
import { useDepartamentos } from '@/features/catalogos/api';
import {
  EstatusCatalogo,
  type SucursalResponse,
} from '@/modules/administracion/api/types';
import {
  useAsignarDepartamentoASucursal,
  useDepartamentosDeSucursal,
  useDesactivarAsignacionSucursalDepartamento,
  useReactivarAsignacionSucursalDepartamento,
} from '@/modules/administracion/api';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';

/**
 * <c>&lt;SheetDepartamentosDeSucursal/&gt;</c> — Sheet slide-from-right
 * que gestiona las asignaciones N:M de una sucursal con el catálogo
 * global de departamentos (PR-A3 frontend / PR-A1 backend).
 *
 * <para>Lista TODOS los departamentos del catálogo global, marcando los
 * asignados con su estatus en la sucursal. Acciones por row:
 * <list type="bullet">
 *   <item>No asignado → botón "Asignar" (crea fila en estado Activo).</item>
 *   <item>Activa → badge "Activa" + botón "Desactivar" (alert confirm).</item>
 *   <item>Inactiva → badge "Inactiva" + botón "Reactivar".</item>
 * </list>
 * </para>
 *
 * <para>El caller (SucursalesPanel) controla el ciclo de vida: pasa la
 * <see cref="SucursalResponse"/> activa y un <c>onOpenChange</c>. Cuando
 * <c>sucursal === null</c> el Sheet está cerrado.</para>
 */
export interface SheetDepartamentosDeSucursalProps {
  /** Sucursal cuyas asignaciones se gestionan. <c>null</c> = cerrado. */
  sucursal: SucursalResponse | null;
  onOpenChange: (open: boolean) => void;
}

interface RowData {
  departamentoId: string;
  clave: string;
  nombre: string;
  /** <c>null</c> = no asignado a la sucursal. */
  estatusEnSucursal: EstatusCatalogo | null;
}

export function SheetDepartamentosDeSucursal({
  sucursal,
  onOpenChange,
}: SheetDepartamentosDeSucursalProps) {
  return (
    <Sheet
      open={sucursal != null}
      onOpenChange={(open) => onOpenChange(open)}
    >
      <SheetContent
        side="right"
        className="w-full overflow-y-auto sm:max-w-2xl"
      >
        <SheetHeader>
          <SheetTitle>
            Departamentos en {sucursal?.clave ?? '—'}
          </SheetTitle>
          <SheetDescription>
            Asigna o desactiva los departamentos que operan en esta sucursal.
            Los cambios bloquean nuevas requisiciones con combinaciones
            inactivas pero no afectan las existentes.
          </SheetDescription>
        </SheetHeader>

        <div className="px-6 pb-6">
          {sucursal != null && <ContenidoSheet sucursal={sucursal} />}
        </div>
      </SheetContent>
    </Sheet>
  );
}

interface ContenidoSheetProps {
  sucursal: SucursalResponse;
}

function ContenidoSheet({ sucursal }: ContenidoSheetProps) {
  const catalogoQuery = useDepartamentos();
  const asignadosQuery = useDepartamentosDeSucursal(sucursal.id);

  const rows = useMemo<RowData[]>(() => {
    const catalogo = catalogoQuery.data?.items ?? [];
    const asignados = asignadosQuery.data?.items ?? [];
    const mapAsig = new Map<string, EstatusCatalogo>(
      asignados.map((a) => [a.departamentoId, a.estatus as EstatusCatalogo]),
    );
    // Ordena: asignados primero (por clave), luego no asignados (por clave).
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

  const isLoading =
    catalogoQuery.isLoading || asignadosQuery.isLoading;
  const isError = catalogoQuery.isError || asignadosQuery.isError;

  if (isError) {
    return (
      <div className="mt-4 rounded-md border border-rose-300 bg-rose-50 px-4 py-3 text-sm text-rose-700">
        No se pudo cargar el catálogo. Reintenta más tarde.
      </div>
    );
  }

  if (isLoading) {
    return (
      <div className="mt-4 space-y-2">
        <Skeleton className="h-12 w-full" />
        <Skeleton className="h-12 w-full" />
        <Skeleton className="h-12 w-full" />
      </div>
    );
  }

  if (rows.length === 0) {
    return (
      <div className="mt-4 rounded-md border border-dashed bg-muted/20 px-4 py-6 text-center text-sm text-muted-foreground">
        El catálogo de departamentos está vacío.
      </div>
    );
  }

  return (
    <div className="mt-4 space-y-3">
      <div className="text-xs text-muted-foreground">
        Total: {rows.length} · Asignados:{' '}
        {rows.filter((r) => r.estatusEnSucursal != null).length}
      </div>
      <ul className="divide-y rounded-md border bg-card">
        {rows.map((row) => (
          <FilaDepartamento
            key={row.departamentoId}
            sucursalId={sucursal.id}
            sucursalClave={sucursal.clave}
            row={row}
          />
        ))}
      </ul>
    </div>
  );
}

interface FilaDepartamentoProps {
  sucursalId: string;
  sucursalClave: string;
  row: RowData;
}

function FilaDepartamento({
  sucursalId,
  sucursalClave,
  row,
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
      {
        sucursalId,
        departamentoId: row.departamentoId,
        idempotencyKey,
      },
      {
        onSuccess: () => {
          toast.success(
            `${row.clave} asignado a ${sucursalClave}`,
          );
        },
        onError: handleError(`asignar ${row.clave}`),
      },
    );
  }

  function handleConfirmarDesactivar() {
    desactivar.mutate(
      {
        sucursalId,
        departamentoId: row.departamentoId,
        idempotencyKey,
      },
      {
        onSuccess: () => {
          toast.success(`${row.clave} desactivado en ${sucursalClave}`);
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
      {
        sucursalId,
        departamentoId: row.departamentoId,
        idempotencyKey,
      },
      {
        onSuccess: () => {
          toast.success(`${row.clave} reactivado en ${sucursalClave}`);
        },
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

        {noAsignado && (
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
        {activa && (
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
        {inactiva && (
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
              la sucursal{' '}
              <span className="font-mono font-semibold">{sucursalClave}</span>?
              Bloquea nuevas requisiciones con esta combinación pero NO
              afecta las existentes.
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
