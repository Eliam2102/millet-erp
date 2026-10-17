/**
 * Formateador de fecha-hora de las páginas de detalle CxP. Montos y fechas
 * de calendario usan <c>MoneyDisplay</c>/<c>DateTimeDisplay</c>.
 */

export function formatearFechaHora(iso: string): string {
  try {
    return new Date(iso).toLocaleString('es-MX', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
    });
  } catch {
    return iso;
  }
}
