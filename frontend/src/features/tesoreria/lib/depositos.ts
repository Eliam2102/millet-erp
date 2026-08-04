import type { DepositoConfirmacionResponse } from '@/features/tesoreria/api/types';

/**
 * Etiqueta del origen de la confirmación de depósito (TES-FE-PR4b, §4.6):
 * propuesta de aplicación de CxC o expectativa de una sesión de caja de
 * Facturación (el check de origen del backend garantiza uno de los dos).
 */
export function origenDeposito(
  d: Pick<DepositoConfirmacionResponse, 'propuestaCxcId' | 'cajaSesionId'>,
): string {
  if (d.propuestaCxcId != null) return 'Propuesta CxC';
  if (d.cajaSesionId != null) return 'Caja';
  return '—';
}
