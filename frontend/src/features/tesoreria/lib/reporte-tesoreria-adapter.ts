import {
  TipoColumnaReporteTesoreria,
  type ReporteTesoreriaBackend,
} from '@/features/tesoreria/api/types';

/**
 * Shape canónico que consume <c>&lt;ReporteShell&gt;</c> (ADR-0036) —
 * mismo contrato que los adapters de CxC / Almacén / Facturación.
 */
export interface ReporteShellData {
  titulo: string;
  generadoEn: string;
  filtrosAplicados: Record<string, string | null>;
  columnas: Array<{
    clave: string;
    etiqueta: string;
    tipo: 'texto' | 'numero' | 'moneda' | 'fecha';
    alineacion?: string | null;
    anchoPx?: number | null;
  }>;
  filas: Array<Record<string, unknown>>;
  totales?: Record<string, number> | null;
}

const TIPO_MAP: Record<
  TipoColumnaReporteTesoreria,
  ReporteShellData['columnas'][number]['tipo']
> = {
  [TipoColumnaReporteTesoreria.Texto]: 'texto',
  [TipoColumnaReporteTesoreria.Entero]: 'numero',
  [TipoColumnaReporteTesoreria.Numerico]: 'numero',
  [TipoColumnaReporteTesoreria.Moneda]: 'moneda',
  [TipoColumnaReporteTesoreria.Fecha]: 'fecha',
  [TipoColumnaReporteTesoreria.FechaHora]: 'fecha',
  [TipoColumnaReporteTesoreria.Booleano]: 'texto',
  [TipoColumnaReporteTesoreria.Enum]: 'texto',
};

/**
 * Adapta el <c>ReporteJsonResponse</c> de Tesorería (columnas
 * <c>key/label/tipo</c> enum numérico, filtros como lista) al shape
 * canónico del <c>&lt;ReporteShell&gt;</c> (patrón
 * <c>adaptarReporteCxc</c>).
 */
export function adaptarReporteTesoreria(
  reporte: ReporteTesoreriaBackend | undefined,
): ReporteShellData | undefined {
  if (reporte == null) return undefined;

  const filtros: Record<string, string | null> = {};
  for (const f of reporte.filtrosAplicados) filtros[f.label] = f.valor;

  const totales =
    reporte.totales == null
      ? null
      : Object.fromEntries(
          Object.entries(reporte.totales).filter(
            (par): par is [string, number] => typeof par[1] === 'number',
          ),
        );

  return {
    titulo: reporte.titulo,
    generadoEn: reporte.generadoEn,
    filtrosAplicados: filtros,
    columnas: reporte.columnas.map((c) => ({
      clave: c.key,
      etiqueta: c.label,
      tipo: TIPO_MAP[c.tipo] ?? 'texto',
    })),
    filas: reporte.filas,
    totales,
  };
}
