/**
 * Formateadores compartidos de las páginas de detalle CxP: montos con
 * moneda es-MX y fechas cortas/fecha-hora. Mismo formato que los
 * <c>formatearMonto</c>/<c>formatearFecha</c> locales de las bandejas.
 */

export function formatearMonto(v: number, moneda: string): string {
  try {
    return new Intl.NumberFormat('es-MX', {
      style: 'currency',
      currency: moneda,
      minimumFractionDigits: 2,
    }).format(v);
  } catch {
    return `${v.toFixed(2)} ${moneda}`;
  }
}

export function formatearFecha(iso: string): string {
  try {
    // Una fecha de calendario llega sin hora o como medianoche UTC; se formatea en UTC
    // para que en México (UTC-6) no se muestre el día anterior.
    const soloFecha = /^\d{4}-\d{2}-\d{2}(T00:00:00(\.0+)?(Z|\+00:00))?$/.test(iso);
    return new Date(iso).toLocaleDateString('es-MX', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      ...(soloFecha ? { timeZone: 'UTC' } : {}),
    });
  } catch {
    return iso;
  }
}

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
