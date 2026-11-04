import type { PasivoPendienteResponse } from '../api/types';

type Seleccionable = Pick<PasivoPendienteResponse, 'proveedorId' | 'moneda' | 'proveedorBloqueado'>;

/**
 * Si el pasivo se puede marcar en la bandeja de pagos. Proveedor bloqueado nunca (igual que en «Nueva corrida»);
 * con algo ya marcado, solo el mismo proveedor y moneda: una transferencia = un beneficiario (RN-3).
 */
export function esPasivoSeleccionable(p: Seleccionable, ancla: Seleccionable | null): boolean {
  if (p.proveedorBloqueado === true) return false;
  if (ancla == null) return true;
  return p.proveedorId === ancla.proveedorId && p.moneda === ancla.moneda;
}
