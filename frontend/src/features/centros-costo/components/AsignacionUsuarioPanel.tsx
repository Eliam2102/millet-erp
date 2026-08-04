import { ShieldCheck } from 'lucide-react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { esApiError } from '@/lib/api';
import { ArbolAsignacion } from '@/features/centros-costo/components/ArbolAsignacion';
import { ResumenAsignacionBar } from '@/features/centros-costo/components/ResumenAsignacionBar';
import {
  useArbolAsignacion,
  useMarcarAlcance,
} from '@/features/centros-costo/api/useAsignacion';
import type { MarcarAlcanceRequest } from '@/features/centros-costo/api/types';

interface AsignacionUsuarioPanelProps {
  /** Usuario cuyo alcance se ve/edita. Siempre resuelto: quien monta el panel
   * (página con selector o tab del detalle) garantiza el id. */
  usuarioId: string;
  /** Fuerza modo lectura desde afuera. Se agrega al bloqueo interno; no lo
   * reemplaza — `esAlcanceTotal` y el refetch en curso siguen deshabilitando
   * el árbol por su cuenta. Default: editable. */
  readOnly?: boolean;
}

/**
 * Panel de asignación de alcance de un usuario: árbol de 5 niveles con
 * tri-estado + barra resumen + banner de alcance total. Marcar un nivel =
 * POST que expande a máquinas; la UI se repinta del refetch (no re-deriva
 * tri-estado, no usa el response del POST). Usuario con `dim3.leer-todos`:
 * banner "alcance total" + árbol deshabilitado.
 *
 * <para>Componente compartido (extraído de <c>AsignacionCentrosCostoPage</c>):
 * lo consumen la pantalla <c>/centros-costo/asignaciones</c> (con su propio
 * <c>UsuarioSelector</c> arriba) y el tab "Centros de Costo" del detalle de
 * usuario. No trae header ni selector — el consumidor decide el chrome.</para>
 */
export function AsignacionUsuarioPanel({
  usuarioId,
  readOnly = false,
}: AsignacionUsuarioPanelProps) {
  const arbolQuery = useArbolAsignacion(usuarioId);
  const marcar = useMarcarAlcance(usuarioId);

  function onMarcar(req: MarcarAlcanceRequest) {
    marcar.mutate(req, {
      onError: (error) => {
        if (esApiError(error)) {
          toast.error(error.problem.title, {
            description: error.traceId ? `Código: ${error.traceId}` : undefined,
          });
          return;
        }
        toast.error('Error inesperado al marcar el alcance.');
      },
    });
  }

  const arbol = arbolQuery.data;
  const esAlcanceTotal = arbol?.esAlcanceTotal ?? false;
  // Deshabilita durante el marcado o el refetch (pending) — la UI no deja
  // clicar mientras el backend resuelve; el refetch repinta el estado real.
  // `readOnly` es un candado externo adicional (p. ej. superficie sin permiso
  // de edición); no sustituye a los otros dos.
  const bloqueado =
    readOnly || esAlcanceTotal || marcar.isPending || arbolQuery.isFetching;

  if (arbolQuery.isLoading) {
    return (
      <div className="space-y-2" data-testid="arbol-cargando">
        <div className="h-8 w-full animate-pulse rounded bg-muted" />
        <div className="h-8 w-full animate-pulse rounded bg-muted" />
      </div>
    );
  }

  if (arbolQuery.isError) {
    return (
      <div className="rounded-md border border-destructive/40 px-4 py-6 text-center text-sm text-destructive">
        No se pudo cargar el árbol de asignación.
      </div>
    );
  }

  if (!arbol) return null;

  return (
    <div className="space-y-4">
      {esAlcanceTotal && (
        <div
          className="flex items-start gap-2 rounded-md border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-900"
          data-testid="badge-alcance-total"
        >
          <ShieldCheck className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
          <span>
            <Badge className="mr-1 bg-amber-600 hover:bg-amber-600">
              Alcance total
            </Badge>
            Este usuario tiene alcance total (ve todas las máquinas sin
            asignación). Editar su alcance no cambia lo que ve — el árbol
            queda deshabilitado.
          </span>
        </div>
      )}

      <ResumenAsignacionBar resumen={arbol.resumen} />

      <ArbolAsignacion arbol={arbol} onMarcar={onMarcar} disabled={bloqueado} />
    </div>
  );
}
