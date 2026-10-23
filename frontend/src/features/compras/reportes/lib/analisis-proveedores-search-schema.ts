import { z } from 'zod';

/**
 * Search params de «Análisis de proveedores» (F1-COM-13). Todo el estado
 * de filtros vive en la URL para que sobreviva a F5 y se pueda compartir.
 * <c>desde</c>/<c>hasta</c> sin valor = trimestre en curso (ver
 * <c>trimestreActual</c>); no se escriben en la URL hasta que se editan.
 */
export const TABS_ANALISIS = ['compras', 'saldos', 'cumplimiento'] as const;
export const AGRUPAR_POR = ['proveedor', 'articulo', 'departamento', 'mes'] as const;

const texto = z.string().min(1).optional().catch(undefined);

export const AnalisisProveedoresSearchSchema = z.object({
  tab: z.enum(TABS_ANALISIS).catch('compras'),
  agruparPor: z.enum(AGRUPAR_POR).catch('proveedor'),
  desde: texto,
  hasta: texto,
  sucursalId: texto,
  proveedorId: texto,
  articuloId: texto,
  departamentoId: texto,
  moneda: texto,
  estado: texto,
});

export type AnalisisProveedoresSearch = z.infer<typeof AnalisisProveedoresSearchSchema>;

const iso = (d: Date) =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

/** Primer y último día del trimestre calendario de <c>hoy</c>. */
export function trimestreActual(hoy: Date = new Date()): { desde: string; hasta: string } {
  const mesInicio = Math.floor(hoy.getMonth() / 3) * 3;
  return {
    desde: iso(new Date(hoy.getFullYear(), mesInicio, 1)),
    hasta: iso(new Date(hoy.getFullYear(), mesInicio + 3, 0)),
  };
}

/** Mensaje de error del periodo según el contrato (hasta ≥ desde, ≤ 366 días) o <c>null</c>. */
export function errorPeriodo(desde: string, hasta: string): string | null {
  if (!desde || !hasta) return 'Captura las fechas desde y hasta.';
  if (hasta < desde) return 'La fecha «Hasta» no puede ser anterior a «Desde».';
  const dias = (Date.parse(hasta) - Date.parse(desde)) / 86_400_000;
  // Mismo tope que el backend: hasta − desde < 366 días.
  if (dias >= 366) return 'El periodo no puede exceder 366 días.';
  return null;
}
