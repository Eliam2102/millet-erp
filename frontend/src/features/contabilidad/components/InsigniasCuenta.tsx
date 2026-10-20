import { Lock } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import type { ClaseCuenta, CuentaControl, Naturaleza, TipoCuenta } from '../api/types';

interface Props {
  tipo: TipoCuenta | null;
  activa: boolean;
  pendienteValidacion: boolean;
  /** `undefined` = no se muestra (el árbol no la trae); `null` = pendiente. */
  naturaleza?: Naturaleza | null;
  control?: CuentaControl;
  usada?: boolean | null;
  clase?: ClaseCuenta;
}

/**
 * Insignias de una cuenta con las variantes semánticas de `Badge`: warning = pendiente, neutral = tipo/inactiva/usada,
 * info = colectiva o rubro. El tipo lo calcula el sistema (P19): «Acumula» o «Afectable». Una naturaleza nula muestra
 * «Pendiente» explícito: nunca se pinta un valor supuesto. Un rubro (P24) no lleva tipo ni naturaleza: es agrupación de reporte.
 */
export function InsigniasCuenta({
  tipo,
  activa,
  pendienteValidacion,
  naturaleza,
  control,
  usada,
  clase,
}: Props) {
  const rubro = clase === 'Rubro';
  return (
    <span className="inline-flex flex-wrap items-center gap-1">
      {rubro ? (
        <Badge variant="info">Rubro de reporte</Badge>
      ) : tipo === null ? (
        <Badge variant="warning">Tipo: Pendiente</Badge>
      ) : (
        <Badge
          variant="neutral"
          title={tipo === 'Titulo' ? 'No recibe movimientos' : 'Recibe movimientos'}
        >
          {tipo === 'Titulo' ? 'Acumula' : 'Afectable'}
        </Badge>
      )}
      {!rubro && naturaleza === null && (
        <span className="text-xs text-warning-fg">Naturaleza: Pendiente</span>
      )}
      {naturaleza && <span className="text-xs text-ink-muted">{naturaleza}</span>}
      {pendienteValidacion && <Badge variant="warning">Pendiente de validación</Badge>}
      {!activa && <span className="text-xs text-ink-muted">Inactiva</span>}
      {control && control !== 'Ninguna' && (
        <span className="text-xs text-info-fg">Colectiva: {control}</span>
      )}
      {usada && (
        <span className="inline-flex items-center text-xs text-ink-muted" title="Tiene movimientos">
          <Lock className="mr-1 size-3" aria-hidden="true" />
          Usada
        </span>
      )}
    </span>
  );
}
