import { EstadoCxcBadge } from './EstadoCxcBadge';
import type { EstadoPropuestaAplicacion } from '../api/types';
export function ChipEstadoPropuesta({ estado }: { estado: EstadoPropuestaAplicacion }) {
  return <EstadoCxcBadge entidad="propuesta" estado={estado} />;
}
