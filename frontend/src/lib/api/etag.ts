/**
 * Helpers para <c>ETag</c> y <c>If-Match</c> (ADR-0012 Capa 1).
 *
 * <para>El backend emite <c>ETag</c> en <c>GET /{id}</c>. El frontend lo
 * captura, lo guarda como <c>meta</c> de la query (vía <c>useQuery</c>) y
 * en cada mutation manda <c>If-Match: "&lt;etag&gt;"</c>. Si el backend
 * lo respeta, detecta conflictos antes de tocar la BD y devuelve 409
 * <c>CONCURRENCY_CONFLICT</c>; si lo ignora, no pasa nada (defensa en
 * profundidad). Ver doc 05 §7.5.</para>
 */

/**
 * Extrae el ETag de una respuesta HTTP, quitando comillas. Devuelve
 * <c>undefined</c> si la respuesta no incluye el header (no todos los
 * endpoints lo emiten — solo los <c>GET</c> de detalle).
 *
 * @example
 * ```ts
 * const response = await apiFetch('/api/v1/compras/requisiciones/123');
 * const etag = extractEtag(response); // → "12" (sin comillas)
 * ```
 */
export function extractEtag(response: Response): string | undefined {
  const header = response.headers.get('ETag');
  if (header == null) return undefined;
  // Algunos servidores emiten W/"abc" (weak ETag) y otros "abc" — quitamos
  // tanto comillas como prefijo W/, ya que para If-Match solo importa el
  // valor opaco.
  return header.replace(/^W\//, '').replace(/"/g, '');
}

/**
 * Construye un objeto plano <c>{ "If-Match": "..." }</c> que se hace
 * spread sobre los headers de una request. Si <c>etag</c> es
 * <c>undefined</c> o vacío, devuelve <c>{}</c> (sin header).
 *
 * @example
 * ```ts
 * const headers = { 'Content-Type': 'application/json', ...ifMatch(etag) };
 * ```
 */
export function ifMatch(
  etag: string | undefined | null,
): Record<string, string> {
  if (!etag) return {};
  // El header espera el valor entre comillas (RFC 7232 §3.1).
  return { 'If-Match': `"${etag}"` };
}
