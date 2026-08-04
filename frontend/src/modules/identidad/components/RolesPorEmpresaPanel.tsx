import { useState } from 'react';
import { Plus, Trash2 } from 'lucide-react';
import { toast } from 'sonner';
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
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import { useRevocarAsignacion } from '@/modules/identidad/api';
import type { AsignacionDetalleResponse } from '@/modules/identidad/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { RolesPorEmpresaInlineForm } from '@/modules/identidad/components/RolesPorEmpresaInlineForm';
import { DateTimeDisplay } from '@/components/erp';

/**
 * Panel "Roles por empresa" del detalle de usuario. Renderiza:
 *
 * <list>
 *   <item>Lista de asignaciones existentes (Empresa RFC · Rol código +
 *         fecha de asignación + botón Revocar).</item>
 *   <item>Botón "Asignar rol" que monta el inline form
 *         (<c>RolesPorEmpresaInlineForm</c>, border dashed primary).</item>
 *   <item>AlertDialog de confirmación al revocar.</item>
 * </list>
 *
 * <para>Acciones bloqueadas si el usuario actual no tiene
 * <c>identidad.asignaciones.administrar</c>.</para>
 */
export interface RolesPorEmpresaPanelProps {
  usuarioId: string;
  asignaciones: readonly AsignacionDetalleResponse[];
}

export function RolesPorEmpresaPanel({
  usuarioId,
  asignaciones,
}: RolesPorEmpresaPanelProps) {
  const [agregando, setAgregando] = useState(false);
  const [paraRevocar, setParaRevocar] =
    useState<AsignacionDetalleResponse | null>(null);

  const canGestionar = useHasPermission(
    PermisosCanonicos.IdentidadAsignacionesAdministrar,
  );

  const idempotencyKey = useFormIdempotencyKey();
  const revocar = useRevocarAsignacion();

  function handleConfirmarRevocar() {
    if (paraRevocar == null) return;
    const target = paraRevocar;
    revocar.mutate(
      {
        asignacionId: target.id,
        usuarioId,
        idempotencyKey,
      },
      {
        onSuccess: () => {
          toast.success(
            `Rol "${target.rolCodigo}" revocado en ${target.empresaRfc}`,
          );
          setParaRevocar(null);
        },
        onError: (error) => {
          if (esApiError(error)) {
            toast.error(error.problem.title, {
              description: error.traceId
                ? `Código: ${error.traceId}`
                : undefined,
            });
          } else {
            toast.error('Error al revocar la asignación.');
          }
          setParaRevocar(null);
        },
      },
    );
  }

  return (
    <section className="space-y-3" aria-label="Roles por empresa del usuario">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h3 className="text-base font-semibold">Roles por empresa</h3>
          <p className="text-xs text-muted-foreground">
            Asignaciones activas del usuario en cada razón social.
            Total: {asignaciones.length}.
          </p>
        </div>
        {canGestionar && !agregando && (
          <Button
            size="sm"
            variant="outline"
            onClick={() => setAgregando(true)}
          >
            <Plus className="mr-1 h-4 w-4" />
            Asignar rol
          </Button>
        )}
      </header>

      {agregando && canGestionar && (
        <RolesPorEmpresaInlineForm
          usuarioId={usuarioId}
          onCancel={() => setAgregando(false)}
          onSaved={() => setAgregando(false)}
        />
      )}

      {asignaciones.length === 0 ? (
        <div className="rounded-md border border-dashed bg-muted/20 px-4 py-6 text-center text-sm text-muted-foreground">
          No hay asignaciones registradas.
        </div>
      ) : (
        <ul className="divide-y rounded-md border bg-card">
          {asignaciones.map((a) => (
            <li key={a.id} className="px-3 py-2">
              <div className="flex flex-wrap items-center gap-3">
                <div className="min-w-0 flex-1 space-y-0.5">
                  <div className="flex flex-wrap items-center gap-2 text-sm">
                    <span className="font-mono font-semibold">
                      {a.empresaRfc}
                    </span>
                    <span className="text-muted-foreground">·</span>
                    <span className="font-mono">{a.rolCodigo}</span>
                  </div>
                  <div className="truncate text-xs text-muted-foreground">
                    Asignado <DateTimeDisplay value={a.fechaAsignacion} />
                  </div>
                </div>
                {canGestionar && (
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={() => setParaRevocar(a)}
                    aria-label={`Revocar rol ${a.rolCodigo} en ${a.empresaRfc}`}
                  >
                    <Trash2 className="mr-1 h-3.5 w-3.5" />
                    Revocar
                  </Button>
                )}
              </div>
            </li>
          ))}
        </ul>
      )}

      <AlertDialog
        open={paraRevocar != null}
        onOpenChange={(open) => {
          if (!open) setParaRevocar(null);
        }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Revocar asignación</AlertDialogTitle>
            <AlertDialogDescription>
              ¿Confirmas revocar el rol{' '}
              <span className="font-mono font-semibold">
                {paraRevocar?.rolCodigo}
              </span>{' '}
              del usuario en{' '}
              <span className="font-mono font-semibold">
                {paraRevocar?.empresaRfc}
              </span>
              ? El usuario perderá el acceso al recargar su sesión. La
              acción se puede revertir asignando el rol nuevamente.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={revocar.isPending}>
              Cancelar
            </AlertDialogCancel>
            <AlertDialogAction
              onClick={handleConfirmarRevocar}
              disabled={revocar.isPending}
            >
              {revocar.isPending ? 'Revocando…' : 'Revocar'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </section>
  );
}
