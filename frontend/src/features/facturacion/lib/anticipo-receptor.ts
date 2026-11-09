/** RFC genérico del receptor extranjero (SAT). */
const RFC_EXTRANJERO = 'XEXX010101000';

/**
 * País del receptor del anticipo. Un cliente extranjero (RFC XEXX010101000) lleva su país de residencia del maestro: el SAT y el
 * backend rechazan un receptor extranjero con país MEX (RECEPTOR_FISCAL_INVALIDO), y con MEX el NumRegIdTrib no viaja. Cualquier
 * otro cliente es nacional (MEX). Sin país en el maestro se queda en MEX y el backend explica qué falta en la ficha.
 */
export function paisReceptorAnticipo(rfc: string | null | undefined, paisResidencia: string | null | undefined): string {
  const pais = (paisResidencia ?? '').trim().toUpperCase();
  return (rfc ?? '').trim().toUpperCase() === RFC_EXTRANJERO && pais !== '' ? pais : 'MEX';
}
