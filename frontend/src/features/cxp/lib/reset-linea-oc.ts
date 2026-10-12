import type { UseFormGetValues, UseFormSetValue } from 'react-hook-form';
import type { CapturarFacturaValues } from '@/features/cxp/schemas/factura';

/**
 * Resetea <c>lineaOcId</c> de TODAS las líneas de factura a <c>null</c>.
 *
 * Se llama al CAMBIAR o LIMPIAR la OC de cabecera: los <c>lineaOcId</c>
 * previos apuntan a líneas de la OC vieja (quedan stale) y la guarda backend
 * <c>LINEA_OC_NO_PERTENECE</c> los rechazaría al enviar. No toca otros campos
 * de la línea.
 *
 * <para>Puro/aislado: recibe <c>getValues</c>/<c>setValue</c> de RHF → se
 * testea sin un form real ni popover (imperativo, no <c>useEffect</c>, para
 * no chocar con <c>react-hooks/set-state-in-effect</c>).</para>
 */
export function resetLineaOcIds(
  getValues: UseFormGetValues<CapturarFacturaValues>,
  setValue: UseFormSetValue<CapturarFacturaValues>,
): void {
  const lineas = getValues('lineas') ?? [];
  lineas.forEach((_, i) => {
    // Sin shouldValidate: validar aquí mostraba el error antes del submit, y el
    // selector de línea no revalida hasta que el form se envía.
    setValue(`lineas.${i}.lineaOcId`, null);
  });
}
