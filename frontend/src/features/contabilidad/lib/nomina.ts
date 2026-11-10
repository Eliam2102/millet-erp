import type { AliasColumnas } from './archivo';
import type { ErrorImportacionNomina } from '../api/nomina';

/** Alias de las columnas del formato de nómina (mismos que el servidor) para reconocer la fila de encabezado del .xlsx. */
export const ALIAS_NOMINA: AliasColumnas = {
  cuenta: ['cuenta', 'cuenta contable', 'num cta', 'numcta', 'codigo cuenta'],
  cargo: ['cargo', 'cargos', 'debe'],
  abono: ['abono', 'abonos', 'haber'],
  sucursal: ['sucursal', 'clave sucursal'],
  centro: ['centro', 'centro de costo', 'centro costo', 'ceco', 'cc'],
  proyecto: ['proyecto'],
  concepto: ['concepto', 'descripcion'],
  empleado: ['empleado', 'codigo empleado', 'codigo de nomina', 'no empleado', 'num empleado'],
};

/** Plantilla CSV del formato propuesto (supuesto a validar con Contabilidad). */
export const PLANTILLA_NOMINA =
  'cuenta,cargo,abono,sucursal,centro,proyecto,concepto,empleado\n' +
  '6101-001,15000.00,,MATRIZ,,,Sueldos quincena,E-001\n' +
  '2105-001,,15000.00,MATRIZ,,,Nómina por pagar,\n';

/**
 * Número de fila REAL del archivo para mostrar: el servidor numera con la cabecera = 1 sobre lo que recibió; en un .xlsx con
 * filas de título antes del encabezado se suma ese desplazamiento.
 */
export function filaReal(error: ErrorImportacionNomina, desplazamiento: number): number | null {
  return error.fila === null ? null : error.fila + desplazamiento;
}

/** Motivo por el que «Importar» está deshabilitado (undefined = se puede). */
export function motivoImportar(a: {
  tipoOperacionId: string;
  fecha: string;
  referencia: string;
  archivo: boolean;
  vistaValida: boolean | null;
}): string | undefined {
  if (!a.archivo) return 'Elige el archivo de la nómina.';
  if (!a.tipoOperacionId) return 'Elige el tipo de póliza de nómina.';
  if (!a.fecha) return 'Indica la fecha contable.';
  if (!a.referencia.trim()) return 'Indica la referencia del lote (periodo y tipo de nómina).';
  if (a.vistaValida === null) return 'Revisa el archivo antes de importar.';
  if (!a.vistaValida) return 'Corrige los errores del archivo y vuelve a revisarlo.';
  return undefined;
}
