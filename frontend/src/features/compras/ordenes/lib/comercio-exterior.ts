import {
  TipoOperacionComercioExterior,
  TipoTransporte,
} from '@/features/compras/ordenes/api/types';

/**
 * Etiquetas en español de los enums de comercio exterior de la OC.
 * // PLATFORM-TODO(<F1-COM-06>): supuesto Vilo, pendiente lista oficial de Millet.
 */
export const ETIQUETA_TIPO_OPERACION: Record<TipoOperacionComercioExterior, string> = {
  [TipoOperacionComercioExterior.Importacion]: 'Importación',
  [TipoOperacionComercioExterior.Exportacion]: 'Exportación',
};

export const ETIQUETA_TIPO_TRANSPORTE: Record<TipoTransporte, string> = {
  [TipoTransporte.Maritimo]: 'Marítimo',
  [TipoTransporte.Aereo]: 'Aéreo',
  [TipoTransporte.Terrestre]: 'Terrestre',
};
