import type { ContenedoresDetalle } from '../api/types';

export function totalesPacking(d: ContenedoresDetalle, id: string) {
  const c = d.contenedores.find((c) => c.id === id);
  const partidas = d.partidas.filter((p) => p.contenedorId === id && p.estado !== 3);
  const pesoKg = partidas.reduce((s, p) => s + (p.pesoKg ?? 0), 0);
  const bultos = partidas.reduce((s, p) => s + (p.bultos ?? 0), 0);
  return {
    pesoKg,
    bultos,
    cuadra:
      partidas.every((p) => p.pesoKg != null && p.bultos != null) &&
      Math.abs((c?.pesoKg ?? -1) - pesoKg) < 0.000001 &&
      c?.bultos === bultos,
  };
}
