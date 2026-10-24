/** F1-MP-01: presentación de la medida de hoja (vidrio crudo). La existencia viaja en m²; las hojas se derivan con el área. */

export function etiquetaMedidaMm(anchoMm?: number | null, altoMm?: number | null): string | null {
  return anchoMm != null && altoMm != null ? `${anchoMm} × ${altoMm} mm` : null;
}

/** Hojas equivalentes a una cantidad en m²; null si no hay área (medida sin ancho y alto). */
export function hojasDe(cantidadM2: number, areaHojaM2?: number | null): number | null {
  return areaHojaM2 ? Math.round((cantidadM2 / areaHojaM2) * 100) / 100 : null;
}

/** «2440 × 3660 mm · 20 hojas» o null si la fila no es por medida. */
export function textoMedida(
  fila: { productoAwVarianteId?: string | null; anchoMm?: number | null; altoMm?: number | null; areaHojaM2?: number | null },
  cantidadM2: number,
): string | null {
  if (!fila.productoAwVarianteId) return null;
  const medida = etiquetaMedidaMm(fila.anchoMm, fila.altoMm) ?? 'Medida sin dimensiones';
  const hojas = hojasDe(cantidadM2, fila.areaHojaM2);
  return hojas == null ? medida : `${medida} · ${hojas} ${hojas === 1 ? 'hoja' : 'hojas'}`;
}
