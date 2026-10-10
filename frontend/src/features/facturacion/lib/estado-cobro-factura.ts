import type { ComprobanteDetalleResponse } from '../api/types';

/** El estado del cobro es independiente del estado fiscal del comprobante. */
export function estadoCobroFactura(f: Pick<ComprobanteDetalleResponse,
  'metodoPago' | 'cobroMostrador' | 'pagadoPorRep' | 'totalPorCobrar'>) {
  if (f.metodoPago === 'PPD') {
    if (f.totalPorCobrar <= 0) return f.pagadoPorRep > 0 ? 'Cobrada' : 'Sin monto por cobrar';
    return f.pagadoPorRep > 0 ? 'Parcial (PPD)' : 'Por cobrar';
  }
  if (f.metodoPago === 'PUE') {
    if (f.cobroMostrador != null) return 'Cobrada';
    return f.totalPorCobrar > 0 ? 'Por cobrar' : 'Sin monto por cobrar';
  }
  return 'Por confirmar';
}
