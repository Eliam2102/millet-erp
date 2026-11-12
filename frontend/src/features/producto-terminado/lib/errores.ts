import { esApiError } from '@/lib/api';
import { errorUsuario } from '@/lib/error-usuario';

/** Mensaje del servidor (`title` del Problem Details) tal cual; si no es un error de la API, el texto genérico. */
export function mensajeApi(e: unknown): string {
  if (esApiError(e) && e.problem.title?.trim()) return e.problem.title;
  return errorUsuario(e);
}
