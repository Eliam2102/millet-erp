import type { CuerpoImportacion, Perfil } from '../api/types';

/** Límite de tamaño razonable para 5 000 filas de texto; el servidor valida además el tope de filas. */
export const MAX_BYTES = 10 * 1024 * 1024;

export class ArchivoInvalidoError extends Error {}

function aBase64(bytes: Uint8Array): string {
  let s = '';
  for (let i = 0; i < bytes.length; i += 0x8000) {
    s += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
  }
  return btoa(s);
}

/** Alias de columnas por columna canónica (`importacion.columnas` de GET /configuracion-formato). */
export type AliasColumnas = Record<string, string[]>;

export interface HojaXlsx {
  nombre: string;
  /** Filas con al menos una celda con valor. */
  filasConDatos: number;
  /** Celdas como TEXTO; el índice es el número de fila del archivo - 1 (las filas vacías se conservan). */
  matriz: string[][];
}

export type ArchivoAbierto =
  | { tipo: 'csv'; cuerpo: CuerpoImportacion }
  | { tipo: 'xlsx'; nombre: string; hojas: HojaXlsx[] };

export interface HojaPreparada {
  cuerpo: CuerpoImportacion;
  /** Fila del archivo (1-based) donde se detectó el encabezado. */
  filaEncabezado: number;
  /** Sumar a los números de fila que reporta el servidor (cabecera = 1) para obtener la fila real del archivo. */
  desplazamiento: number;
  /** Se usó la fila 1 por no reconocer ningún encabezado. */
  sinEncabezadoReconocido: boolean;
}

/** Misma normalización que el servidor (`FormatoCatalogo.NormalizarCabecera`): sin acentos, minúsculas, espacios/guiones → `_`. */
export function normalizarCabecera(s: string): string {
  return s
    .normalize('NFD')
    .replace(/\p{Mn}/gu, '')
    .toLowerCase()
    .trim()
    .replace(/[\s-]+/g, '_')
    .replace(/^_+|_+$/g, '');
}

const FILAS_BUSQUEDA_ENCABEZADO = 15;

function canonicasEn(fila: string[], alias: AliasColumnas): number {
  const mapa = new Map<string, string>();
  for (const [canonica, lista] of Object.entries(alias)) for (const a of lista) mapa.set(normalizarCabecera(a), canonica);
  return new Set(fila.map((c) => mapa.get(normalizarCabecera(c))).filter(Boolean)).size;
}

/** Primera fila (1-based, dentro de las primeras 15) con al menos 2 celdas que coinciden con alias de columnas canónicas; `null` si ninguna. */
export function detectarEncabezado(matriz: string[][], alias: AliasColumnas): number | null {
  const tope = Math.min(matriz.length, FILAS_BUSQUEDA_ENCABEZADO);
  for (let i = 0; i < tope; i++) if (canonicasEn(matriz[i], alias) >= 2) return i + 1;
  return null;
}

/** Índice de la hoja a preseleccionar: nombre con «plan de cuentas» y encabezado reconocible (gana la de más columnas reconocidas); si no, la primera. */
export function hojaSugerida(hojas: HojaXlsx[], alias: AliasColumnas): number {
  let mejor = -1;
  let puntaje = 0;
  hojas.forEach((h, i) => {
    if (!normalizarCabecera(h.nombre).replace(/_/g, ' ').includes('plan de cuentas')) return;
    const fila = detectarEncabezado(h.matriz, alias);
    const p = fila === null ? 0 : canonicasEn(h.matriz[fila - 1], alias);
    if (p > puntaje) [mejor, puntaje] = [i, p];
  });
  return mejor >= 0 ? mejor : 0;
}

/**
 * Abre el archivo elegido.
 * - `.csv`: se envían los bytes tal cual (base64). BOM UTF-8/UTF-16, UTF-8 con respaldo Windows-1252,
 *   delimitador y comillas los normaliza el servidor, único normalizador (05-contrato §Tolerancias);
 *   así el cliente no re-decodifica ni altera el contenido ni la huella. El servidor exige el encabezado en la fila 1.
 * - `.xlsx`: se leen todas las hojas con exceljs y TODAS las celdas pasan como TEXTO (un código `1.01` no se vuelve número).
 */
export async function abrirArchivo(file: File): Promise<ArchivoAbierto> {
  const nombre = file.name;
  const ext = nombre.toLowerCase().split('.').pop();
  if (ext !== 'csv' && ext !== 'xlsx') {
    throw new ArchivoInvalidoError('Formato no soportado: elige un archivo .csv o .xlsx.');
  }
  if (file.size === 0) throw new ArchivoInvalidoError('El archivo está vacío.');
  if (file.size > MAX_BYTES) throw new ArchivoInvalidoError('El archivo supera los 10 MB.');
  const buffer = await file.arrayBuffer();

  if (ext === 'csv') return { tipo: 'csv', cuerpo: { archivoNombre: nombre, csvBase64: aBase64(new Uint8Array(buffer)) } };

  const { Workbook } = await import('exceljs');
  const wb = new Workbook();
  try {
    await wb.xlsx.load(buffer);
  } catch {
    throw new ArchivoInvalidoError('No se pudo leer el .xlsx: el archivo está dañado o no es un libro de Excel.');
  }
  const hojas = wb.worksheets.map((h): HojaXlsx => {
    const cols = h.columnCount;
    const matriz = Array.from({ length: h.rowCount }, (_, r) =>
      Array.from({ length: cols }, (_, c) => h.getRow(r + 1).getCell(c + 1).text.trim()));
    while (matriz.length > 0 && matriz.at(-1)!.every((t) => t === '')) matriz.pop(); // vacías al final: sin valor
    return { nombre: h.name, filasConDatos: matriz.filter((f) => f.some((t) => t !== '')).length, matriz };
  }).filter((h) => h.matriz.length > 0);
  if (hojas.length === 0) throw new ArchivoInvalidoError('El libro no tiene hojas con datos.');
  return { tipo: 'xlsx', nombre, hojas };
}

/**
 * Arma el cuerpo de importación de una hoja: detecta la fila de encabezado, descarta las filas previas (título)
 * y conserva las filas vacías posteriores. El servidor numera con cabecera = 1; `desplazamiento` lo convierte
 * al número de fila real del archivo (el backend no cambia).
 */
export function prepararHoja(nombreArchivo: string, hoja: HojaXlsx, alias: AliasColumnas): HojaPreparada {
  const detectada = detectarEncabezado(hoja.matriz, alias);
  const filaEncabezado = detectada ?? 1;
  const [encabezado, ...resto] = hoja.matriz.slice(filaEncabezado - 1);
  return {
    cuerpo: {
      archivoNombre: nombreArchivo,
      columnas: encabezado,
      filas: resto.map((f) => f.map((t) => (t === '' ? null : t))),
    },
    filaEncabezado,
    desplazamiento: filaEncabezado - 1,
    sinEncabezadoReconocido: detectada === null,
  };
}

/**
 * Reporte descargable del perfilado: solo conteos, filas y códigos de error. Se retiran los valores de celda
 * no reconocidos (`valoresNoReconocidos`): son contenido del archivo y se ven solo en pantalla.
 */
export function perfilSinContenido(perfil: Perfil, desplazamiento = 0): string {
  return JSON.stringify(
    perfil,
    (k, v: unknown) => {
      if (k === 'valoresNoReconocidos') return undefined;
      // Números de fila del servidor (cabecera = 1) → fila real del archivo.
      if (k === 'fila' && typeof v === 'number') return v + desplazamiento;
      if (k === 'ejemplos' && Array.isArray(v) && v.every((x) => typeof x === 'number')) return v.map((x: number) => x + desplazamiento);
      return v;
    },
    2,
  );
}
