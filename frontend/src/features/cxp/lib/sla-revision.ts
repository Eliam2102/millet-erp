/** Umbrales de aviso en días hábiles; misma regla que el backend `PlazoRevision.Umbrales`. */
export function umbralesSla(slaDias: number): { aviso: number; vencido: number; escalamiento: number } {
  if (slaDias === 5) return { aviso: 3, vencido: 5, escalamiento: 10 };
  if (slaDias === 15) return { aviso: 8, vencido: 12, escalamiento: 20 };
  return { aviso: Math.ceil(slaDias * 0.6), vencido: slaDias, escalamiento: slaDias * 2 };
}

/** Tono del plazo: sin plazo (Indicación expresa) es neutral; ámbar desde el aviso, rojo desde el vencimiento. */
export function tonoPlazo(dias: number, slaDias: number | null): 'success' | 'warning' | 'danger' | 'neutral' {
  if (slaDias == null) return 'neutral';
  const { aviso, vencido } = umbralesSla(slaDias);
  if (dias >= vencido) return 'danger';
  if (dias >= aviso) return 'warning';
  return 'success';
}

/** Etiqueta del último aviso emitido en el episodio vigente (1 aviso, 2 vencido, 3 escalada). */
export function etiquetaAviso(nivel: 1 | 2 | 3 | null): { texto: string; tono: 'warning' | 'danger' } | null {
  if (nivel === 1) return { texto: 'Aviso enviado', tono: 'warning' };
  if (nivel === 2) return { texto: 'Plazo vencido', tono: 'danger' };
  if (nivel === 3) return { texto: 'Escalada a Dirección de Finanzas', tono: 'danger' };
  return null;
}
