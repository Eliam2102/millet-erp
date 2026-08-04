/**
 * Barrel del cliente HTTP enriquecido. Importar desde
 * <c>@/lib/api</c> en lugar de los archivos individuales para mantener
 * imports cortos en hooks y features.
 */
export {
  apiRequest,
  MAX_IDEMPOTENCY_RETRIES,
  MAX_RETRY_AFTER_MS,
  type ApiRequestOptions,
  type ApiResponse,
  type SleepFn,
} from '@/lib/api/client';
export {
  ApiError,
  esApiError,
  esConflictoConcurrencia,
  esIdempotencyEnCurso,
  esPrecondicionRequerida,
  esErrorDeCliente,
  esErrorDeServidor,
  esCodigoEspecifico,
  type ProblemDetails,
} from '@/lib/api/error';
export { extractEtag, ifMatch } from '@/lib/api/etag';
export {
  useFormIdempotencyKey,
  useBodyScopedIdempotencyKey,
  idempotencyHeader,
} from '@/lib/api/idempotency';
export {
  applyServerErrors,
  type FormConSetError,
} from '@/lib/api/apply-server-errors';
