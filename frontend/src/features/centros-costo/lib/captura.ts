import type { CentroCostoCaptura, CentroCostoOpcion } from '../api/captura';
export function centroCostoInicial(contexto: CentroCostoCaptura): CentroCostoOpcion | null {
  return contexto.heredado ?? (contexto.puedeElegir ? contexto.unicaOpcion : null);
}
export function etiquetaCentroCosto(nodo: Pick<CentroCostoOpcion, 'clave' | 'nombre'>): string {
  return `${nodo.clave} — ${nodo.nombre}`;
}
export function etiquetaNivelCentroCosto(nivel: number): string {
  return nivel === 3 ? 'Máquina (opcional)' : nivel === 2 ? 'Área / departamento' : 'Planta';
}
