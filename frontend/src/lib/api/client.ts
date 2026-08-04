import { apiFetch } from '@/lib/auth/api-client';
import { ApiError, type ProblemDetails } from '@/lib/api/error';
import { extractEtag } from '@/lib/api/etag';
import { idempotencyHeader } from '@/lib/api/idempotency';

/**
 * Cliente HTTP enriquecido (doc 05 §7.1). Construye sobre
 * <see cref="apiFetch"/> (que ya inyecta <c>Authorization</c> y limpia
 * sesión en 401) las cuatro capacidades que ningún endpoint del ERP
 * debería implementar a mano:
 *
 * 1. Parseo de <c>application/problem+json</c> (ADR-0010) → lanza
 *    <see cref="ApiError"/> tipado.
 * 2. Inyección de <c>Idempotency-Key</c> (ADR-0020) cuando el caller
 *    lo pasa.
 * 3. Inyección de <c>If-Match: "&lt;etag&gt;"</c> (ADR-0012 Capa 1)
 *    para detección temprana de conflictos.
 * 4. Captura de <c>ETag</c> en la respuesta (para guardarlo en la
 *    cache de TanStack Query y reusarlo en el siguiente
 *    <c>If-Match</c>).
 *
 * <para>Reintenta automáticamente <c>409 IDEMPOTENCY_IN_PROGRESS</c>
 * respetando <c>Retry-After</c> (hasta <see cref="MAX_IDEMPOTENCY_RETRIES"/>
 * intentos). Otros 409 (incluido <c>CONCURRENCY_CONFLICT</c>) se
 * propagan al caller — la UI los traduce a
 * <c>&lt;ConflictResolutionDialog/&gt;</c>.</para>
 *
 * <para>Cualquier mutation que conviva con TanStack Query debe pasar
 * por este cliente; <see cref="apiFetch"/> queda solo para casos de
 * auth (login flow) que no quieren el parseo de problem+json.</para>
 */

/** Máximo número de reintentos de <c>IDEMPOTENCY_IN_PROGRESS</c>. */
export const MAX_IDEMPOTENCY_RETRIES = 5;

/**
 * Tope superior por intento, para evitar que un <c>Retry-After</c>
 * patológico (servicio caído pidiendo "vuelve en 1h") cuelgue la UI.
 * Si el backend pide más, el cliente espera este máximo y reintenta;
 * el siguiente reintento decidirá si propagar el error.
 */
export const MAX_RETRY_AFTER_MS = 5_000;

/**
 * <c>setTimeout</c> reemplazable por tests (los tests inyectan un
 * scheduler controlado para no esperar tiempo real).
 */
export type SleepFn = (ms: number) => Promise<void>;

const defaultSleep: SleepFn = (ms) =>
  new Promise((resolve) => setTimeout(resolve, ms));

export interface ApiRequestOptions extends Omit<RequestInit, 'body'> {
  /**
   * Body como objeto JSON (se serializa con <c>JSON.stringify</c>) o
   * como <c>BodyInit</c> nativo (string, Blob, FormData...). Si es
   * objeto, el cliente fija <c>Content-Type: application/json</c>
   * automáticamente.
   */
  body?: unknown;
  /** Header <c>Idempotency-Key</c> (ADR-0020). */
  idempotencyKey?: string | null;
  /** ETag conocido para enviar como <c>If-Match</c> (ADR-0012). */
  ifMatch?: string | null;
  /** Permite a tests inyectar un sleeper determinista. */
  sleep?: SleepFn;
}

export interface ApiResponse<T> {
  /**
   * Cuerpo parseado de la respuesta. Si la respuesta es <c>204 No
   * Content</c>, viene como <c>undefined</c> (el caller debe tipar
   * <c>T = void</c> o tolerar <c>undefined</c>).
   */
  data: T;
  /**
   * Valor del header <c>ETag</c> (sin comillas) o <c>undefined</c> si
   * el endpoint no lo emite. Solo presente en <c>GET /{id}</c> de
   * detalle hoy.
   */
  etag?: string;
  /** HTTP status code de la respuesta. */
  status: number;
}

/**
 * Realiza una request HTTP enriquecida. Lanza <see cref="ApiError"/>
 * en cualquier <c>!response.ok</c>; devuelve <c>ApiResponse&lt;T&gt;</c>
 * en caso de éxito.
 *
 * @example
 * ```ts
 * // GET con tipado:
 * const { data: rq, etag } = await apiRequest<RequisicionResponse>(
 *   `/api/v1/compras/requisiciones/${id}`,
 * );
 *
 * // POST con idempotency + If-Match:
 * await apiRequest('/api/v1/compras/requisiciones', {
 *   method: 'POST',
 *   body: command,
 *   idempotencyKey: key,
 *   ifMatch: etag,
 * });
 * ```
 */
export async function apiRequest<T = unknown>(
  path: string,
  options: ApiRequestOptions = {},
): Promise<ApiResponse<T>> {
  const sleep = options.sleep ?? defaultSleep;

  for (let intento = 0; intento <= MAX_IDEMPOTENCY_RETRIES; intento++) {
    const respuesta = await ejecutarUnIntento<T>(path, options);
    if (respuesta.tipo === 'ok') return respuesta.payload;
    if (respuesta.tipo === 'error') throw respuesta.error;

    // tipo === 'reintentar': esperar Retry-After y volver a intentar.
    const espera = Math.min(
      respuesta.retryAfterMs ?? 100,
      MAX_RETRY_AFTER_MS,
    );
    await sleep(espera);
  }

  // Agotamos los reintentos; lanzar el último error como propagación
  // normal (el last-known sería IDEMPOTENCY_IN_PROGRESS — ahora lo
  // tratamos como falla).
  throw new ApiError(
    {
      type: 'about:blank',
      title: 'Idempotency conflict',
      status: 409,
      code: 'IDEMPOTENCY_IN_PROGRESS',
      detail: `El servidor sigue procesando la operación tras ${MAX_IDEMPOTENCY_RETRIES} reintentos.`,
    },
    409,
  );
}

type ResultadoIntento<T> =
  | { tipo: 'ok'; payload: ApiResponse<T> }
  | { tipo: 'error'; error: ApiError }
  | { tipo: 'reintentar'; retryAfterMs: number | null };

async function ejecutarUnIntento<T>(
  path: string,
  options: ApiRequestOptions,
): Promise<ResultadoIntento<T>> {
  const { body, idempotencyKey, ifMatch, sleep, ...rest } = options;
  // El reintento de IDEMPOTENCY_IN_PROGRESS lo orquesta apiRequest, no
  // este paso — descartamos sleep aquí.
  void sleep;

  const headers = new Headers(rest.headers);

  // Body: si es un objeto plano (no string/FormData/Blob/etc.), lo
  // serializamos como JSON y fijamos Content-Type.
  let bodyFinal: BodyInit | null | undefined;
  if (body == null) {
    bodyFinal = null;
  } else if (esBodyNativo(body)) {
    bodyFinal = body;
  } else {
    bodyFinal = JSON.stringify(body);
    if (!headers.has('Content-Type')) {
      headers.set('Content-Type', 'application/json');
    }
  }

  // Idempotency-Key: solo si el caller lo pidió. ADR-0020 lista qué
  // endpoints lo requieren; la responsabilidad recae en el hook.
  for (const [k, v] of Object.entries(idempotencyHeader(idempotencyKey))) {
    headers.set(k, v);
  }

  // If-Match: solo si tenemos un ETag conocido.
  if (ifMatch) headers.set('If-Match', `"${ifMatch}"`);

  const response = await apiFetch(path, {
    ...rest,
    headers,
    body: bodyFinal,
  });

  if (response.ok) {
    const etag = extractEtag(response);

    // 204 No Content: no hay cuerpo que parsear.
    if (response.status === 204) {
      return {
        tipo: 'ok',
        payload: { data: undefined as T, etag, status: response.status },
      };
    }

    const data = (await response.json()) as T;
    return {
      tipo: 'ok',
      payload: { data, etag, status: response.status },
    };
  }

  // Path de error: parsear ProblemDetails si el Content-Type lo
  // anuncia, o construir un ApiError genérico.
  const problem = await leerProblemDetails(response);
  const retryAfterSeconds = parseRetryAfter(response.headers.get('Retry-After'));
  const error = new ApiError(
    problem,
    response.status,
    retryAfterSeconds,
  );

  // Reintento automático solo en IDEMPOTENCY_IN_PROGRESS — el resto
  // (incluido CONCURRENCY_CONFLICT y otros IDEMPOTENCY_*) se propaga.
  if (
    response.status === 409 &&
    problem.code === 'IDEMPOTENCY_IN_PROGRESS'
  ) {
    return {
      tipo: 'reintentar',
      retryAfterMs: retryAfterSeconds != null ? retryAfterSeconds * 1000 : null,
    };
  }

  return { tipo: 'error', error };
}

/**
 * Intenta leer el body como <c>ProblemDetails</c>. Si el Content-Type
 * no es <c>application/problem+json</c> o el body no es JSON parseable,
 * devuelve un payload sintetizado a partir del status line — sin
 * desarmar el flujo.
 */
async function leerProblemDetails(
  response: Response,
): Promise<ProblemDetails> {
  const contentType = response.headers.get('Content-Type') ?? '';
  if (
    contentType.includes('application/problem+json') ||
    contentType.includes('application/json')
  ) {
    try {
      return (await response.json()) as ProblemDetails;
    } catch {
      // Body inválido — caemos al fallback.
    }
  }
  return {
    type: 'about:blank',
    title: response.statusText || `HTTP ${response.status}`,
    status: response.status,
  };
}

/**
 * Parsea el header <c>Retry-After</c>: solo soportamos el formato
 * <c>delta-seconds</c> (RFC 7231 §7.1.3). Si viene como HTTP-date o no
 * es parseable, devuelve <c>null</c> y el cliente cae al delay default.
 */
function parseRetryAfter(header: string | null): number | null {
  if (header == null) return null;
  const trimmed = header.trim();
  if (!/^\d+$/.test(trimmed)) return null;
  const segundos = Number(trimmed);
  return Number.isFinite(segundos) && segundos >= 0 ? segundos : null;
}

/**
 * Heurística para detectar bodies que NO debemos serializar como JSON:
 * strings, ArrayBuffers, Blobs, FormData, URLSearchParams, ReadableStream.
 */
function esBodyNativo(body: unknown): body is BodyInit {
  return (
    typeof body === 'string' ||
    body instanceof ArrayBuffer ||
    body instanceof Blob ||
    body instanceof FormData ||
    body instanceof URLSearchParams ||
    (typeof ReadableStream !== 'undefined' && body instanceof ReadableStream)
  );
}
