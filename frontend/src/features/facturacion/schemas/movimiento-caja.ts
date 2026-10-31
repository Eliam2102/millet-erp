/**
 * Validación del movimiento manual de caja (depósito / retiro). Espeja
 * RegistrarCajaMovimientoValidator: el motivo de un retiro lleva al menos
 * 15 caracteres sin contar los espacios de los extremos (Millet 19-oct).
 */
export const TIPO_MOVIMIENTO_DEPOSITO = 2;
export const TIPO_MOVIMIENTO_RETIRO = 3;
export const TIPO_MOVIMIENTO_DEVOLUCION_FALTANTE = 7;
export const MOTIVO_RETIRO_MINIMO = 15;

export function errorDescripcionMovimiento(tipo: number, descripcion: string): string | null {
  const largo = descripcion.trim().length;
  // La devolución de faltante se describe sola («Devolución del corte del dd/MM/aaaa»).
  if (tipo === TIPO_MOVIMIENTO_DEVOLUCION_FALTANTE) return null;
  if (largo === 0) return 'La descripción es obligatoria.';
  if (tipo === TIPO_MOVIMIENTO_RETIRO && largo < MOTIVO_RETIRO_MINIMO) {
    return `El motivo del retiro debe tener al menos ${MOTIVO_RETIRO_MINIMO} caracteres (llevas ${largo}).`;
  }
  return null;
}
