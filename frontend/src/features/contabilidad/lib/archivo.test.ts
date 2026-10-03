import { describe, expect, it } from 'vitest';
import {
  ArchivoInvalidoError, abrirArchivo, detectarEncabezado, hojaSugerida, perfilSinContenido, prepararHoja,
} from './archivo';
import { ALIAS_FIX, libroTresHojas, libroUnaHoja } from './__fixtures__/libro-fix';
import type { Perfil } from '../api/types';

const decodificar = (b64: string) => new Uint8Array([...atob(b64)].map((c) => c.charCodeAt(0)));

async function hojasDelLibro(archivo: Promise<File>) {
  const a = await abrirArchivo(await archivo);
  if (a.tipo !== 'xlsx') throw new Error('se esperaba xlsx');
  return a.hojas;
}

describe('abrirArchivo', () => {
  it('csv: envía los bytes tal cual en base64 (BOM incluido; el servidor lo normaliza)', async () => {
    const bytes = new Uint8Array([0xef, 0xbb, 0xbf, ...new TextEncoder().encode('codigo;nombre\nFIX-1;Caña\n')]);
    const abierto = await abrirArchivo(new File([bytes], 'FIX.csv'));
    if (abierto.tipo !== 'csv') throw new Error('se esperaba csv');
    expect(abierto.cuerpo.archivoNombre).toBe('FIX.csv');
    expect(Array.from(decodificar(abierto.cuerpo.csvBase64!))).toEqual(Array.from(bytes));
  });

  it('csv en Windows-1252 (no UTF-8) no se altera en el cliente', async () => {
    const bytes = new Uint8Array([...new TextEncoder().encode('codigo,nombre\nFIX-1,Ca'), 0xf1, 0x61, 0x0a]);
    const abierto = await abrirArchivo(new File([bytes], 'FIX.CSV'));
    if (abierto.tipo !== 'csv') throw new Error('se esperaba csv');
    expect(Array.from(decodificar(abierto.cuerpo.csvBase64!))).toEqual(Array.from(bytes));
  });

  it('xlsx de 3 hojas: lista nombre y filas con datos de cada hoja', async () => {
    const hojas = await hojasDelLibro(libroTresHojas());
    expect(hojas.map((h) => [h.nombre, h.filasConDatos])).toEqual([
      ['Plan de cuentas (5)', 2],
      ['Catalogo', 2],
      ['Plan de cuentas-VILO', 4], // título + encabezado + 2 cuentas (las filas vacías no cuentan)
    ]);
  });

  it('rechaza extensión desconocida, archivo vacío y xlsx dañado', async () => {
    await expect(abrirArchivo(new File(['x'], 'FIX.pdf'))).rejects.toBeInstanceOf(ArchivoInvalidoError);
    await expect(abrirArchivo(new File([], 'FIX.csv'))).rejects.toThrow('vacío');
    await expect(abrirArchivo(new File(['no es un zip'], 'FIX.xlsx'))).rejects.toThrow('dañado');
  });
});

describe('hoja correcta y encabezado en xlsx', () => {
  it('preselecciona la hoja «plan de cuentas» con encabezado reconocible (no la 1.ª con el mismo nombre)', async () => {
    expect(hojaSugerida(await hojasDelLibro(libroTresHojas()), ALIAS_FIX)).toBe(2);
  });

  it('sin hoja candidata preselecciona la primera', () => {
    expect(hojaSugerida([{ nombre: 'Otra', filasConDatos: 1, matriz: [['x']] }], ALIAS_FIX)).toBe(0);
  });

  it('detecta el encabezado en la fila 2 (el título de la fila 1 no coincide con alias)', async () => {
    const hoja = (await hojasDelLibro(libroTresHojas()))[2];
    expect(detectarEncabezado(hoja.matriz, ALIAS_FIX)).toBe(2);
  });

  it('prepara el cuerpo: descarta el título, conserva la columna vacía y las filas vacías; desplazamiento = 1', async () => {
    const hoja = (await hojasDelLibro(libroTresHojas()))[2];
    const p = prepararHoja('FIX-libro.xlsx', hoja, ALIAS_FIX);
    expect(p).toMatchObject({ filaEncabezado: 2, desplazamiento: 1, sinEncabezadoReconocido: false });
    expect(p.cuerpo.columnas).toEqual(['Nivel Contable', 'Numero', 'Cuenta', 'Tipo', '', 'Nivel de cuenta SAT', 'Código agrupador SAT']);
    const filas = p.cuerpo.filas!;
    expect(filas[0]).toEqual([null, null, null, null, null, null, null]); // fila 3 del archivo (vacía)
    expect(filas[1].slice(0, 3)).toEqual(['1', '100', 'FIX Activo']); // fila 4
    expect(filas[4].slice(0, 3)).toEqual(['2', '110', 'FIX Caja']); // fila 7
    // El servidor numera con cabecera = 1 (número = índice en `filas` + 2); con el desplazamiento es el de la fila del archivo.
    expect(1 + 2 + p.desplazamiento).toBe(4);
    expect(4 + 2 + p.desplazamiento).toBe(7);
  });

  it('una sola hoja con encabezado en la fila 1: desplazamiento 0 y filas vacías conservadas', async () => {
    const hojas = await hojasDelLibro(libroUnaHoja());
    expect(hojas).toHaveLength(1);
    const p = prepararHoja('FIX-una.xlsx', hojas[0], ALIAS_FIX);
    expect(p).toMatchObject({ filaEncabezado: 1, desplazamiento: 0, sinEncabezadoReconocido: false });
    expect(p.cuerpo.columnas).toEqual(['codigo', 'nombre']);
    expect(p.cuerpo.filas).toEqual([['001.01', 'FIX Uno'], [null, null], ['002', 'FIX Dos']]); // códigos como texto
  });

  it('sin encabezado reconocible: usa la fila 1 y lo indica', () => {
    const p = prepararHoja('FIX.xlsx', { nombre: 'X', filasConDatos: 2, matriz: [['a', 'b'], ['1', '2']] }, ALIAS_FIX);
    expect(p).toMatchObject({ filaEncabezado: 1, desplazamiento: 0, sinEncabezadoReconocido: true });
  });

  it('solo busca el encabezado en las primeras 15 filas', () => {
    const matriz = [...Array.from({ length: 15 }, () => ['x', 'y']), ['codigo', 'nombre']];
    expect(detectarEncabezado(matriz, ALIAS_FIX)).toBeNull();
  });
});

describe('perfilSinContenido', () => {
  const base = {
    resumen: { filasLeidas: 2 },
    distribuciones: { naturaleza: { noReconocidos: 1, valoresNoReconocidos: ['FIX-SECRETO'] } },
    estructura: {}, control: {}, pendientesValidacion: {}, nivelContable: { ejemplos: [3, 6] }, columnasSinMapeo: [],
    porCodigoError: [{ codigo: 'CONTAB_IMPORT_X', severidad: 'Error', conteo: 1, ejemplos: [{ fila: 4, columna: 'codigo' }] }],
    queSeReabre: [],
  } as unknown as Perfil;

  it('conserva conteos y filas, y retira los valores de celda no reconocidos', () => {
    const json = perfilSinContenido(base);
    expect(json).not.toContain('FIX-SECRETO');
    expect(json).toContain('"conteo": 1');
    expect(json).toContain('"fila": 4');
  });

  it('con desplazamiento, los números de fila del reporte son los del archivo', () => {
    const o = JSON.parse(perfilSinContenido(base, 1));
    expect(o.porCodigoError[0].ejemplos[0].fila).toBe(5);
    expect(o.nivelContable.ejemplos).toEqual([4, 7]);
  });
});
