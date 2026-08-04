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
    return new Date(iso).toLocaleDateString('es-MX', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
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
