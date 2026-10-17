import type { ErroresClasificacionFiscal } from '@/modules/datos-maestros/components/ClasificacionFiscalCampos';

/** Traduce el código 422 del backend al campo que corresponde. */
export function erroresDeClasificacion(code: string | undefined, mensaje: string): ErroresClasificacionFiscal {
  if (code === 'PROVEEDOR_REGIMEN_REQUERIDO' || code === 'PROVEEDOR_REGIMEN_NO_EXISTE' || code === 'PROVEEDOR_REGIMEN_INVALIDO') {
    return { regimen: mensaje };
  }
  return {};
}
