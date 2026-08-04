/** Helpers de formato del módulo Tesorería (TES-FE-PR2). */

export function formatoMonto(monto: number, moneda: string): string {
  return `${monto.toLocaleString('es-MX', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })} ${moneda}`;
}

/** Fecha de negocio DateOnly (`yyyy-MM-dd`) → `dd/mm/aaaa` sin timezone shift. */
export function formatoFecha(fecha: string): string {
  const [y, m, d] = fecha.split('-');
  if (!y || !m || !d) return fecha;
  return `${d}/${m}/${y}`;
}
