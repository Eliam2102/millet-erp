import { toast } from 'sonner';
import { codigoSoporte, esApiError } from '@/lib/api';

/**
 * Toast de error de las acciones de cancelación (F6.5). En un 422 el backend
 * manda el mismo texto en título y detalle: el detalle solo se muestra si
 * agrega algo (O1).
 */
export function mostrarError(error: unknown, generico: string) {
  if (!esApiError(error)) {
    toast.error(generico);
    return;
  }
  const { title, detail } = error.problem;
  toast.error(title, {
    description: detail && detail !== title ? detail : codigoSoporte(error),
  });
}
