import { Badge } from '@/components/ui/badge';
import type {
  EstadoAplicacionMovimiento,
  EstadoConciliacionMovimiento,
  EstadoDepositoConfirmacion,
  EstadoDevolucionCliente,
  SentidoMovimiento,
} from '../api/types';
import { estadoUi, type EntidadEstado } from '../lib/estados-ui';

export function EstadoTesoreriaBadge({
  entidad,
  estado,
}: {
  entidad: EntidadEstado;
  estado: string | number;
}) {
  const ui = estadoUi(entidad, estado);
  return <Badge variant={ui.tono}>{ui.texto}</Badge>;
}
export function SentidoBadge({ sentido }: { sentido: SentidoMovimiento }) {
  return <EstadoTesoreriaBadge entidad="sentido" estado={sentido} />;
}
export function EstadoAplicacionBadge({ estado }: { estado: EstadoAplicacionMovimiento }) {
  return <EstadoTesoreriaBadge entidad="aplicacion" estado={estado} />;
}
export function EstadoConciliacionBadge({ estado }: { estado: EstadoConciliacionMovimiento }) {
  return <EstadoTesoreriaBadge entidad="conciliacion" estado={estado} />;
}
export function EstadoDepositoBadge({ estado }: { estado: EstadoDepositoConfirmacion }) {
  return <EstadoTesoreriaBadge entidad="deposito" estado={estado} />;
}
export function ReppTimbradoBadge({ timbrado }: { timbrado: boolean }) {
  return <EstadoTesoreriaBadge entidad="repp" estado={timbrado ? 'timbrado' : 'pendiente'} />;
}
export function EstadoDevolucionBadge({ estado }: { estado: EstadoDevolucionCliente }) {
  return <EstadoTesoreriaBadge entidad="devolucion" estado={estado} />;
}
