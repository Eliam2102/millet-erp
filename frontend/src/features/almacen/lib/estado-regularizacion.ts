import { REGULARIZACION_UI } from './estados-ui';
export function estadoRegularizacion(salida: {
  pendienteRegularizacion: boolean;
  vencido: boolean;
}) {
  if (!salida.pendienteRegularizacion) return REGULARIZACION_UI.regularizado;
  return salida.vencido ? REGULARIZACION_UI.vencido : REGULARIZACION_UI.pendiente;
}
