import { TipoIncidenciaProveedorOc } from '../api/types';

/** F1-COM-07: etiqueta visible del tipo de incidencia del proveedor. */
export const TIPO_INCIDENCIA_LABEL: Record<TipoIncidenciaProveedorOc, string> = {
  [TipoIncidenciaProveedorOc.Faltante]: 'Faltante',
  [TipoIncidenciaProveedorOc.Danado]: 'Dañado',
  [TipoIncidenciaProveedorOc.Atraso]: 'Atraso',
};
