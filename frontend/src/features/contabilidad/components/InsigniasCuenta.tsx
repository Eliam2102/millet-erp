import { Lock } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import type { CuentaControl, Naturaleza, TipoCuenta } from '../api/types';

interface Props {
  tipo: TipoCuenta | null;
  activa: boolean;
  pendienteValidacion: boolean;
  /** `undefined` = no se muestra (el árbol no la trae); `null` = pendiente. */
  naturaleza?: Naturaleza | null;
  control?: CuentaControl;
  usada?: boolean | null;
}

/**
 * Insignias de una cuenta con las variantes semánticas de `Badge`: warning = pendiente, neutral = tipo/inactiva/usada,
 * info = control. Naturaleza/tipo nulos muestran «Pendiente» explícito: nunca se pinta un valor supuesto.
 */
export function InsigniasCuenta({ tipo, activa, pendienteValidacion, naturaleza, control, usada }: Props) {
  return (
    <span className="inline-flex flex-wrap items-center gap-1">
      {tipo === null ? (
        <Badge variant="warning">Tipo: Pendiente</Badge>
      ) : (
        <Badge variant="neutral">{tipo === 'Titulo' ? 'Título' : 'Afectable'}</Badge>
      )}
      {naturaleza === null && <Badge variant="warning">Naturaleza: Pendiente</Badge>}
      {naturaleza && <Badge variant="outline">{naturaleza}</Badge>}
      {pendienteValidacion && <Badge variant="warning">Pendiente de validación</Badge>}
      {!activa && <Badge variant="neutral">Inactiva</Badge>}
      {control && control !== 'Ninguna' && <Badge variant="info">Control: {control}</Badge>}
      {usada && (
        <Badge variant="neutral" title="Tiene movimientos">
          <Lock className="mr-1 size-3" aria-hidden="true" />Usada
        </Badge>
      )}
    </span>
  );
}
