import { AlertTriangle } from 'lucide-react';
import { Label } from '@/components/ui/label';
import type { CentroOpcion } from '../../api/dimensiones-types';
import { ETIQUETA_DIMENSION } from '../../lib/dimensiones';
import { CentroSelector } from './CentroSelector';

export interface AreaEquipoValor {
  area: CentroOpcion | null;
  equipo: CentroOpcion | null;
}

interface Props {
  /** Prefijo de los ids de los controles. */
  id: string;
  /** Sucursal del movimiento; sin ella no se ofrecen centros. */
  sucursalId: string | null | undefined;
  /** Selector de centros del módulo que captura (mismo contrato que el de Contabilidad). */
  endpoint: string;
  exigeArea: boolean;
  exigeEquipo: boolean;
  /** Cuenta que cargará la póliza, para el aviso. */
  cuentaCodigo?: string | null;
  value: AreaEquipoValor;
  onChange: (v: AreaEquipoValor) => void;
}

/** Falta un dato que la cuenta exige (la captura procede; la póliza queda en Rechazos). */
export function faltaAreaEquipo(v: AreaEquipoValor, exigeArea: boolean, exigeEquipo: boolean): boolean {
  return (exigeArea && !v.area && !v.equipo) || (exigeEquipo && !v.equipo);
}

/** Id del centro que viaja en el movimiento: el equipo si se eligió; si no, el área. */
export function areaEquipoIds(v: AreaEquipoValor): { areaId?: string; equipoId?: string } {
  return { areaId: v.area?.id ?? v.equipo?.dim2Id ?? undefined, equipoId: v.equipo?.id };
}

/**
 * H1 pasos 3 y 4 (86e3z5w25): área (Dim2) y equipo (Dim3) de un movimiento cuya póliza automática carga a una cuenta con la
 * regla de dimensiones C-03 (500 exige área; 600, área y equipo). La ubicación sale de la sucursal. Solo se ofrecen centros activos
 * de la sucursal; el equipo se filtra por el área elegida. Sin el dato exigido la captura procede y la póliza queda en Rechazos.
 */
export function AreaEquipoCaptura({
  id,
  sucursalId,
  endpoint,
  exigeArea,
  exigeEquipo,
  cuentaCodigo,
  value,
  onChange,
}: Props) {
  const falta = faltaAreaEquipo(value, exigeArea, exigeEquipo);
  const avisoId = `${id}-aviso`;
  const exigidos = [exigeArea && 'el área', exigeEquipo && 'el equipo'].filter(Boolean).join(' y ');

  return (
    <fieldset className="space-y-3">
      <legend className="text-sm font-medium text-ink">Dimensiones contables</legend>
      <div className="grid gap-3 sm:grid-cols-2">
        <div className="space-y-1.5">
          <Label htmlFor={`${id}-area`}>
            {ETIQUETA_DIMENSION.Dim2}
            {(exigeArea || exigeEquipo) && <span className="text-danger"> *</span>}
          </Label>
          <CentroSelector
            id={`${id}-area`}
            nivel="Dim2"
            sucursalId={sucursalId ?? ''}
            endpoint={endpoint}
            value={value.area}
            invalido={falta && (exigeArea || exigeEquipo) && !value.area && !value.equipo}
            describedBy={falta ? avisoId : undefined}
            onChange={(area) =>
              onChange({
                area,
                equipo: value.equipo && area && value.equipo.dim2Id !== area.id ? null : value.equipo,
              })
            }
          />
        </div>
        <div className="space-y-1.5">
          <Label htmlFor={`${id}-equipo`}>
            {ETIQUETA_DIMENSION.Dim3}
            {exigeEquipo && <span className="text-danger"> *</span>}
          </Label>
          <CentroSelector
            id={`${id}-equipo`}
            nivel="Dim3"
            sucursalId={sucursalId ?? ''}
            endpoint={endpoint}
            dim2Id={value.area?.id}
            value={value.equipo}
            invalido={falta && exigeEquipo && !value.equipo}
            describedBy={falta ? avisoId : undefined}
            onChange={(equipo) => onChange({ area: value.area, equipo })}
          />
        </div>
      </div>
      {!sucursalId ? (
        <p className="text-xs text-ink-muted">
          No se conoce la sucursal del movimiento: no se pueden elegir área ni equipo.
        </p>
      ) : falta ? (
        <p
          id={avisoId}
          role="status"
          className="flex items-start gap-2 rounded-md border border-warning bg-warning-note-bg px-3 py-2 text-xs text-warning-note-fg"
        >
          <AlertTriangle className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
          <span>
            La cuenta {cuentaCodigo ?? 'de la póliza'} exige {exigidos} (regla de dimensiones C-03). Sin{' '}
            {exigeEquipo && !exigeArea ? 'él' : 'ese dato'}, el movimiento se guarda y su póliza queda en Rechazos
            hasta capturarlo.
          </span>
        </p>
      ) : null}
    </fieldset>
  );
}
