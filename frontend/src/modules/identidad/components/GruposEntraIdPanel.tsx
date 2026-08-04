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
import { useDesasociarGrupoEntraId } from '@/modules/identidad/api';
import type { RolGrupoEntraIdResponse } from '@/modules/identidad/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { GrupoEntraIdInlineForm } from '@/modules/identidad/components/GrupoEntraIdInlineForm';

/**
 * Panel "Grupos Entra ID" del detalle del rol. Espejo de
 * <c>SucursalesPanel</c>/<c>DepartamentosPanel</c> — header con
 * acción "Asociar grupo", inline form (dashed) para agregar y lista
 * de los asociados con botón "Desasociar" con confirm dialog.
 *
 * <para>Roles del sistema (<c>esDelSistema=true</c>) NO permiten
 * asociar ni desasociar — el backend rechaza ambas operaciones; la
 * UI también las oculta.</para>
 */
export interface GruposEntraIdPanelProps {
  rolId: string;
  grupos: readonly RolGrupoEntraIdResponse[];
  /** <c>true</c> ⇒ oculta acciones de gestión (rol del sistema). */
  bloqueado?: boolean;
}

export function GruposEntraIdPanel({
  rolId,
  grupos,
  bloqueado = false,
}: GruposEntraIdPanelProps) {
  const [agregando, setAgregando] = useState(false);
  const [paraDesasociar, setParaDesasociar] =
    useState<RolGrupoEntraIdResponse | null>(null);

  const canGestionar = useHasPermission(
    PermisosCanonicos.IdentidadRolesGruposEntraIdGestionar,
  );
  const puedeGestionar = canGestionar && !bloqueado;

  const idempotencyKey = useFormIdempotencyKey();
  const desasociar = useDesasociarGrupoEntraId();

  function handleConfirmarDesasociar() {
    if (paraDesasociar == null) return;
    const g = paraDesasociar;
    desasociar.mutate(
      { rolId, grupoId: g.id, idempotencyKey },
      {
        onSuccess: () => {
          toast.success(`Grupo "${g.nombre}" desasociado`);
          setParaDesasociar(null);
        },
        onError: (error) => {
          if (esApiError(error)) {
            toast.error(error.problem.title, {
              description: error.traceId
                ? `Código: ${error.traceId}`
                : undefined,
            });
          } else {
            toast.error('Error al desasociar el grupo.');
          }
          setParaDesasociar(null);
        },
      },
    );
  }

  return (
    <section className="space-y-3" aria-label="Grupos de Entra ID asociados">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h3 className="text-base font-semibold">Grupos de Entra ID</h3>
          <p className="text-xs text-muted-foreground">
            Microsoft Entra ID — usuarios miembros del grupo heredan el rol.
            Total: {grupos.length}.
          </p>
        </div>
        {puedeGestionar && !agregando && (
          <Button
            size="sm"
            variant="outline"
            onClick={() => setAgregando(true)}
          >
            <Plus className="mr-1 h-4 w-4" />
            Asociar grupo
          </Button>
        )}
      </header>

      {bloqueado && (
        <div
          role="status"
          className="rounded-md border border-amber-300 bg-amber-50/50 px-3 py-2 text-xs text-amber-900"
        >
          Los roles del sistema no admiten asociación de grupos.
        </div>
      )}

      {agregando && puedeGestionar && (
        <GrupoEntraIdInlineForm
          rolId={rolId}
          onCancel={() => setAgregando(false)}
          onSaved={() => setAgregando(false)}
        />
      )}

      {grupos.length === 0 ? (
        <div className="rounded-md border border-dashed bg-muted/20 px-4 py-6 text-center text-sm text-muted-foreground">
          No hay grupos asociados.
        </div>
      ) : (
        <ul className="divide-y rounded-md border bg-card">
          {grupos.map((g) => (
            <li key={g.id} className="px-3 py-2">
              <div className="flex flex-wrap items-center gap-3">
                <div className="min-w-0 flex-1 space-y-0.5">
                  <div className="truncate text-sm font-medium">
                    {g.nombre}
                  </div>
                  <div className="truncate font-mono text-xs text-muted-foreground">
                    {g.objectId}
                  </div>
                </div>
                {puedeGestionar && (
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={() => setParaDesasociar(g)}
                    aria-label={`Desasociar grupo ${g.nombre}`}
                  >
                    <Trash2 className="mr-1 h-3.5 w-3.5" />
                    Desasociar
                  </Button>
                )}
              </div>
            </li>
          ))}
        </ul>
      )}

      <AlertDialog
        open={paraDesasociar != null}
        onOpenChange={(open) => {
          if (!open) setParaDesasociar(null);
        }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Desasociar grupo</AlertDialogTitle>
            <AlertDialogDescription>
              ¿Confirmas desasociar el grupo{' '}
              <span className="font-semibold">{paraDesasociar?.nombre}</span>{' '}
              de este rol? Los usuarios miembros dejarán de heredar este rol
              al recargar su sesión.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={desasociar.isPending}>
              Cancelar
            </AlertDialogCancel>
            <AlertDialogAction
              onClick={handleConfirmarDesasociar}
              disabled={desasociar.isPending}
            >
              {desasociar.isPending ? 'Desasociando…' : 'Desasociar'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </section>
  );
}
