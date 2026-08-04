import { describe, expect, it } from 'vitest';
import {
  articuloIdsUnicos,
  contarLineasSinCobertura,
  indicesParaAutoAsignarConCobertura,
  opcionesHelper,
  type SaldosPorArticulo,
} from './nueva-salida-ubicacion-helpers';
import type { FilaSalidaValues } from '@/features/almacen/schemas/salida';

const ART_A = '00000000-0000-0000-0000-0000000000a1';
const ART_B = '00000000-0000-0000-0000-0000000000b2';
const ART_C = '00000000-0000-0000-0000-0000000000c3';
const RACK_1 = '00000000-0000-0000-0000-000000000101';
const RACK_2 = '00000000-0000-0000-0000-000000000202';
const UNICA = '00000000-0000-0000-0000-0000000000f0';

function fila(partial: Partial<FilaSalidaValues>): FilaSalidaValues {
  return {
    lineaRqId: '00000000-0000-0000-0000-000000000001',
    articuloId: ART_A,
    articuloClave: 'IPP60001',
    articuloNombre: 'Aceite de corte',
    posicion: 1,
    unidadMedida: 'PZA',
    cantidadSolicitada: 10,
    cantidadPlaneadaAlmacen: 10,
    cantidadYaEntregada: 0,
    pendienteEntregar: 10,
    incluida: true,
    cantidad: 10,
    centroCostoId: null,
    centroCostoClave: null,
    centroCostoNombre: null,
    proyectoId: null,
    ubicacionReferencia: null,
    ubicacionId: null,
    comentario: null,
    ...partial,
  } as FilaSalidaValues;
}

/** ART_A vive en RACK_1 y la ÚNICA; ART_B sólo en RACK_2; ART_C sin saldo. */
const SALDOS: SaldosPorArticulo = new Map([
  [
    ART_A,
    [
      { ubicacionId: RACK_1, clave: 'RACK-1', nombre: 'Rack uno', cantidad: 40 },
      { ubicacionId: UNICA, clave: 'UNICA', nombre: 'Única', cantidad: 5 },
    ],
  ],
  [
    ART_B,
    [{ ubicacionId: RACK_2, clave: 'RACK-2', nombre: 'Rack dos', cantidad: 12 }],
  ],
  [ART_C, []],
]);

describe('articuloIdsUnicos', () => {
  it('deduplica y ordena — la clave de useQueries debe ser estable', () => {
    const filas = [
      fila({ articuloId: ART_B }),
      fila({ articuloId: ART_A }),
      fila({ articuloId: ART_B }),
    ];
    expect(articuloIdsUnicos(filas)).toEqual([ART_A, ART_B]);
  });

  it('da el mismo resultado si las filas llegan en otro orden', () => {
    const orden1 = [fila({ articuloId: ART_C }), fila({ articuloId: ART_A })];
    const orden2 = [fila({ articuloId: ART_A }), fila({ articuloId: ART_C })];
    expect(articuloIdsUnicos(orden1)).toEqual(articuloIdsUnicos(orden2));
  });
});

describe('opcionesHelper (cobertura por bin)', () => {
  it('etiqueta cada bin con a cuántos artículos del sheet cubre', () => {
    const filas = [fila({ articuloId: ART_A }), fila({ articuloId: ART_B })];
    const opciones = opcionesHelper(filas, SALDOS);

    const rack1 = opciones.find((o) => o.ubicacionId === RACK_1);
    expect(rack1?.etiqueta).toBe('RACK-1 · 1 de 2 artículos con existencia');
    expect(rack1?.articulosTotales).toBe(2);
  });

  it('ordena por cobertura descendente — el bin que resuelve más va primero', () => {
    // Los dos artículos comparten RACK_1; sólo ART_A tiene la ÚNICA.
    const saldos: SaldosPorArticulo = new Map([
      [
        ART_A,
        [
          { ubicacionId: RACK_1, clave: 'RACK-1', nombre: 'R1', cantidad: 10 },
          { ubicacionId: UNICA, clave: 'UNICA', nombre: 'U', cantidad: 3 },
        ],
      ],
      [
        ART_B,
        [{ ubicacionId: RACK_1, clave: 'RACK-1', nombre: 'R1', cantidad: 7 }],
      ],
    ]);
    const filas = [fila({ articuloId: ART_A }), fila({ articuloId: ART_B })];
    const opciones = opcionesHelper(filas, saldos);

    expect(opciones[0].ubicacionId).toBe(RACK_1);
    expect(opciones[0].articulosConExistencia).toBe(2);
    expect(opciones[1].articulosConExistencia).toBe(1);
  });

  it('ignora bins con cantidad 0 (no sirven para surtir)', () => {
    const saldos: SaldosPorArticulo = new Map([
      [
        ART_A,
        [{ ubicacionId: RACK_1, clave: 'RACK-1', nombre: 'R1', cantidad: 0 }],
      ],
    ]);
    expect(opcionesHelper([fila({ articuloId: ART_A })], saldos)).toEqual([]);
  });

  it('sub-almacén sin existencias → sin opciones (el sheet deshabilita el helper)', () => {
    const filas = [fila({ articuloId: ART_C })];
    expect(opcionesHelper(filas, SALDOS)).toEqual([]);
  });

  it('singulariza la etiqueta con un solo artículo', () => {
    const opciones = opcionesHelper([fila({ articuloId: ART_B })], SALDOS);
    expect(opciones[0].etiqueta).toBe(
      'RACK-2 · 1 de 1 artículo con existencia',
    );
  });
});

describe('indicesParaAutoAsignarConCobertura', () => {
  it('asigna sólo a filas vacías CUYO artículo tiene saldo en el helper', () => {
    const filas = [
      fila({ articuloId: ART_A, ubicacionId: null }), // 0 → cubierta, se asigna
      fila({ articuloId: ART_B, ubicacionId: null }), // 1 → sin saldo en RACK_1
      fila({ articuloId: ART_A, ubicacionId: RACK_2 }), // 2 → ya tiene bin
      fila({ articuloId: ART_C, ubicacionId: null }), // 3 → artículo sin saldo
    ];
    expect(indicesParaAutoAsignarConCobertura(filas, RACK_1, SALDOS)).toEqual([
      0,
    ]);
  });

  it('nunca pisa un bin ya capturado, aunque coincida la cobertura', () => {
    const filas = [fila({ articuloId: ART_A, ubicacionId: UNICA })];
    expect(indicesParaAutoAsignarConCobertura(filas, RACK_1, SALDOS)).toEqual(
      [],
    );
  });

  it('también asigna a filas no incluidas (si luego se incluyen, ya traen bin)', () => {
    const filas = [
      fila({ articuloId: ART_A, incluida: false, ubicacionId: null }),
    ];
    expect(indicesParaAutoAsignarConCobertura(filas, RACK_1, SALDOS)).toEqual([
      0,
    ]);
  });

  it('sin helper elegido no toca nada', () => {
    const filas = [fila({ articuloId: ART_A, ubicacionId: null })];
    expect(indicesParaAutoAsignarConCobertura(filas, null, SALDOS)).toEqual([]);
    expect(indicesParaAutoAsignarConCobertura(filas, undefined, SALDOS)).toEqual(
      [],
    );
  });
});

describe('contarLineasSinCobertura (aviso)', () => {
  it('cuenta sólo las INCLUIDAS cuyo artículo no tiene saldo en el helper', () => {
    const filas = [
      fila({ articuloId: ART_A, incluida: true }), // cubierta → no cuenta
      fila({ articuloId: ART_B, incluida: true }), // sin saldo en RACK_1 → cuenta
      fila({ articuloId: ART_C, incluida: true }), // sin saldo → cuenta
      fila({ articuloId: ART_B, incluida: false }), // no incluida → no cuenta
    ];
    expect(contarLineasSinCobertura(filas, RACK_1, SALDOS)).toBe(2);
  });

  it('es 0 sin helper (no hay contra qué comparar)', () => {
    const filas = [fila({ articuloId: ART_C, incluida: true })];
    expect(contarLineasSinCobertura(filas, null, SALDOS)).toBe(0);
  });

  it('es 0 cuando el helper cubre todas las incluidas', () => {
    const filas = [
      fila({ articuloId: ART_A, incluida: true }),
      fila({ articuloId: ART_A, incluida: true }),
    ];
    expect(contarLineasSinCobertura(filas, RACK_1, SALDOS)).toBe(0);
  });
});
