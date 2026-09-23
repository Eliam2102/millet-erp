import { useState } from 'react';
import { Briefcase, Pencil, Plus, Power, PowerOff } from 'lucide-react';
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
  useDesactivarPuesto,
  usePuestosAdmin,
  useReactivarPuesto,
} from '@/modules/administracion/api';
import type { PuestoListItem } from '@/features/catalogos/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import { PuestoInlineForm } from '@/modules/administracion/components/PuestoInlineForm';

/**
 * <c>&lt;PuestosPage/&gt;</c> — catálogo de puestos organizacionales
 * (ADM-FE-PR1, doc 10-catalogo-puestos-empleados; backend ADM-PR1 en
 * <c>compartido.puestos</c>). Es la llave de las políticas de viáticos
 * por puesto + tipo de destino de CxP.
 *
 * <para>Mismo patrón que <c>CanalesVentaPage</c>: lista + alta y
 * edición con inline forms (NUNCA modal), desactivar con confirm y
 * reactivar directo. Permiso: <c>admin.puestos.gestionar</c>.</para>
 */
export function PuestosPage() {
  const [agregando, setAgregando] = useState(false);
  const [editandoId, setEditandoId] = useState<string | null>(null);
  const [confirmDesactivar, setConfirmDesactivar] =
    useState<PuestoListItem | null>(null);

  const canGestionar = useHasPermission(PermisosCanonicos.AdminPuestosGestionar);

  const query = usePuestosAdmin();
  const puestos = query.data ?? [];

  const idempotencyKey = useFormIdempotencyKey();
  const desactivar = useDesactivarPuesto();
  const reactivar = useReactivarPuesto();
  const cambioPendiente = desactivar.isPending || reactivar.isPending;

  function onErrorEstatus(error: Error) {
    if (esApiError(error)) {
      toast.error(error.problem.title, {
        description: error.traceId ? `Código: ${error.traceId}` : undefined,
      });
    } else {
      toast.error('Error al actualizar el puesto.');
    }
    setConfirmDesactivar(null);
  }

  return (
    <div className="mx-auto max-w-3xl space-y-4 p-4">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h1 className="flex items-center gap-2 text-lg font-semibold">
            <Briefcase className="h-5 w-5" aria-hidden="true" />
            Puestos
          </h1>
          <p className="text-sm text-muted-foreground">
            Master organizacional — las políticas de viáticos (tope diario
            por destino) se definen por puesto. Total: {puestos.length}.
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
            Agregar puesto
          </Button>
        )}
      </header>

      {agregando && canGestionar && (
        <PuestoInlineForm
          onCancel={() => setAgregando(false)}
          onSaved={() => setAgregando(false)}
        />
      )}

      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar los puestos"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton rows={5} />
      ) : puestos.length === 0 ? (
        <div className="rounded-md border border-dashed bg-muted/20 px-4 py-6 text-center text-sm text-muted-foreground">
          No hay puestos registrados.
        </div>
      ) : (
        <ul className="divide-y rounded-md border bg-card">
          {puestos.map((p) => {
            const editando = editandoId === p.id;
            const activo = p.estatus === EstatusCatalogo.Activo;
            return (
              <li key={p.id} className="px-3 py-2">
                {editando && canGestionar ? (
                  <PuestoInlineForm
                    puesto={p}
                    onCancel={() => setEditandoId(null)}
                    onSaved={() => setEditandoId(null)}
                  />
                ) : (
                  <div className="flex flex-wrap items-center gap-3">
                    <span className="w-20 truncate font-mono text-xs text-muted-foreground">
                      {p.clave}
                    </span>
                    <div className="flex flex-1 flex-col truncate">
                      <span className="truncate text-sm font-medium">{p.nombre}</span>
                      <div className="flex flex-wrap items-center gap-x-2 gap-y-0.5 text-xs text-muted-foreground">
                        {p.rolSugeridoNombre ? (
                          <span>
                            Rol sugerido:{' '}
                            <span className="font-medium text-foreground">
                              {p.rolSugeridoNombre}
                            </span>
                          </span>
                        ) : (
                          <span className="italic text-muted-foreground/70">
                            Sin rol sugerido
                          </span>
                        )}
                      </div>
                    </div>
                    {activo ? (
                      <Badge variant="secondary">Activo</Badge>
                    ) : (
                      <Badge variant="outline" className="text-muted-foreground">
                        {p.estatus === EstatusCatalogo.EnRevision
                          ? 'En revisión'
                          : 'Inactivo'}
                      </Badge>
                    )}
                    {canGestionar && (
                      <div className="flex items-center gap-1">
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => {
                            setEditandoId(p.id);
                            setAgregando(false);
                          }}
                          aria-label={`Editar puesto ${p.clave}`}
                        >
                          <Pencil className="h-3.5 w-3.5" />
                        </Button>
                        {activo ? (
                          <Button
                            variant="ghost"
                            size="sm"
                            onClick={() => setConfirmDesactivar(p)}
                            aria-label={`Desactivar puesto ${p.clave}`}
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
                                { id: p.id, idempotencyKey },
                                {
                                  onSuccess: () =>
                                    toast.success(
                                      `Puesto "${p.nombre}" reactivado`,
                                    ),
                                  onError: onErrorEstatus,
                                },
                              )
                            }
                            aria-label={`Reactivar puesto ${p.clave}`}
                          >
                            <Power className="h-3.5 w-3.5" />
                          </Button>
                        )}
                      </div>
                    )}
                  </div>
                )}
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
            <AlertDialogTitle>Desactivar puesto</AlertDialogTitle>
            <AlertDialogDescription>
              ¿Confirmas desactivar el puesto{' '}
              <span className="font-semibold">{confirmDesactivar?.nombre}</span>
              ? Saldrá del selector y no se podrá asignar a empleados ni a
              políticas de viáticos nuevas; los empleados que ya lo tienen
              conservan la referencia. Se puede reactivar en cualquier
              momento.
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
                        `Puesto "${confirmDesactivar.nombre}" desactivado`,
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
