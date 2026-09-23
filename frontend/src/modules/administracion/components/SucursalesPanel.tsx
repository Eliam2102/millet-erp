import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { Briefcase, FolderTree, Pencil, Plus, PowerOff } from 'lucide-react';
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
  EstatusCatalogo,
  type SucursalResponse,
} from '@/modules/administracion/api/types';
import { useDesactivarSucursal } from '@/modules/administracion/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { SucursalInlineForm } from '@/modules/administracion/components/SucursalInlineForm';

/**
 * Panel "Sucursales" — usado por <c>SucursalesTopLevelPage</c>
 * (<c>/admin/sucursales</c>). Renderiza:
 *
 * <list>
 *   <item>Lista de sucursales existentes (Clave + Nombre + estatus).
 *         La clave enlaza al detalle standalone (<c>/admin/sucursales/$id</c>)
 *         donde se gestionan sus Departamentos/Puestos/Usuarios.</item>
 *   <item>Botón "Agregar sucursal" que monta el inline form (border
 *         dashed primary).</item>
 *   <item>Click en una row inactiva-friendly → expande inline form
 *         de edición (border amber). Solo una row editable a la vez.</item>
 *   <item>Botón "Desactivar" por row activa (alertdialog de confirm).
 *         La fila desactivada queda visible con badge "Inactiva".</item>
 * </list>
 */
export interface SucursalesPanelProps {
  empresaId: string;
  sucursales: readonly SucursalResponse[];
  /**
   * Cuando la empresa que se está viendo no coincide con la empresa
   * activa de la sesión, la gestión (alta/edición) queda bloqueada
   * aunque el usuario tenga el permiso — el backend resolvería la
   * escritura contra la empresa activa, no contra la que se ve.
   */
  bloqueadoPorEmpresaActiva?: boolean;
}

export function SucursalesPanel({
  empresaId,
  sucursales,
  bloqueadoPorEmpresaActiva = false,
}: SucursalesPanelProps) {
  const [agregando, setAgregando] = useState(false);
  const [editandoId, setEditandoId] = useState<string | null>(null);
  const [confirmDesactivar, setConfirmDesactivar] =
    useState<SucursalResponse | null>(null);

  const canGestionar =
    useHasPermission(PermisosCanonicos.AdminEmpresasSucursalesGestionar) &&
    !bloqueadoPorEmpresaActiva;

  const canGestionarMasterDeptos = useHasPermission(
    PermisosCanonicos.AdminDepartamentosGestionar,
  );
  const canGestionarMasterPuestos = useHasPermission(
    PermisosCanonicos.AdminPuestosGestionar,
  );

  const desactivar = useDesactivarSucursal();

  function handleConfirmarDesactivar() {
    if (confirmDesactivar == null) return;
    const target = confirmDesactivar;
    desactivar.mutate(
      { empresaId, id: target.id, idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: () => {
          toast.success(`Sucursal ${target.clave} desactivada`);
          setConfirmDesactivar(null);
        },
        onError: (error) => {
          if (esApiError(error)) {
            toast.error(error.problem.title, {
              description: error.traceId
                ? `Código: ${error.traceId}`
                : undefined,
            });
          } else {
            toast.error('Error al desactivar la sucursal.');
          }
          setConfirmDesactivar(null);
        },
      },
    );
  }

  return (
    <section className="space-y-3">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h3 className="text-base font-semibold">Sucursales</h3>
          <p className="text-xs text-muted-foreground">
            Catálogo organizacional compartido. Total: {sucursales.length}.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {canGestionarMasterDeptos && (
            <Button size="sm" variant="ghost" className="h-8 text-xs" asChild>
              <Link to="/admin/departamentos">
                <FolderTree className="mr-1.5 h-3.5 w-3.5 text-muted-foreground" />
                Catálogo Departamentos
              </Link>
            </Button>
          )}
          {canGestionarMasterPuestos && (
            <Button size="sm" variant="ghost" className="h-8 text-xs" asChild>
              <Link to="/admin/puestos">
                <Briefcase className="mr-1.5 h-3.5 w-3.5 text-muted-foreground" />
                Catálogo Puestos
              </Link>
            </Button>
          )}
          {canGestionar && !agregando && (
            <Button
              size="sm"
              variant="outline"
              className="h-8 text-xs"
              onClick={() => {
                setAgregando(true);
                setEditandoId(null);
              }}
            >
              <Plus className="mr-1 h-4 w-4" />
              Agregar sucursal
            </Button>
          )}
        </div>
      </header>

      {agregando && canGestionar && (
        <SucursalInlineForm
          empresaId={empresaId}
          onCancel={() => setAgregando(false)}
          onSaved={() => setAgregando(false)}
        />
      )}

      {sucursales.length === 0 ? (
        <div className="rounded-md border border-dashed bg-muted/20 px-4 py-6 text-center text-sm text-muted-foreground">
          No hay sucursales registradas.
        </div>
      ) : (
        <ul className="divide-y rounded-md border bg-card">
          {sucursales.map((s) => {
            const editando = editandoId === s.id;
            const activa = s.estatus === EstatusCatalogo.Activo;
            return (
              <li key={s.id} className="px-3 py-2">
                {editando && canGestionar ? (
                  <SucursalInlineForm
                    empresaId={empresaId}
                    sucursal={s}
                    onCancel={() => setEditandoId(null)}
                    onSaved={() => setEditandoId(null)}
                  />
                ) : (
                  <div className="flex flex-wrap items-center gap-3">
                    <Link
                      to="/admin/sucursales/$id"
                      params={{ id: s.id }}
                      className="font-mono text-sm font-semibold hover:underline"
                    >
                      {s.clave}
                    </Link>
                    <span className="flex-1 truncate text-sm">{s.nombre}</span>
                    {activa ? (
                      <Badge variant="secondary">Activa</Badge>
                    ) : (
                      <Badge variant="outline" className="text-muted-foreground">
                        Inactiva
                      </Badge>
                    )}
                    <div className="flex items-center gap-1">
                      {canGestionar && (
                        <>
                          <Button
                            variant="ghost"
                            size="sm"
                            onClick={() => {
                              setEditandoId(s.id);
                              setAgregando(false);
                            }}
                            aria-label={`Editar sucursal ${s.clave}`}
                          >
                            <Pencil className="h-3.5 w-3.5" />
                          </Button>
                          {activa && (
                            <Button
                              variant="ghost"
                              size="sm"
                              onClick={() => setConfirmDesactivar(s)}
                              aria-label={`Desactivar sucursal ${s.clave}`}
                            >
                              <PowerOff className="h-3.5 w-3.5" />
                            </Button>
                          )}
                        </>
                      )}
                    </div>
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
            <AlertDialogTitle>Desactivar sucursal</AlertDialogTitle>
            <AlertDialogDescription>
              ¿Confirmas desactivar la sucursal{' '}
              <span className="font-mono font-semibold">
                {confirmDesactivar?.clave}
              </span>
              ? Quedará oculta en los selectores pero conservará su
              histórico. La acción se puede revertir contactando al
              administrador del sistema.
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
    </section>
  );
}
