/** F1-MP-01: presentación de la medida de hoja (vidrio crudo). La existencia viaja en m²; las hojas se derivan con el área. */

export function etiquetaMedidaMm(anchoMm?: number | null, altoMm?: number | null): string | null {
  return anchoMm != null && altoMm != null ? `${anchoMm} × ${altoMm} mm` : null;
}

/** m² por hoja, redondeado a 4 decimales como el servidor; null si falta ancho o alto. */
export function areaHojaDe(anchoMm?: number | null, altoMm?: number | null): number | null {
  return anchoMm != null && altoMm != null ? Math.round((anchoMm * altoMm) / 100) / 10000 : null;
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

type FilaRecepcion = {
  id: string;
  cantidad: number;
  productoAwVarianteId?: string | null;
  anchoMm?: number | null;
  altoMm?: number | null;
};

/** Medida de la partida de recepción (vidrio por hoja): m² por hoja, hojas recibidas y texto «2440 × 3660 mm · 20 hojas recibidas». */
export function medidaDe(l: FilaRecepcion | undefined) {
  const area = l?.productoAwVarianteId ? areaHojaDe(l.anchoMm, l.altoMm) : null;
  const hojas = l && area ? hojasDe(l.cantidad, area) : null;
  if (!l || !area || hojas == null) return null;
  const medida = etiquetaMedidaMm(l.anchoMm, l.altoMm);
  return {
    area,
    hojas,
    texto: `${medida} · ${hojas} ${hojas === 1 ? 'hoja recibida' : 'hojas recibidas'}`,
  };
}

/** Cantidad a enviar (m²): con medida, las hojas capturadas × m² por hoja. */
export function cantidadM2(
  l: { cantidad: number; lineaRecepcionOrigenId?: string | null },
  origen: FilaRecepcion[] | undefined,
): number {
  const medida = medidaDe(origen?.find((o) => o.id === l.lineaRecepcionOrigenId));
  return medida ? Math.round(l.cantidad * medida.area * 10000) / 10000 : l.cantidad;
}
