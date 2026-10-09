import type { AutorizacionOc } from '../api/types';

export function agruparCiclosAutorizacion(firmas: readonly AutorizacionOc[]) {
  const ciclos = [...new Set(firmas.map((firma) => firma.ciclo))].sort((a, b) => b - a);
  return ciclos.map((ciclo) => ({
    ciclo,
    firmas: firmas.filter((firma) => firma.ciclo === ciclo)
      .sort((a, b) => a.fechaHora.localeCompare(b.fechaHora) || a.nivel - b.nivel),
  }));
}
