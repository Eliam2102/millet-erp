/**
 * Formatea una cantidad con separador de miles (es-MX) y los decimales de la
 * unidad de medida (`useDecimalesUnidad`). `null`/`undefined`/`NaN` → «—».
 * Sin `decimales` conocidos, usa hasta 4 sin ceros de relleno.
 */
export function formatCantidad(
  valor: number | null | undefined,
  decimales?: number | null,
): string {
  if (valor == null || !Number.isFinite(valor)) return '—';
  const fijo = decimales != null;
  return new Intl.NumberFormat('es-MX', {
    minimumFractionDigits: fijo ? decimales : 0,
    maximumFractionDigits: fijo ? decimales : 4,
  }).format(valor);
}
