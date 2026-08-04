/** Valida el formato UUID del complemento (folio fiscal del CFDI tipo P). */
const UUID_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function esUuidValido(valor: string): boolean {
  return UUID_RE.test(valor.trim());
}
