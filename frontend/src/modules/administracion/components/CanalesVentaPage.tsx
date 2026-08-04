import { useState } from 'react';
import { Pencil, Plus, Power, PowerOff, Share2 } from 'lucide-react';
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
import {
  EstatusCatalogo,
  type CanalVentaResponse,
} from '@/modules/administracion/api/types';
import {
  useActualizarCanalVenta,
  useCanalesVentaAdmin,
} from '@/modules/administracion/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
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
  const [confirmDesactivar, setConfirmDesactivar] =
    useState<CanalVentaResponse | null>(null);

  const canGestionar = useHasPermission(
    PermisosCanonicos.AdminEmpresasSucursalesGestionar,
  );

  const query = useCanalesVentaAdmin();
  const canales = query.data ?? [];

  const idempotencyKey = useFormIdempotencyKey();
  const actualizar = useActualizarCanalVenta();

  function cambiarEstatus(
    canal: CanalVentaResponse,
    estatus: EstatusCatalogo,
    mensajeOk: string,
  ) {
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
              description: error.traceId
                ? `Código: ${error.traceId}`
                : undefined,
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
    <div className="mx-auto max-w-4xl space-y-4 p-4">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h1 className="flex items-center gap-2 text-lg font-semibold">
            <Share2 className="h-5 w-5" aria-hidden="true" />
            Canales de venta
          </h1>
          <p className="text-sm text-muted-foreground">
            Eje organizacional de la facturación (de dónde viene la venta).
            La clave A+W es el GRUPPE con el que A+W refiere el canal en la
            ingesta de pedidos. Total: {canales.length}.
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
            Agregar canal
          </Button>
        )}
      </header>

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
        <div className="rounded-md border border-dashed bg-muted/20 px-4 py-6 text-center text-sm text-muted-foreground">
          No hay canales de venta registrados.
        </div>
      ) : (
        <ul className="divide-y rounded-md border bg-card">
          {canales.map((c) => {
            const editando = editandoId === c.id;
            const activo = c.estatus === EstatusCatalogo.Activo;
            return (
              <li key={c.id} className="px-3 py-2">
                {editando && canGestionar ? (
                  <CanalVentaInlineForm
                    canal={c}
                    onCancel={() => setEditandoId(null)}
                    onSaved={() => setEditandoId(null)}
                  />
                ) : (
                  <div className="flex flex-wrap items-center gap-3">
                    <span className="w-8 text-right font-mono text-xs text-muted-foreground">
                      {c.id}
                    </span>
                    <span className="flex-1 truncate text-sm">{c.nombre}</span>
                    <span
                      className="w-28 truncate font-mono text-xs"
                      title="Clave A+W (GRUPPE de la ingesta de pedidos)"
                    >
                      {c.claveAw ?? '—'}
                    </span>
                    {activo ? (
                      <Badge variant="secondary">Activo</Badge>
                    ) : (
                      <Badge variant="outline" className="text-muted-foreground">
                        {c.estatus === EstatusCatalogo.EnRevision
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
            <AlertDialogTitle>Desactivar canal de venta</AlertDialogTitle>
            <AlertDialogDescription>
              ¿Confirmas desactivar el canal{' '}
              <span className="font-semibold">{confirmDesactivar?.nombre}</span>
              ? Saldrá de los selectores de pedidos/emisión y de la
              resolución de la ingesta A+W, pero los documentos existentes
              conservan su histórico. Se puede reactivar en cualquier
              momento.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={actualizar.isPending}>
              Cancelar
            </AlertDialogCancel>
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
