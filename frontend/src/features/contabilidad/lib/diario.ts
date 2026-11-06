import type { PolizaDiario } from '../api/reportes';

/** Pólizas por página: el diario de un mes puede traer miles; la exportación lleva la página mostrada. */
export const POLIZAS_POR_PAGINA = 500;

/** Una línea del diario: la partida con los datos de su póliza (para la tabla y para Excel). */
export interface LineaDiario {
  fecha: string;
  folio: string;
  conceptoPoliza: string;
  estado: string;
  cuentaCodigo: string;
  cuentaNombre: string;
  concepto: string;
  sucursalNombre: string;
  cargo: number;
  abono: number;
}

export const COLUMNAS_DIARIO = [
  { clave: 'fecha', etiqueta: 'Fecha', tipo: 'fecha' as const, alineacion: null },
  { clave: 'folio', etiqueta: 'Póliza', tipo: 'texto' as const, alineacion: null },
  { clave: 'conceptoPoliza', etiqueta: 'Concepto de la póliza', tipo: 'texto' as const, alineacion: null },
  { clave: 'cuentaCodigo', etiqueta: 'Cuenta', tipo: 'texto' as const, alineacion: null },
  { clave: 'cuentaNombre', etiqueta: 'Nombre de la cuenta', tipo: 'texto' as const, alineacion: null },
  { clave: 'concepto', etiqueta: 'Concepto de la partida', tipo: 'texto' as const, alineacion: null },
  { clave: 'sucursalNombre', etiqueta: 'Sucursal', tipo: 'texto' as const, alineacion: null },
  { clave: 'cargo', etiqueta: 'Cargo', tipo: 'moneda' as const, alineacion: 'derecha' },
  { clave: 'abono', etiqueta: 'Abono', tipo: 'moneda' as const, alineacion: 'derecha' },
];

const ESTADO: Record<PolizaDiario['estado'], string> = {
  Borrador: 'Borrador',
  Autorizada: 'Autorizada',
  Anulada: 'Anulada',
};

export function lineasDiario(polizas: readonly PolizaDiario[]): LineaDiario[] {
  return polizas.flatMap((p) =>
    p.partidas.map((x) => ({
      fecha: p.fecha,
      folio: p.folio ?? 'Sin número',
      conceptoPoliza: p.concepto,
      estado: ESTADO[p.estado],
      cuentaCodigo: x.cuentaCodigo,
      cuentaNombre: x.cuentaNombre ?? 'Sin nombre',
      concepto: x.concepto ?? p.concepto,
      sucursalNombre: x.sucursalNombre ?? 'Sin sucursal',
      cargo: x.cargo,
      abono: x.abono,
    })),
  );
}

