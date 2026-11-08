/** TES-09: valor a mandar al backend; undefined = MXN, sin cambios o vacío (el backend usa el del catálogo a la fecha valor). */
export function tipoCambioPagoParaEnviar(moneda: string | null, valor: string | null): number | undefined {
  if (moneda == null || moneda === 'MXN' || valor == null || valor.trim() === '') return undefined;
  return Number(valor);
}

/** TES-09: null si es válido o no aplica; si no, el motivo para deshabilitar el envío (con la etiqueta del campo). */
export function errorTipoCambioPago(
  moneda: string | null,
  valor: string | null,
  etiqueta = 'TC del pago',
): string | null {
  if (moneda == null || moneda === 'MXN' || valor == null || valor.trim() === '') return null;
  const n = Number(valor);
  if (!Number.isFinite(n) || n <= 0) return `El ${etiqueta} debe ser mayor a cero`;
  if (!/^\d+(\.\d{1,6})?$/.test(valor.trim())) return `El ${etiqueta} admite hasta 6 decimales`;
  return null;
}
