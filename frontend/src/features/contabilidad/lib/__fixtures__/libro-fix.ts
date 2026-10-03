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

/**
 * FIX-formato-Laura (solo estructura del archivo de Contabilidad, datos ficticios): hoja de reglas + «Plan de cuentas» con el
 * nombre de empresa en la fila 1, el encabezado de 8 columnas en la fila 2, un rubro, dos títulos de reporte sin código y
 * una fila vacía. Fila 3 = rubro, fila 4 = título, fila 5 = FIX-101, fila 7 = FIX-101.01, fila 8 = título.
 */
export async function libroFormatoLaura(): Promise<File> {
  const wb = new Workbook();
  wb.addWorksheet('Niveles y acumulación').addRow(['FIX texto de reglas de niveles']);
  const h = wb.addWorksheet('Plan de cuentas');
  const bal = 'Estado de Posicion Financiera (Balance)';
  h.addRow(['FIX Empresa SA de CV']);
  h.addRow(['Nivel Contable', 'Numero', 'Cuenta', 'Tipo', 'Naturaleza', 'Reporte', 'Nivel de cuenta SAT', 'Código agrupador SAT']);
  h.addRow(['', 'FIX-100.00.00.00', 'FIX ACTIVO', 'Rubro', '', bal, '', '']);
  h.addRow(['', '', 'FIX Activo circulante', 'Título', '', bal, '', '']);
  h.addRow([1, 'FIX-101.00.00.00', 'FIX Caja y bancos', 'Activo circulante', 'Deudora', bal, 1, '101']);
  h.addRow([]);
  h.addRow([2, 'FIX-101.01.00.00', 'FIX Bancos', 'Activo circulante', 'Deudora', bal, 2, '102']);
  h.addRow(['', '', 'FIX Pasivo', 'Titulo', '', bal, '', '']);
  return aFile(wb, 'FIX-formato-Laura.xlsx');
}
