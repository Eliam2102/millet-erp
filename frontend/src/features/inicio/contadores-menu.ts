import type { IndicadorInicio } from './api/useResumenInicio';

/** Suma los conteos disponibles de Inicio; un error o carga evita un total parcial. */
export function contadoresMenu(resumen: {
  filas: readonly IndicadorInicio[];
  errores: readonly IndicadorInicio[];
  cargando: boolean;
}): Record<string, number> {
  if (resumen.cargando) return {};
  const alias: Record<string, string> = {
    CxP: 'Cuentas por pagar',
    CxC: 'Cobranza',
    Proveedores: 'Compras',
  };
  const result: Record<string, number> = {};
  const errores = new Set(
    resumen.errores.map((item) => alias[item.fila.modulo] ?? item.fila.modulo),
  );
  for (const item of resumen.filas) {
    const module = alias[item.fila.modulo] ?? item.fila.modulo;
    if (!errores.has(module)) result[module] = (result[module] ?? 0) + item.prioridad.total;
  }
  return result;
}
