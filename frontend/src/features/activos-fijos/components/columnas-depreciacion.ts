import type { ColumnaLibro } from './TablaLibro';
export const columnasDepreciacion: ColumnaLibro[] = [
  { clave: 'numero', etiqueta: 'Activo', tipo: 'texto' },
  { clave: 'clase', etiqueta: 'Clase', tipo: 'texto' },
  { clave: 'sucursal', etiqueta: 'Sucursal', tipo: 'texto' },
  { clave: 'centroCosto', etiqueta: 'Centro de costo', tipo: 'texto' },
  { clave: 'base', etiqueta: 'Base', tipo: 'moneda' },
  { clave: 'tasa', etiqueta: 'Tasa anual %', tipo: 'numero' },
  { clave: 'observacion', etiqueta: 'Observación', tipo: 'texto' },
  { clave: 'importe', etiqueta: 'Depreciación', tipo: 'moneda' },
  { clave: 'acumuladaAntes', etiqueta: 'Acumulada anterior', tipo: 'moneda' },
  { clave: 'acumuladaDespues', etiqueta: 'Acumulada posterior', tipo: 'moneda' },
];
