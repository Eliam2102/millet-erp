/**
 * Interpreta el texto del campo de tolerancia de recepción (%) del artículo.
 * Vacío = null (0 %). Acepta hasta 3 enteros y 2 decimales con punto, sin
 * exceder 100. El servidor valida lo mismo (ARTICULO_TOLERANCIA_INVALIDA).
 */
export function interpretarToleranciaPorcentaje(valor: string):
  { valido: true; porcentaje: number | null } | { valido: false } {
  const texto = valor.trim();
  if (texto === '') return { valido: true, porcentaje: null };
  if (!/^\d{1,3}(\.\d{1,2})?$/.test(texto)) return { valido: false };
  const porcentaje = Number(texto);
  return porcentaje <= 100
    ? { valido: true, porcentaje }
    : { valido: false };
}
