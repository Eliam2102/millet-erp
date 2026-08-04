/**
 * Resumen del alcance de una caja para listas y bandeja (CAJAS-PR5).
 * Comodines del alcance (12-cajas.md §4.1): sin sucursales = todas; sin
 * canales = todos.
 */
export function resumenAlcance(c: {
  sucursales: number;
  canales: number;
  usuarios: number;
}): string {
  const sucursales = c.sucursales === 0 ? 'todas las sucursales' : `${c.sucursales} suc.`;
  const canales = c.canales === 0 ? 'todos los canales' : `${c.canales} canal(es)`;
  return `${sucursales} · ${canales} · ${c.usuarios} cajero(s)`;
}
