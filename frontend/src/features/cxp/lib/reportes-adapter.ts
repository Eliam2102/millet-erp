import type { BackendReporteResponse } from '@/features/cxp/api/useReportes';

/**
 * Mapea el shape backend (PascalCase serializado a camelCase) al shape
 * que <c>&lt;ReporteShell&gt;</c> consume. El backend usa enums
 * numéricos para tipo de columna; ReporteShell usa uniones string.
 *
 * <para>Backend <c>TipoColumnaReporte</c>: 1=Texto, 2=Entero,
 * 3=Numerico, 4=Moneda, 5=Fecha, 6=FechaHora, 7=Booleano, 8=Enum.</para>
 *
 * <para>ReporteShell <c>tipo</c>: 'texto' | 'numero' | 'moneda' | 'fecha'.</para>
 */

type ShellTipo = 'texto' | 'numero' | 'moneda' | 'fecha';

function mapTipo(tipo: number): ShellTipo {
  switch (tipo) {
    case 4:
      return 'moneda';
    case 2:
    case 3:
      return 'numero';
    case 5:
    case 6:
      return 'fecha';
    default:
      return 'texto';
  }
}

export interface ReporteShellShape {
  titulo: string;
  generadoEn: string;
  filtrosAplicados: Record<string, string | null>;
  columnas: {
    clave: string;
    etiqueta: string;
    tipo: ShellTipo;
    alineacion: string | null;
    anchoPx: number | null;
  }[];
  filas: Record<string, unknown>[];
  totales: Record<string, number> | null;
}

export function backendToShellShape(data: BackendReporteResponse): ReporteShellShape {
  const filtrosAplicados: Record<string, string | null> = {};
  for (const f of data.filtrosAplicados) {
    filtrosAplicados[f.label] = f.valor;
  }

  // Totales del backend pueden venir mixtos (numéricos + strings); filtrar
  // a sólo valores numéricos para que ReporteShell los formatee con moneda/numero.
  const totales: Record<string, number> = {};
  if (data.totales) {
    for (const [k, v] of Object.entries(data.totales)) {
      if (typeof v === 'number') totales[k] = v;
    }
  }

  return {
    titulo: data.titulo,
    generadoEn: data.generadoEn,
    filtrosAplicados,
    columnas: data.columnas.map((c) => ({
      clave: c.key,
      etiqueta: c.label,
      // ReporteShell usa MXN al formatear 'moneda'. Con una columna de moneda,
      // mostramos el importe numérico y conservamos su divisa explícita en cada fila.
      tipo:
        c.tipo === 4 && data.columnas.some((col) => col.key === 'moneda')
          ? 'numero'
          : mapTipo(c.tipo),
      alineacion: null,
      anchoPx: null,
    })),
    filas: [...data.filas, ...filasTotalesMoneda(data.totales)],
    totales: Object.keys(totales).length > 0 ? totales : null,
  };
}

/** Filas de resumen por moneda: se conservan en pantalla, PDF y Excel sin sumar MXN y USD. */
export function filasTotalesMoneda(
  totales: Record<string, unknown> | null,
): Record<string, unknown>[] {
  if (!Array.isArray(totales?.por_moneda)) return [];
  return totales.por_moneda
    .filter(
      (t): t is Record<string, unknown> =>
        typeof t === 'object' && t !== null && !Array.isArray(t) && 'moneda' in t,
    )
    .map((t) => ({
      ...t,
      proveedor_nombre: `Total ${String(t.moneda)}`,
      rfc: '',
      obra: '',
      en_revision: null,
    }));
}
