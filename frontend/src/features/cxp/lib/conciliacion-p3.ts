export interface RetencionDetalle { impuesto: string; tasa: number | null; importe: number }

export function desgloseRetenciones(detalle: readonly RetencionDetalle[] | null | undefined) {
  return {
    isr: detalle?.filter(r => r.impuesto === '001').reduce((s, r) => s + r.importe, 0) ?? 0,
    iva: detalle?.filter(r => r.impuesto === '002').reduce((s, r) => s + r.importe, 0) ?? 0,
  };
}

export function lineasParaCompensar(lineas: readonly { lineaOcId: string | null; descripcion: string }[]) {
  return lineas.flatMap((l, i) => l.lineaOcId && lineas.findIndex(x => x.lineaOcId === l.lineaOcId) === i
    ? [{ id: l.lineaOcId, etiqueta: `Línea ${i + 1} · ${l.descripcion}` }] : []);
}
