import type { CentroOpcion } from '../../api/dimensiones-types';

/** H1 (86e3z5w25): área (Dim2) y equipo (Dim3) capturados en un movimiento cuya póliza carga a una cuenta con la regla C-03. */
export interface AreaEquipoValor {
  area: CentroOpcion | null;
  equipo: CentroOpcion | null;
}

/** Falta un dato que la cuenta exige; M-450: el movimiento no se guarda hasta capturarlo. */
export function faltaAreaEquipo(v: AreaEquipoValor, exigeArea: boolean, exigeEquipo: boolean): boolean {
  return (exigeArea && !v.area && !v.equipo) || (exigeEquipo && !v.equipo);
}

/** Id del centro que viaja en el movimiento: el equipo si se eligió; si no, el área. */
export function areaEquipoIds(v: AreaEquipoValor): { areaId?: string; equipoId?: string } {
  return { areaId: v.area?.id ?? v.equipo?.dim2Id ?? undefined, equipoId: v.equipo?.id };
}
