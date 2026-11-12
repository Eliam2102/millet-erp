/** Mensaje de un error de API para pintarlo junto al campo o en una alerta. */
export function mensajeApi(error: unknown, respaldo: string): string {
  const p = (error as { problem?: { detail?: string; title?: string } } | null)?.problem;
  return p?.detail || p?.title || respaldo;
}
