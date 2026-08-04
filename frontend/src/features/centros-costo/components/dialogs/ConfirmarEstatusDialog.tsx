import { toast } from 'sonner';
import { useQueryClient } from '@tanstack/react-query';
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
import { useConflictDialog } from '@/components/erp/collaboration/conflict-dialog-context';
import { esApiError } from '@/lib/api';
import { etiquetaNivel, type NivelDim } from '@/features/centros-costo/lib/etiquetas';
import {
  useCambiarEstatusCatalogo,
  useDetalleCatalogo,
} from '@/features/centros-costo/api/useCatalogoCrud';
import type { RecursoCatalogo } from '@/features/centros-costo/api/keys';
import type {
  DesactivarDim1Resultado,
  DesactivarDim2Resultado,
} from '@/features/centros-costo/api/types';
import { handleCeCoMutationError } from '@/features/centros-costo/lib/handle-conflict';

/**
 * Confirmación de desactivar/reactivar (CECO-FE-PR2). La ADVERTENCIA de
 * cascada usa los conteos que el nodo del árbol ya trae
 * (dim2Vivas/dim3Vivas — el test de coherencia árbol↔cascada del backend
 * garantiza que la promesa coincide con el command, ADR-0049) y el toast
 * final RECONCILIA con los conteos REALES del response: si difieren, el
 * árbol estaba desactualizado y la invalidación lo recarga — la fuente
 * de verdad es siempre el response.
 *
 * <para>El diálogo carga el DETALLE al abrirse: eso cachea el ETag que
 * la mutación necesita para el If-Match (sin esto, desactivar desde el
 * árbol — sin haber abierto detalle — saldría sin header y daría 428).
 * Reactivar NUNCA cascada; el guardrail CECO_PADRE_INACTIVO del backend
 * responde 422 si el padre sigue inactivo.</para>
 */
export interface ConfirmarEstatusTarget {
  recurso: RecursoCatalogo;
  nivel: NivelDim;
  id: string;
  clave?: string;
  nombre: string;
  /** Conteos de vivos del nodo (solo dim1/dim2 — la promesa de cascada). */
  dim2Vivas?: number;
  dim3Vivas?: number;
}

interface ConfirmarEstatusDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  target: ConfirmarEstatusTarget;
  accion: 'desactivar' | 'reactivar';
}

export function ConfirmarEstatusDialog({
  open,
  onOpenChange,
  target,
  accion,
}: ConfirmarEstatusDialogProps) {
  const conflictDialog = useConflictDialog();
  const queryClient = useQueryClient();

  // Cachea el ETag para el If-Match de la mutación (ver doc del componente).
  const detalle = useDetalleCatalogo<{ version: number }>(
    open ? target.recurso : 'dim1',
    open ? target.id : null,
  );
  const cambiar = useCambiarEstatusCatalogo<unknown>(target.recurso);

  const etiqueta = etiquetaNivel(target.nivel, 'configuracion');
  const display = target.clave
    ? `${target.clave} — ${target.nombre}`
    : target.nombre;

  const advertencia = construirAdvertencia(target, accion);

  function confirmar() {
    cambiar.mutate(
      { id: target.id, accion },
      {
        onSuccess: (data) => {
          toast.success(
            accion === 'desactivar'
              ? `${etiqueta} desactivada`
              : `${etiqueta} reactivada`,
            { description: describirResultado(target, accion, data) },
          );
          onOpenChange(false);
        },
        onError: (error) => {
          onOpenChange(false);
          if (
            handleCeCoMutationError(error, { conflictDialog, queryClient })
          ) {
            return;
          }
          if (esApiError(error)) {
            toast.error(error.problem.title, {
              description: error.traceId
                ? `Código: ${error.traceId}`
                : undefined,
            });
            return;
          }
          toast.error('Error inesperado al cambiar el estatus.');
        },
      },
    );
  }

  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>
            {accion === 'desactivar' ? 'Desactivar' : 'Reactivar'} {etiqueta}
          </AlertDialogTitle>
          <AlertDialogDescription>
            <span className="font-medium">{display}</span>
            {advertencia && (
              <>
                <br />
                {advertencia}
              </>
            )}
            {accion === 'desactivar' && (
              <>
                <br />
                Nada se borra: la baja es lógica y los documentos históricos
                siguen resolviendo.
              </>
            )}
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={cambiar.isPending}>
            Cancelar
          </AlertDialogCancel>
          <AlertDialogAction
            onClick={(e) => {
              e.preventDefault();
              confirmar();
            }}
            disabled={cambiar.isPending || detalle.isLoading}
          >
            {cambiar.isPending
              ? 'Aplicando…'
              : accion === 'desactivar'
                ? 'Desactivar'
                : 'Reactivar'}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}

/** La PROMESA: conteos de vivos del nodo del árbol (denominador de la cascada). */
function construirAdvertencia(
  target: ConfirmarEstatusTarget,
  accion: 'desactivar' | 'reactivar',
): string | null {
  if (accion === 'reactivar') {
    return target.nivel === 'dim1'
      ? null
      : 'Reactivar NO reactiva descendientes y requiere el padre activo.';
  }
  const d2 = etiquetaNivel('dim2', 'configuracion');
  const d3 = etiquetaNivel('dim3', 'configuracion');
  if (target.nivel === 'dim1') {
    return `La baja es en CASCADA: desactivará ${target.dim2Vivas ?? 0} ${d2} y ${target.dim3Vivas ?? 0} ${d3} vivas bajo este nodo.`;
  }
  if (target.nivel === 'dim2') {
    return `La baja es en CASCADA: desactivará ${target.dim3Vivas ?? 0} ${d3} vivas bajo este nodo.`;
  }
  if (target.nivel === 'grupoDim2' || target.nivel === 'grupoDim3') {
    return 'Los grupos NO cascadan: solo se retira de la clasificación nueva.';
  }
  return null;
}

/** La RECONCILIACIÓN: conteos reales del response (fuente de verdad). */
function describirResultado(
  target: ConfirmarEstatusTarget,
  accion: 'desactivar' | 'reactivar',
  data: unknown,
): string | undefined {
  if (accion !== 'desactivar') return undefined;

  if (target.nivel === 'dim1') {
    const r = data as DesactivarDim1Resultado;
    const d2 = etiquetaNivel('dim2', 'configuracion');
    const d3 = etiquetaNivel('dim3', 'configuracion');
    const base = `Se desactivaron ${r.dim2Desactivadas} ${d2} y ${r.dim3Desactivadas} ${d3}.`;
    const difiere =
      r.dim2Desactivadas !== (target.dim2Vivas ?? 0) ||
      r.dim3Desactivadas !== (target.dim3Vivas ?? 0);
    return difiere
      ? `${base} El árbol estaba desactualizado; se recargó con el estado real.`
      : base;
  }
  if (target.nivel === 'dim2') {
    const r = data as DesactivarDim2Resultado;
    const d3 = etiquetaNivel('dim3', 'configuracion');
    const base = `Se desactivaron ${r.dim3Desactivadas} ${d3}.`;
    return r.dim3Desactivadas !== (target.dim3Vivas ?? 0)
      ? `${base} El árbol estaba desactualizado; se recargó con el estado real.`
      : base;
  }
  return undefined;
}
