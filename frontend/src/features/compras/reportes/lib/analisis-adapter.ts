import { backendToShellShape, type ReporteShellShape } from '@/features/cxp/lib/reportes-adapter';
import type { AnalisisResponse } from '@/features/compras/reportes/api/useAnalisisProveedores';

const PORCENTAJES = new Set(['porcentajeATiempo', 'porcentajeCompleto']);

/** Porcentaje 0–100 o «n/d» cuando no hay base de cálculo (nunca NaN). */
export function formatoPorcentaje(v: unknown): string {
  return typeof v === 'number' && Number.isFinite(v) ? `${v.toFixed(1)} %` : 'n/d';
}

export type FilaAnalisis = Record<string, unknown> & { __total?: string };

export interface AnalisisShell {
  reporte: ReporteShellShape & { filas: FilaAnalisis[] };
  /** Fórmulas y saldo inicial: viajan en PDF/Excel junto a la tabla. */
  notasExport: Record<string, string>;
}

const num2 = new Intl.NumberFormat('es-MX', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

/**
 * Convierte la respuesta al shape de <c>&lt;ReporteShell/&gt;</c>. Reutiliza
 * <c>backendToShellShape</c> de CxP y encima: (1) nunca formatea como MXN
 * (cada fila trae su moneda), (2) agrega una fila «Total {moneda}» por cada
 * moneda de <c>totalesPorMoneda</c> sin sumar entre monedas, (3) porcentajes
 * null → «n/d».
 */
export function analisisToShell(data: AnalisisResponse): AnalisisShell {
  const base = backendToShellShape({ ...data.reporte, totales: null });
  const columnas = base.columnas.map((c) => ({
    ...c,
    tipo: c.tipo === 'moneda' ? ('numero' as const) : c.tipo,
  }));
  const claves = new Set(columnas.map((c) => c.clave));
  const colEtiqueta = columnas[0]?.clave;

  const conPorcentajes = (fila: Record<string, unknown>): FilaAnalisis => {
    const out: FilaAnalisis = { ...fila };
    for (const k of PORCENTAJES) if (claves.has(k)) out[k] = formatoPorcentaje(out[k]);
    return out;
  };

  const filasTotales = data.totalesPorMoneda.map((t): FilaAnalisis => {
    const moneda = String(t.moneda ?? '');
    return {
      ...conPorcentajes(t),
      ...(colEtiqueta ? { [colEtiqueta]: `Total ${moneda}` } : {}),
      __total: moneda,
    };
  });

  const notasExport: Record<string, string> = {};
  data.formulas?.forEach((f, i) => {
    notasExport[`Fórmula ${i + 1}`] = f;
  });
  data.saldoInicial?.forEach((s) => {
    notasExport[`Saldo inicial ${s.moneda}`] =
      `${s.ordenes} órdenes · pendiente físico ${num2.format(s.pendienteFisico)}`;
  });

  return {
    reporte: { ...base, columnas, filas: [...base.filas.map(conPorcentajes), ...filasTotales] },
    notasExport,
  };
}
