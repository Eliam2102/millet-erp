/**
 * <c>ApiError</c> y helpers para el cliente HTTP enriquecido. Modela el
 * shape <c>application/problem+json</c> (ADR-0010) que el backend devuelve
 * en cualquier 4xx/5xx, y expone helpers para distinguir las clases de
 * fallo que la UI trata distinto (validación, conflicto, idempotencia,
 * etc.).
 *
 * <para>Ver doc 05 §8.1 para el mapeo status → tratamiento UI.</para>
 */

/**
 * Forma del payload <c>application/problem+json</c> emitido por el
 * backend (mirror del shape descrito en ADR-0010).
 */
export interface ProblemDetails {
  type: string;
  title: string;
  status: number;
  detail?: string;
  instance?: string;
  traceId?: string;
  /**
   * <c>code</c> aplicativo del error (ej. <c>CONCURRENCY_CONFLICT</c>,
   * <c>IDEMPOTENCY_IN_PROGRESS</c>, <c>TRANSMITIR_SIN_LINEAS</c>). Es
   * la clave que la UI consulta para decidir el tratamiento específico.
   */
  code?: string;
  /**
   * Errores por campo, con la convención del backend:
   * <c>{ campo, codigo, mensaje }</c>. <see cref="applyServerErrors"/>
   * los mapea al estado de un <c>react-hook-form</c>.
   */
  errores?: Array<{ campo: string; codigo: string; mensaje: string }>;
  /**
   * Errores por campo del <c>ValidationProblemDetails</c> DEFAULT de
   * ASP.NET (<c>{ campo: string[] }</c>): los emite el model-binding /
   * la auto-validación de <c>[ApiController]</c> ANTES de FluentValidation
   * (enum fuera de rango, decimal mal formado, JSON inválido). No sigue la
   * convención <c>errores[]</c>; <see cref="applyServerErrors"/> lo mapea
   * como fallback para que el campo no se pierda en un toast genérico.
   */
  errors?: Record<string, string[]>;
}

/**
 * Error tipado que el cliente HTTP lanza ante cualquier respuesta con
 * <c>!response.ok</c>. La UI lo captura y decide qué hacer en función
 * de <c>status</c> + <c>code</c>.
 *
 * <para>Es <c>Error</c> de toda la vida (instancia) — los hooks de
 * TanStack Query lo reciben en el callback <c>onError</c> con tipo
 * <c>unknown</c>; usar <see cref="esApiError"/> como type guard.</para>
 */
export class ApiError extends Error {
  /** HTTP status code (200–599). */
  readonly status: number;
  /** Payload <c>ProblemDetails</c> del backend. */
  readonly problem: ProblemDetails;
  /**
   * Header <c>Retry-After</c> en segundos (parseado), si el backend lo
   * incluyó. Solo relevante para 409/429.
   */
  readonly retryAfterSeconds: number | null;

  constructor(
    problem: ProblemDetails,
    status: number,
    retryAfterSeconds: number | null = null,
  ) {
    super(problem.title || `HTTP ${status}`);
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
    this.retryAfterSeconds = retryAfterSeconds;

    // Restaura el prototype tras llamar al super-constructor (necesario en
    // builds que transpilen a ES5; en ES2023 nativo no haría falta).
    Object.setPrototypeOf(this, ApiError.prototype);
  }

  /** <c>code</c> aplicativo del problem details (atajo). */
  get code(): string | undefined {
    return this.problem.code;
  }

  /** <c>traceId</c> del problem details (atajo, para mostrar a soporte). */
  get traceId(): string | undefined {
    return this.problem.traceId;
  }
}

/**
 * Type guard: <c>true</c> si el valor es una instancia de
 * <see cref="ApiError"/>. Útil para <c>catch (e: unknown)</c>.
 */
export function esApiError(error: unknown): error is ApiError {
  return error instanceof ApiError;
}

/**
 * <c>true</c> si es un 409 con <c>code = CONCURRENCY_CONFLICT</c>
 * (ADR-0012 Capa 1). La UI lo transforma en
 * <c>&lt;ConflictResolutionDialog/&gt;</c> (UF0-PR2).
 */
export function esConflictoConcurrencia(error: unknown): error is ApiError {
  return (
    esApiError(error) &&
    error.status === 409 &&
    error.code === 'CONCURRENCY_CONFLICT'
  );
}

/**
 * <c>true</c> si es un <c>428 Precondition Required</c>: la mutación
 * llegó SIN header <c>If-Match</c> a un endpoint que lo exige
 * (ADR-0012). Tratamiento UI: mismo camino que el 409 de concurrencia —
 * recargar con aviso (el cliente perdió/nunca tuvo el ETag).
 *
 * OJO: se matchea por STATUS, no por <c>code</c> — el backend NO manda
 * <c>code</c> en el 428 (verificado contra la API viva, CECO-FE-PR2):
 * el problem details trae solo type/title/status/detail. Un matcheo por
 * código aquí jamás dispararía.
 */
export function esPrecondicionRequerida(error: unknown): error is ApiError {
  return esApiError(error) && error.status === 428;
}

/**
 * <c>true</c> si es un 409 cuyo <c>code</c> empieza con
 * <c>IDEMPOTENCY_</c>. El cliente HTTP reintenta automáticamente
 * <c>IDEMPOTENCY_IN_PROGRESS</c> respetando <c>Retry-After</c>; otros
 * códigos <c>IDEMPOTENCY_*</c> son fallos del cliente y se propagan.
 *
 * <para>Ver doc 05 §8.3.</para>
 */
export function esIdempotencyEnCurso(error: unknown): error is ApiError {
  return (
    esApiError(error) &&
    error.status === 409 &&
    error.code === 'IDEMPOTENCY_IN_PROGRESS'
  );
}

/** <c>true</c> si el error es un 4xx (cliente). */
export function esErrorDeCliente(error: unknown): error is ApiError {
  return esApiError(error) && error.status >= 400 && error.status < 500;
}

/** <c>true</c> si el error es un 5xx (servidor). */
export function esErrorDeServidor(error: unknown): error is ApiError {
  return esApiError(error) && error.status >= 500;
}

/**
 * <c>true</c> si el error es 422 (regla de negocio o body inválido) con
 * el <c>code</c> indicado. Útil para tratar casos puntuales como
 * <c>TRANSMITIR_SIN_LINEAS</c>.
 *
 * @example
 * ```ts
 * if (esCodigoEspecifico(error, 'TRANSMITIR_SIN_LINEAS')) { ... }
 * ```
 */
export function esCodigoEspecifico(
  error: unknown,
  code: string,
): error is ApiError {
  return esApiError(error) && error.code === code;
}
