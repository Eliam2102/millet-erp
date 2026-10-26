import { DialogBody } from '@/components/ui/dialog';
import { ConfirmacionContent as AlertDialogContent } from '@/modules/administracion/components/ConfirmacionContent';
import {
  Table,
  TableHeader,
  TableRow,
  TableHead,
  TableBody,
  TableCell,
  TableContainer,
} from '@/components/ui/table';
import { ListadoCatalogo } from '@/modules/administracion/components/ListadoCatalogo';
import { OPCIONES_CATALOGO } from '@/modules/administracion/lib/estados-ui';
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
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import {
  EstatusCatalogo,
  type SucursalResponse,
  TipoSucursal,
} from '@/modules/administracion/api/types';
import { useDesactivarSucursal } from '@/modules/administracion/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError, codigoSoporte } from '@/lib/api';
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
  const [confirmDesactivar, setConfirmDesactivar] = useState<SucursalResponse | null>(null);

  const canGestionar =
    useHasPermission(PermisosCanonicos.AdminEmpresasSucursalesGestionar) &&
    !bloqueadoPorEmpresaActiva;

  const canGestionarMasterDeptos = useHasPermission(PermisosCanonicos.AdminDepartamentosGestionar);
  const canGestionarMasterPuestos = useHasPermission(PermisosCanonicos.AdminPuestosGestionar);

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
              description: codigoSoporte(error),
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
          <p className="text-xs text-ink-muted">
            Catálogo organizacional compartido. Total: {sucursales.length}.
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {canGestionarMasterDeptos && (
            <Button size="sm" variant="ghost" className="h-8 text-xs" asChild>
              <Link to="/admin/departamentos">
                <FolderTree className="mr-1.5 h-3.5 w-3.5 text-ink-muted" />
                Catálogo Departamentos
              </Link>
            </Button>
          )}
          {canGestionarMasterPuestos && (
            <Button size="sm" variant="ghost" className="h-8 text-xs" asChild>
              <Link to="/admin/puestos">
                <Briefcase className="mr-1.5 h-3.5 w-3.5 text-ink-muted" />
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
        <div className="rounded-md border border-dashed bg-surface-muted/20 px-4 py-6 text-center text-sm text-ink-muted">
          No hay sucursales registradas.
        </div>
      ) : (
        <ListadoCatalogo
          items={sucursales}
          texto={(s) => s.clave + ' ' + s.nombre}
          estado={(s) => s.estatus}
          opciones={OPCIONES_CATALOGO}
        >
          {(visibles) => (
            <TableContainer maxHeightClassName="">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Clave</TableHead>
                  <TableHead>Sucursal</TableHead>
                  <TableHead>Tipo</TableHead>
                  <TableHead>Estado</TableHead>
                  <TableHead>Acciones</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {visibles.map((s) => {
                  const editando = editandoId === s.id;
                  const activa = s.estatus === EstatusCatalogo.Activo;
                  return (
                    <TableRow key={s.id} className="px-3 py-2">
                      {editando && canGestionar ? (
                        <TableCell colSpan={5}>
                          <SucursalInlineForm
                            empresaId={empresaId}
                            sucursal={s}
                            onCancel={() => setEditandoId(null)}
                            onSaved={() => setEditandoId(null)}
                          />
                        </TableCell>
                      ) : (
                        <>
                          <TableCell className="w-32">
                            <Link
                              to="/admin/sucursales/$id"
                              params={{ id: s.id }}
                              className="font-mono text-sm font-semibold hover:underline"
                            >
                              {s.clave}
                            </Link>
                          </TableCell>
                          <TableCell className="min-w-48">
                            <span className="flex-1 truncate text-sm">{s.nombre}</span>
                          </TableCell>
                          <TableCell className="w-32">
                            {s.tipo === TipoSucursal.Planta ? (
                              <Badge variant="info">Planta</Badge>
                            ) : (
                              <Badge variant="info">Taller</Badge>
                            )}
                          </TableCell>
                          <TableCell className="w-32">
                            {activa ? (
                              <Badge variant="success">Activa</Badge>
                            ) : (
                              <Badge variant="neutral">Inactiva</Badge>
                            )}
                          </TableCell>
                          <TableCell className="w-28">
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
                          </TableCell>
                        </>
                      )}
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
            </TableContainer>
          )}
        </ListadoCatalogo>
      )}

      <AlertDialog
        open={confirmDesactivar != null}
        onOpenChange={(open) => {
          if (!open) setConfirmDesactivar(null);
        }}
      >
        <AlertDialogContent>
          <AlertDialogHeader className="px-6 pt-5 pb-3">
            <AlertDialogTitle>Desactivar sucursal</AlertDialogTitle>
          </AlertDialogHeader>
          <DialogBody>
            <AlertDialogDescription>
              ¿Confirmas desactivar la sucursal{' '}
              <span className="font-mono font-semibold">{confirmDesactivar?.clave}</span>? Quedará
              oculta en los selectores pero conservará su histórico. La acción se puede revertir
              contactando al administrador del sistema.
            </AlertDialogDescription>
          </DialogBody>
          <AlertDialogFooter className="border-t border-line-divider bg-surface-subtle px-6 py-4">
            <AlertDialogCancel disabled={desactivar.isPending}>Cancelar</AlertDialogCancel>
            <AlertDialogAction onClick={handleConfirmarDesactivar} disabled={desactivar.isPending}>
              {desactivar.isPending ? 'Desactivando…' : 'Desactivar'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </section>
  );
}
