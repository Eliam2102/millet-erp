import { useState } from 'react';
import { KeyRound, Pencil, Plus, Power, PowerOff, Users } from 'lucide-react';
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
import { ErrorState, TableSkeleton } from '@/components/erp';
import { EstatusCatalogo } from '@/modules/administracion/api/types';
import {
  useDesactivarEmpleado,
  useEmpleadosAdmin,
  useReactivarEmpleado,
} from '@/modules/administracion/api';
import { usePuestos } from '@/features/catalogos/api';
import type { EmpleadoListItem } from '@/features/catalogos/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import { EmpleadoInlineForm } from '@/modules/administracion/components/EmpleadoInlineForm';
import { EmpleadoAccesoPanel } from '@/modules/administracion/components/EmpleadoAccesoPanel';

/**
 * <c>&lt;EmpleadosPage/&gt;</c> — master de empleados (ADM-FE-PR1,
 * doc 10-catalogo-puestos-empleados; backend ADM-PR1 en
 * <c>compartido.empleados</c>). Solicitantes de viáticos (puesto →
 * tope, jefe directo → autorizador N1), responsables de comprobaciones
 * y titulares de TC. NO es el padrón de nómina de RH.
 *
 * <para>Mismo patrón que <c>CanalesVentaPage</c>: lista + alta y
 * edición con inline forms, desactivar (baja) con confirm y reactivar
 * (recontratación) directo. Permiso:
 * <c>admin.empleados.gestionar</c>.</para>
 */
export function EmpleadosPage() {
  const [agregando, setAgregando] = useState(false);
  const [editandoId, setEditandoId] = useState<string | null>(null);
  const [accesoId, setAccesoId] = useState<string | null>(null);
  const [confirmDesactivar, setConfirmDesactivar] =
    useState<EmpleadoListItem | null>(null);

  const canGestionar = useHasPermission(
    PermisosCanonicos.AdminEmpleadosGestionar,
  );

  const query = useEmpleadosAdmin();
  const empleados = query.data ?? [];

  // Lookup puesto_id → clave para la columna Puesto (catálogo chico).
  const puestosQuery = usePuestos();
  const puestoClavePorId = new Map(
    (puestosQuery.data?.items ?? []).map((p) => [p.id, p.clave]),
  );

  const idempotencyKey = useFormIdempotencyKey();
  const desactivar = useDesactivarEmpleado();
  const reactivar = useReactivarEmpleado();
  const cambioPendiente = desactivar.isPending || reactivar.isPending;

  function onErrorEstatus(error: Error) {
    if (esApiError(error)) {
      toast.error(error.problem.title, {
        description: error.traceId ? `Código: ${error.traceId}` : undefined,
      });
    } else {
      toast.error('Error al actualizar el empleado.');
    }
    setConfirmDesactivar(null);
  }

  return (
    <div className="mx-auto max-w-5xl space-y-4 p-4">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h1 className="flex items-center gap-2 text-lg font-semibold">
            <Users className="h-5 w-5" aria-hidden="true" />
            Empleados
          </h1>
          <p className="text-sm text-muted-foreground">
            Master de personas para reglas de negocio (viáticos por puesto,
            jefe directo como autorizador). No administra préstamos de
            nómina. Total: {empleados.length}.
          </p>
        </div>
        {canGestionar && !agregando && (
          <Button
            size="sm"
            variant="outline"
            onClick={() => {
              setAgregando(true);
              setEditandoId(null);
            }}
          >
            <Plus className="mr-1 h-4 w-4" />
            Agregar empleado
          </Button>
        )}
      </header>

      {agregando && canGestionar && (
        <EmpleadoInlineForm
          onCancel={() => setAgregando(false)}
          onSaved={() => setAgregando(false)}
        />
      )}

      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar los empleados"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton rows={5} />
      ) : empleados.length === 0 ? (
        <div className="rounded-md border border-dashed bg-muted/20 px-4 py-6 text-center text-sm text-muted-foreground">
          No hay empleados registrados. Agrega el primero para habilitar
          las solicitudes de viáticos.
        </div>
      ) : (
        <ul className="divide-y rounded-md border bg-card">
          {empleados.map((e) => {
            const editando = editandoId === e.id;
            const activo = e.estatus === EstatusCatalogo.Activo;
            return (
              <li key={e.id} className="px-3 py-2">
                {editando && canGestionar ? (
                  <EmpleadoInlineForm
                    empleado={e}
                    onCancel={() => setEditandoId(null)}
                    onSaved={() => setEditandoId(null)}
                  />
                ) : (
                  <div className="flex flex-wrap items-center gap-3">
                    <span className="w-20 truncate font-mono text-xs text-muted-foreground">
                      {e.clave}
                    </span>
                    <div className="min-w-0 flex-1">
                      <p className="truncate text-sm">{e.nombre}</p>
                      {e.email && (
                        <p className="truncate text-xs text-muted-foreground">
                          {e.email}
                        </p>
                      )}
                    </div>
                    <span
                      className="w-16 truncate font-mono text-xs"
                      title="Puesto (llave de la política de viáticos)"
                    >
                      {e.puestoId
                        ? (puestoClavePorId.get(e.puestoId) ?? '…')
                        : '—'}
                    </span>
                    {activo ? (
                      <Badge variant="secondary">Activo</Badge>
                    ) : (
                      <Badge variant="outline" className="text-muted-foreground">
                        {e.estatus === EstatusCatalogo.EnRevision
                          ? 'En revisión'
                          : 'Inactivo'}
                      </Badge>
                    )}
                    {canGestionar && (
                      <div className="flex items-center gap-1">
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => setAccesoId(accesoId === e.id ? null : e.id)}
                          aria-label={`Gestionar acceso de ${e.clave}`}
                        >
                          <KeyRound className="mr-1 h-3.5 w-3.5" />
                          Acceso
                        </Button>
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => {
                            setEditandoId(e.id);
                            setAgregando(false);
                          }}
                          aria-label={`Editar empleado ${e.clave}`}
                        >
                          <Pencil className="h-3.5 w-3.5" />
                        </Button>
                        {activo ? (
                          <Button
                            variant="ghost"
                            size="sm"
                            onClick={() => setConfirmDesactivar(e)}
                            aria-label={`Desactivar empleado ${e.clave}`}
                          >
                            <PowerOff className="h-3.5 w-3.5" />
                          </Button>
                        ) : (
                          <Button
                            variant="ghost"
                            size="sm"
                            disabled={cambioPendiente}
                            onClick={() =>
                              reactivar.mutate(
                                { id: e.id, idempotencyKey },
                                {
                                  onSuccess: () =>
                                    toast.success(
                                      `Empleado "${e.nombre}" reactivado`,
                                    ),
                                  onError: onErrorEstatus,
                                },
                              )
                            }
                            aria-label={`Reactivar empleado ${e.clave}`}
                          >
                            <Power className="h-3.5 w-3.5" />
                          </Button>
                        )}
                      </div>
                    )}
                  </div>
                )}
                {accesoId === e.id && <div className="mt-2">
                  <EmpleadoAccesoPanel empleadoId={e.id} usuarioId={e.usuarioId}
                    email={e.email} empleadoActivo={activo} />
                </div>}
              </li>
            );
          })}
        </ul>
      )}

      <AlertDialog
        open={confirmDesactivar != null}
        onOpenChange={(open) => {
          if (!open) setConfirmDesactivar(null);
        }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Desactivar empleado</AlertDialogTitle>
            <AlertDialogDescription>
              ¿Confirmas dar de baja a{' '}
              <span className="font-semibold">{confirmDesactivar?.nombre}</span>
              ? Saldrá de los selectores (viáticos, comprobaciones); su
              histórico se conserva. Si tenía acceso, su usuario quedará
              bloqueado. En una recontratación, el acceso se reactiva por separado.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={cambioPendiente}>
              Cancelar
            </AlertDialogCancel>
            <AlertDialogAction
              onClick={() => {
                if (confirmDesactivar == null) return;
                desactivar.mutate(
                  { id: confirmDesactivar.id, idempotencyKey },
                  {
                    onSuccess: () => {
                      toast.success(
                        `Empleado "${confirmDesactivar.nombre}" desactivado`,
                      );
                      setConfirmDesactivar(null);
                    },
                    onError: onErrorEstatus,
                  },
                );
              }}
              disabled={cambioPendiente}
            >
              {cambioPendiente ? 'Desactivando…' : 'Desactivar'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}
