import { useId, useState } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';
import { UbicacionSelector } from '@/components/erp/selectors/UbicacionSelector';
import { esApiError } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useCrearAsignacion } from '@/features/almacen/api/useAsignaciones';

export interface AsignarUbicacionEnLineaProps {
  articuloId: string;
  /** Se llama con el rack recién asignado, para elegirlo en la línea. */
  onAsignada: (ubicacionId: string) => void;
}

/**
 * Asigna un rack al artículo dentro de la misma línea (UX-F0-02, ALM-01): sin
 * sheet apilado. Va dentro de otro `<form>`, por eso no usa `<form>` propio y
 * sus botones son `type="button"`. Sin permiso muestra el aviso con la salida.
 */
export function AsignarUbicacionEnLinea({ articuloId, onAsignada }: AsignarUbicacionEnLineaProps) {
  const puede = useHasPermission(PermisosCanonicos.AlmacenAsignacionesAdministrar);
  const crear = useCrearAsignacion();
  const [abierto, setAbierto] = useState(false);
  const [ubicacionId, setUbicacionId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const labelId = useId();

  if (!puede) {
    return (
      <AvisoEnLinea tono="advertencia">
        Este artículo no tiene rack asignado. Pide a quien administra Almacén › Ubicación de
        artículos que lo asigne; después podrás elegirlo aquí.
      </AvisoEnLinea>
    );
  }

  if (!abierto) {
    return (
      <Button type="button" variant="link" size="sm" onClick={() => setAbierto(true)}>
        Asignar ubicación
      </Button>
    );
  }

  function guardar() {
    if (!ubicacionId) {
      setError('Elige la ubicación donde vivirá el artículo.');
      return;
    }
    setError(null);
    crear.mutate(
      { command: { articuloId, ubicacionId }, idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: () => {
          toast.success('Artículo asignado a la ubicación');
          onAsignada(ubicacionId);
          setAbierto(false);
          setUbicacionId(null);
        },
        onError: (e) => {
          if (esApiError(e)) {
            setError(
              e.code === 'ASIGNACION_DUPLICADA'
                ? 'El artículo ya está asignado a esta ubicación.'
                : e.code === 'ARTICULO_INACTIVO'
                  ? 'El artículo está inactivo; no se puede asignar.'
                  : (e.problem.detail ?? e.problem.title),
            );
          } else {
            setError('Error inesperado al asignar el artículo.');
          }
        },
      },
    );
  }

  return (
    <div className="space-y-2 rounded-md border border-dashed border-brand/40 bg-brand/5 p-3">
      <p id={labelId} className="text-xs font-medium">
        Asignar rack a este artículo
      </p>
      <div aria-labelledby={labelId} role="group">
        <UbicacionSelector
          value={ubicacionId}
          onChange={setUbicacionId}
          disabled={crear.isPending}
        />
      </div>
      {error && (
        <p role="alert" className="text-xs text-danger-fg">
          {error}
        </p>
      )}
      <div className="flex justify-end gap-2">
        <Button
          type="button"
          variant="ghost"
          size="sm"
          disabled={crear.isPending}
          onClick={() => setAbierto(false)}
        >
          Cancelar
        </Button>
        <Button type="button" size="sm" disabled={crear.isPending} onClick={guardar}>
          {crear.isPending ? 'Asignando…' : 'Asignar'}
        </Button>
      </div>
    </div>
  );
}
