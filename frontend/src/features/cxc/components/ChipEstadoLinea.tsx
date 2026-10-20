import { EstadoCxcBadge } from './EstadoCxcBadge';
import type { EstadoLineaCredito } from '../api/types';
export function ChipEstadoLinea({ estado }: { estado: EstadoLineaCredito }) {
  return <EstadoCxcBadge entidad="linea" estado={estado} />;
}
