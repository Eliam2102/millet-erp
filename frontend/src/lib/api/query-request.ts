import type { QueryFunctionContext } from '@tanstack/react-query';
import { apiRequest, type ApiResponse } from './client';

/**
 * Deja terminar el montaje antes de consumir el signal del observador.
 * StrictMode retira y repone suscripciones en el mismo turno: consumirlo
 * sin esperar cancela la consulta compartida y vuelve a iniciar el HTTP.
 * Después del montaje se conservan la cancelación y la caché de TanStack.
 */
export async function apiQueryRequest<T>(
  path: string,
  context: Pick<QueryFunctionContext, 'signal'>,
): Promise<ApiResponse<T>> {
  await Promise.resolve();
  const signal = context.signal;
  signal.throwIfAborted();
  return apiRequest<T>(path, { signal });
}
