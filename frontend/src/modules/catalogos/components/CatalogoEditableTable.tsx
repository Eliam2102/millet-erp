import { Fragment, useState, type ReactNode } from 'react';
import { Pencil, PowerOff, X } from 'lucide-react';
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
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import { EstatusCatalogo } from '@/modules/catalogos/api/types';
import { toast } from 'sonner';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;CatalogoEditableTable/&gt;</c> — componente genérico que
 * consolida el patrón cross-recurso de los 4 catálogos editables del
 * Grupo 2 (CondicionesPago, Incoterms, Transportistas,
 * UsosPrincipales). Cada recurso tiene 1-3 columnas extra; el
 * <c>renderInlineEditForm</c> sigue siendo específico (los fields
 * difieren en tipo y validación).
 *
 * <para>Render: tabla con columnas declarativas + acciones inline
 * (Editar / Desactivar / Reactivar). Click en Editar abre inline form
 * (border-dashed amber) DEBAJO de la fila — la fila original queda
 * visible. Confirm con AlertDialog para Desactivar; reactivar es
 * directo (acción reversible).</para>
 */
export interface CatalogoColumn<T> {
  key: string;
  label: string;
  /** Render del valor de la celda; default es <c>String(item[key])</c>. */
  render?: (item: T) => ReactNode;
  className?: string;
}

export interface DesactivarArgs {
  id: string;
  idempotencyKey: string;
}

export interface CatalogoEditableTableProps<T extends { id: string; estatus: EstatusCatalogo }> {
  recurso: string;
  recursoSingular: string;
  items: readonly T[];
  isLoading: boolean;
  isError: boolean;
  error: unknown;
  onRetry: () => void;
  columns: ReadonlyArray<CatalogoColumn<T>>;
  /** Render del inline form de edición (mode-specific). */
  renderInlineEditForm: (item: T, onClose: () => void) => ReactNode;
  /** Hook de mutación de desactivar. */
  desactivar: {
    mutate: (
      args: DesactivarArgs,
      callbacks: {
        onSuccess: () => void;
        onError: (err: unknown) => void;
      },
    ) => void;
    isPending: boolean;
  };
  canEditar: boolean;
  canDesactivar: boolean;
  emptyMessage?: string;
  /** Action prop para el EmptyState (típicamente botón "Nuevo X"). */
  emptyAction?: ReactNode;
}

export function CatalogoEditableTable<
  T extends { id: string; estatus: EstatusCatalogo },
>({
  recurso,
  recursoSingular,
  items,
  isLoading,
  isError,
  error,
  onRetry,
  columns,
  renderInlineEditForm,
  desactivar,
  canEditar,
  canDesactivar,
  emptyMessage,
  emptyAction,
}: CatalogoEditableTableProps<T>) {
  const idempotencyKey = useFormIdempotencyKey();
  const [editandoId, setEditandoId] = useState<string | null>(null);
  const [confirmDesactivarId, setConfirmDesactivarId] = useState<string | null>(
    null,
  );

  if (isLoading) {
    return (
      <TableSkeleton
        rows={5}
        columns={columns.map(() => ({ width: 'w-full' as const }))}
      />
    );
  }

  if (isError) {
    const problem = esApiError(error) ? error.problem : undefined;
    return <ErrorState problem={problem} onRetry={onRetry} />;
  }

  if (items.length === 0) {
    return (
      <EmptyState
        title={`Aún no hay ${recurso}.`}
        description={emptyMessage}
        action={emptyAction}
      />
    );
  }

  const totalCols = columns.length + 2; // +1 estatus, +1 acciones

  function handleConfirmarDesactivar(id: string) {
    desactivar.mutate(
      { id, idempotencyKey },
      {
        onSuccess: () => {
          toast.success(`${recursoSingular} desactivado`);
          setConfirmDesactivarId(null);
        },
        onError: (err) => {
          if (esApiError(err)) {
            toast.error(err.problem.title, {
              description: err.traceId ? `Código: ${err.traceId}` : undefined,
            });
          } else {
            toast.error(`Error al desactivar el ${recursoSingular.toLowerCase()}.`);
          }
          setConfirmDesactivarId(null);
        },
      },
    );
  }

  return (
    <>
      <div className="overflow-x-auto rounded-md border bg-card">
        <table className="w-full text-sm">
          <thead className="border-b bg-muted/30 text-xs uppercase text-muted-foreground">
            <tr>
              {columns.map((c) => (
                <th
                  key={c.key}
                  scope="col"
                  className={cn('px-3 py-2 text-left', c.className)}
                >
                  {c.label}
                </th>
              ))}
              <th scope="col" className="px-3 py-2 text-left">
                Estatus
              </th>
              <th scope="col" className="px-3 py-2 text-right">
                Acciones
              </th>
            </tr>
          </thead>
          <tbody className="divide-y">
            {items.map((item) => {
              const activo = item.estatus === EstatusCatalogo.Activo;
              const editando = editandoId === item.id;
              return (
                <Fragment key={item.id}>
                  <tr className={editando ? 'bg-muted/30' : undefined}>
                    {columns.map((c) => (
                      <td key={c.key} className={cn('px-3 py-2', c.className)}>
                        {c.render
                          ? c.render(item)
                          : String((item as Record<string, unknown>)[c.key] ?? '')}
                      </td>
                    ))}
                    <td className="px-3 py-2">
                      {activo ? (
                        <Badge variant="secondary">Activo</Badge>
                      ) : (
                        <Badge
                          variant="outline"
                          className="text-muted-foreground"
                        >
                          Inactivo
                        </Badge>
                      )}
                    </td>
                    <td className="px-3 py-2">
                      <div className="flex items-center justify-end gap-1">
                        {canEditar && (
                          <Button
                            type="button"
                            size="sm"
                            variant="ghost"
                            onClick={() =>
                              setEditandoId(editando ? null : item.id)
                            }
                            aria-label={editando ? 'Cancelar edición' : 'Editar'}
                          >
                            {editando ? (
                              <X className="h-4 w-4" />
                            ) : (
                              <Pencil className="h-4 w-4" />
                            )}
                          </Button>
                        )}
                        {canDesactivar && activo && (
                          <Button
                            type="button"
                            size="sm"
                            variant="ghost"
                            onClick={() => setConfirmDesactivarId(item.id)}
                            aria-label="Desactivar"
                          >
                            <PowerOff className="h-4 w-4" />
                          </Button>
                        )}
                      </div>
                    </td>
                  </tr>
                  {editando && (
                    <tr>
                      <td colSpan={totalCols} className="bg-amber-50/40 p-3">
                        {renderInlineEditForm(item, () => setEditandoId(null))}
                      </td>
                    </tr>
                  )}
                </Fragment>
              );
            })}
          </tbody>
        </table>
      </div>

      <AlertDialog
        open={confirmDesactivarId != null}
        onOpenChange={(open) => {
          if (!open) setConfirmDesactivarId(null);
        }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Desactivar {recursoSingular.toLowerCase()}</AlertDialogTitle>
            <AlertDialogDescription>
              ¿Confirmas desactivar este {recursoSingular.toLowerCase()}? Los
              registros históricos siguen funcionando; las nuevas referencias
              quedan bloqueadas. La acción es reversible (reactivar).
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={desactivar.isPending}>
              Cancelar
            </AlertDialogCancel>
            <AlertDialogAction
              onClick={() => {
                if (confirmDesactivarId != null) {
                  handleConfirmarDesactivar(confirmDesactivarId);
                }
              }}
              disabled={desactivar.isPending}
            >
              {desactivar.isPending ? 'Desactivando…' : 'Desactivar'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  );
}
