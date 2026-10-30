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
import { PageHeader } from '@/components/erp/PageHeader';
import { useState } from 'react';
import { Pencil, Plus, Power, PowerOff } from 'lucide-react';
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
import { ErrorState, TableSkeleton } from '@/components/erp';
import { EstatusCatalogo, type CanalVentaResponse } from '@/modules/administracion/api/types';
import { useActualizarCanalVenta, useCanalesVentaAdmin } from '@/modules/administracion/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError, useFormIdempotencyKey, codigoSoporte } from '@/lib/api';
import { CanalVentaInlineForm } from '@/modules/administracion/components/CanalVentaInlineForm';

/**
 * <c>&lt;CanalesVentaPage/&gt;</c> — catálogo administrable de Canales
 * de venta (FAC-ING-PR3; backend FAC-ING-PR2 en
 * <c>compartido.canales_venta</c>). Reemplaza el enum hardcodeado de
 * Facturación; los selectores de pedidos/emisión consumen el lookup de
 * solo-activos.
 *
 * <para>Mismo patrón que <c>SucursalesPanel</c>: lista + alta y edición
 * con inline forms (NUNCA modal), desactivar con confirm (el canal sale
 * de los selectores y de la resolución de ingesta A+W sin tocar el
 * histórico) y reactivar directo. La columna "Clave A+W" es el GRUPPE
 * con el que A+W refiere el canal en la ingesta de pedidos.</para>
 */
export function CanalesVentaPage() {
  const [agregando, setAgregando] = useState(false);
  const [editandoId, setEditandoId] = useState<number | null>(null);
  const [confirmDesactivar, setConfirmDesactivar] = useState<CanalVentaResponse | null>(null);

  const canGestionar = useHasPermission(PermisosCanonicos.AdminEmpresasSucursalesGestionar);

  const query = useCanalesVentaAdmin();
  const canales = query.data ?? [];

  const idempotencyKey = useFormIdempotencyKey();
  const actualizar = useActualizarCanalVenta();

  function cambiarEstatus(canal: CanalVentaResponse, estatus: EstatusCatalogo, mensajeOk: string) {
    actualizar.mutate(
      { id: canal.id, payload: { estatus }, idempotencyKey },
      {
        onSuccess: () => {
          toast.success(mensajeOk);
          setConfirmDesactivar(null);
        },
        onError: (error) => {
          if (esApiError(error)) {
            toast.error(error.problem.title, {
              description: codigoSoporte(error),
            });
          } else {
            toast.error('Error al actualizar el canal de venta.');
          }
          setConfirmDesactivar(null);
        },
      },
    );
  }

  return (
    <div className="space-y-5">
      <PageHeader
        titulo={<>Canales de venta</>}
        descripcion={
          <>
            Eje organizacional de la facturación (de dónde viene la venta). La clave A+W es el
            GRUPPE con el que A+W refiere el canal en la ingesta de pedidos. Total: {canales.length}
            .
          </>
        }
        acciones={
          <>
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
                Agregar canal
              </Button>
            )}
          </>
        }
      />

      {agregando && canGestionar && (
        <CanalVentaInlineForm
          onCancel={() => setAgregando(false)}
          onSaved={() => setAgregando(false)}
        />
      )}

      {query.isError ? (
        <ErrorState
          title="No se pudieron cargar los canales de venta"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton rows={5} />
      ) : canales.length === 0 ? (
        <div className="rounded-md border border-dashed bg-surface-muted/20 px-4 py-6 text-center text-sm text-ink-muted">
          No hay canales de venta registrados.
        </div>
      ) : (
        <ListadoCatalogo
          items={canales}
          texto={(c) => c.nombre + ' ' + (c.claveAw ?? '')}
          estado={(c) => c.estatus}
          opciones={OPCIONES_CATALOGO}
        >
          {(visibles) => (
            <TableContainer maxHeightClassName="">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Código</TableHead>
                  <TableHead>Canal</TableHead>
                  <TableHead>Clave A+W</TableHead>
                  <TableHead>Estado</TableHead>
                  <TableHead>Acciones</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {visibles.map((c) => {
                  const editando = editandoId === c.id;
                  const activo = c.estatus === EstatusCatalogo.Activo;
                  return (
                    <TableRow key={c.id} className="px-3 py-2">
                      {editando && canGestionar ? (
                        <TableCell colSpan={5}>
                          <CanalVentaInlineForm
                            canal={c}
                            onCancel={() => setEditandoId(null)}
                            onSaved={() => setEditandoId(null)}
                          />
                        </TableCell>
                      ) : (
                        <>
                          <TableCell className="w-32">
                            <span className="w-8 text-right font-mono text-xs text-ink-muted">
                              {c.id}
                            </span>
                          </TableCell>
                          <TableCell className="min-w-48">
                            <span className="flex-1 truncate text-sm">{c.nombre}</span>
                          </TableCell>
                          <TableCell className="w-32">
                            <span
                              className="w-28 truncate font-mono text-xs"
                              title="Clave A+W (GRUPPE de la ingesta de pedidos)"
                            >
                              {c.claveAw ?? '—'}
                            </span>
                          </TableCell>
                          <TableCell className="w-32">
                            {activo ? (
                              <Badge variant="success">Activo</Badge>
                            ) : (
                              <Badge variant="neutral">
                                {c.estatus === EstatusCatalogo.EnRevision
                                  ? 'En revisión'
                                  : 'Inactivo'}
                              </Badge>
                            )}
                          </TableCell>
                          <TableCell className="w-28">
                            {canGestionar && (
                              <div className="flex items-center gap-1">
                                <Button
                                  variant="ghost"
                                  size="sm"
                                  onClick={() => {
                                    setEditandoId(c.id);
                                    setAgregando(false);
                                  }}
                                  aria-label={`Editar canal ${c.nombre}`}
                                >
                                  <Pencil className="h-3.5 w-3.5" />
                                </Button>
                                {activo ? (
                                  <Button
                                    variant="ghost"
                                    size="sm"
                                    onClick={() => setConfirmDesactivar(c)}
                                    aria-label={`Desactivar canal ${c.nombre}`}
                                  >
                                    <PowerOff className="h-3.5 w-3.5" />
                                  </Button>
                                ) : (
                                  <Button
                                    variant="ghost"
                                    size="sm"
                                    disabled={actualizar.isPending}
                                    onClick={() =>
                                      cambiarEstatus(
                                        c,
                                        EstatusCatalogo.Activo,
                                        `Canal "${c.nombre}" reactivado`,
                                      )
                                    }
                                    aria-label={`Reactivar canal ${c.nombre}`}
                                  >
                                    <Power className="h-3.5 w-3.5" />
                                  </Button>
                                )}
                              </div>
                            )}
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
            <AlertDialogTitle>Desactivar canal de venta</AlertDialogTitle>
          </AlertDialogHeader>
          <DialogBody>
            <AlertDialogDescription>
              ¿Confirmas desactivar el canal{' '}
              <span className="font-semibold">{confirmDesactivar?.nombre}</span>? Saldrá de los
              selectores de pedidos/emisión y de la resolución de la ingesta A+W, pero los
              documentos existentes conservan su histórico. Se puede reactivar en cualquier momento.
            </AlertDialogDescription>
          </DialogBody>
          <AlertDialogFooter className="border-t border-line-divider bg-surface-subtle px-6 py-4">
            <AlertDialogCancel disabled={actualizar.isPending}>Cancelar</AlertDialogCancel>
            <AlertDialogAction
              onClick={() => {
                if (confirmDesactivar == null) return;
                cambiarEstatus(
                  confirmDesactivar,
                  EstatusCatalogo.Inactivo,
                  `Canal "${confirmDesactivar.nombre}" desactivado`,
                );
              }}
              disabled={actualizar.isPending}
            >
              {actualizar.isPending ? 'Desactivando…' : 'Desactivar'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}
