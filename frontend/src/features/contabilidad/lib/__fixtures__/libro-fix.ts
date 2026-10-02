import { Workbook } from 'exceljs';
import type { AliasColumnas } from '../archivo';

/** Alias vigentes del servidor (subconjunto de `ColumnasDefault`), para pruebas. */
export const ALIAS_FIX: AliasColumnas = {
  codigo: ['codigo', 'numero', 'account_code', 'clave'],
  nombre: ['nombre', 'cuenta', 'descripcion', 'name', 'description'],
  codigo_agrupador: ['codigo_agrupador', 'agrupador', 'codigo_agrupador_sat'],
  nivel_contable: ['nivel_contable'],
};

async function aFile(wb: Workbook, nombre: string): Promise<File> {
  return new File([(await wb.xlsx.writeBuffer()) as ArrayBuffer], nombre);
}

/**
 * Libro FIX-* con la estructura del archivo real (solo estructura): 3 hojas; la 3.ª («Plan de cuentas-VILO»)
 * trae un título en la fila 1, el encabezado en la fila 2 (con una columna vacía) y filas vacías intercaladas.
 * Datos: fila 4 = FIX-100, fila 7 = FIX-110 (las filas 3, 5 y 6 están vacías).
 */
export async function libroTresHojas(): Promise<File> {
  const wb = new Workbook();
  const otra = wb.addWorksheet('Plan de cuentas (5)'); // misma frase en el nombre, sin encabezado reconocible
  otra.addRow(['Folio', 'Detalle']);
  otra.addRow(['FIX-A', 'FIX otra numeración']);
  const cat = wb.addWorksheet('Catalogo');
  cat.addRow(['Cuenta', 'Saldo']); // solo 1 alias reconocido
  cat.addRow(['FIX otro catálogo', '0']);
  const h = wb.addWorksheet('Plan de cuentas-VILO');
  h.addRow(['FIX Empresa SA de CV']);
  h.addRow(['Nivel Contable', 'Numero', 'Cuenta', 'Tipo', '', 'Nivel de cuenta SAT', 'Código agrupador SAT']);
  h.addRow([]);
  h.addRow([1, '100', 'FIX Activo', 'Mayor', '', 1, '100']);
  h.addRow([]);
  h.addRow([]);
  h.addRow([2, '110', 'FIX Caja', 'Detalle', '', 2, '100.1']);
  return aFile(wb, 'FIX-libro.xlsx');
}

/** Una sola hoja con el encabezado en la fila 1 (caso simple). */
export async function libroUnaHoja(): Promise<File> {
  const wb = new Workbook();
  const h = wb.addWorksheet('Hoja1');
  h.addRow(['codigo', 'nombre']);
  h.addRow(['001.01', 'FIX Uno']);
  h.addRow([]);
  h.addRow(['002', 'FIX Dos']);
  return aFile(wb, 'FIX-una.xlsx');
}
