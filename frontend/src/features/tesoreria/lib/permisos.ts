/**
 * Permisos de Tesorería agregados con M-199/M-201/M-202. Espejo de
 * `PermisosCanonicos.cs`; pasan a `permission-codes.ts` cuando se toque ese archivo.
 */
export const PermisosTesoreriaNuevos = {
  CuentasAutorizar: 'tesoreria.cuentas.autorizar',
  ConciliacionRevisar: 'tesoreria.conciliacion.revisar',
  ConciliacionReabrir: 'tesoreria.conciliacion.reabrir',
  ConciliacionAutorizarTransito: 'tesoreria.conciliacion.autorizar-transito',
} as const;

/** Mensaje de un error de API para pintarlo junto al campo o en una alerta. */
export function mensajeApi(error: unknown, respaldo: string): string {
  const p = (error as { problem?: { detail?: string; title?: string } } | null)?.problem;
  return p?.detail || p?.title || respaldo;
}
