import type { Depreciacion } from './libro';
export type AgrupacionDepreciacion = 'clase' | 'sucursal' | 'centroCosto';
export function agruparDepreciacion(filas: Depreciacion[], por: AgrupacionDepreciacion) {
  const grupos = new Map<string, { grupo: string; importe: number; activos: number }>();
  for (const f of filas) {
    const id = por === 'clase' ? f.claseId : por === 'sucursal' ? f.sucursalId : f.centroCostoId;
    const grupo = grupos.get(id) ?? { grupo: f[por], importe: 0, activos: 0 };
    grupo.importe = Math.round((grupo.importe + f.importe) * 100) / 100;
    grupo.activos++;
    grupos.set(id, grupo);
  }
  return [...grupos.values()];
}
