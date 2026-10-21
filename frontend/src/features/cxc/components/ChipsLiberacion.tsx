import { EstadoCxcBadge } from './EstadoCxcBadge';
import type { ResultadoLiberacion, EstadoAutorizacionCredito } from '../api/types';
export function ChipResultadoLiberacion({ resultado }: { resultado: ResultadoLiberacion }) {
  return <EstadoCxcBadge entidad="liberacion" estado={resultado} />;
}
export function ChipEstadoAutorizacion({ estado }: { estado: EstadoAutorizacionCredito }) {
  return <EstadoCxcBadge entidad="autorizacion" estado={estado} />;
}
