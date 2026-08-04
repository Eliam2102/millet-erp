import { describe, expect, it } from 'vitest';
import {
  construirFilas,
  contarLineasEnOtraUbicacion,
  indicesParaAutoAsignar,
} from './nueva-recepcion-helpers';
import type { LineaOrdenCompraResponse } from '@/features/compras/ordenes/api/types';
import type { FilaRecepcionValues } from '@/features/almacen/schemas/recepcion';

function lineaOc(
  partial: Partial<LineaOrdenCompraResponse>,
): LineaOrdenCompraResponse {
  return {
    id: '00000000-0000-0000-0000-000000000001',
    posicion: 1,
    articuloId: '00000000-0000-0000-0000-000000000002',
    articuloClave: 'IPP60001',
    articuloNombre: 'Aceite de corte',
    descripcionExtendida: null,
    cantidad: 10,
    unidadMedida: 'PZA',
    precioUnitario: 100,
    ivaImporte: 16,
    retencionIsr: null,
    subtotalLinea: 1000,
    departamentoSolicitanteId: '00000000-0000-0000-0000-000000000004',
    requisicionId: null,
    lineaRequisicionId: null,
    fechaEntregaLinea: null,
    cantidadRecibida: 0,
    cantidadFacturada: 0,
    textoAdicional: null,
    ...partial,
  };
}

describe('construirFilas', () => {
  it('mapea líneas pendientes con pendiente = cantidad - cantidadRecibida', () => {
    const filas = construirFilas([
      lineaOc({ cantidad: 10, cantidadRecibida: 3 }),
    ]);
    expect(filas).toHaveLength(1);
    expect(filas[0]).toMatchObject({
      pendiente: 7,
      cantidadSolicitada: 10,
      cantidadYaRecibida: 3,
      cantidad: 7,
      incluida: false,
    });
  });

  it('filtra líneas ya completas (pendiente = 0)', () => {
    const filas = construirFilas([
      lineaOc({ cantidad: 10, cantidadRecibida: 10 }),
      lineaOc({ id: 'l-2', cantidad: 5, cantidadRecibida: 2 }),
    ]);
    expect(filas).toHaveLength(1);
    expect(filas[0].lineaOcId).toBe('l-2');
  });

  it('clamp pendiente a ≥ 0 si cantidadRecibida > cantidad (defensivo)', () => {
    const filas = construirFilas([
      lineaOc({ cantidad: 5, cantidadRecibida: 7 }),
    ]);
    // Math.max(0, 5 - 7) = 0 → la línea queda filtrada fuera porque
    // pendiente == 0.
    expect(filas).toHaveLength(0);
  });

  it('default cantidad = pendiente (almacenista solo edita si difiere)', () => {
    const filas = construirFilas([
      lineaOc({ cantidad: 100, cantidadRecibida: 25 }),
    ]);
    expect(filas[0].cantidad).toBe(75);
    expect(filas[0].pendiente).toBe(75);
  });

  it('preserva metadata de la línea (artículo, UM, posición)', () => {
    const filas = construirFilas([
      lineaOc({
        id: 'l-x',
        posicion: 3,
        articuloId: 'art-x',
        unidadMedida: 'KG',
      }),
    ]);
    expect(filas[0]).toMatchObject({
      lineaOcId: 'l-x',
      posicion: 3,
      articuloId: 'art-x',
      unidadMedida: 'KG',
    });
  });

  it('propaga la etiqueta legible del artículo (clave + nombre, ADR-0042)', () => {
    const filas = construirFilas([
      lineaOc({ articuloClave: 'ACC86024', articuloNombre: 'PVB acústico' }),
      lineaOc({ id: 'l-2', articuloClave: null, articuloNombre: null }),
    ]);
    // Con etiqueta: el sheet muestra clave + nombre, no el UUID.
    expect(filas[0]).toMatchObject({
      articuloClave: 'ACC86024',
      articuloNombre: 'PVB acústico',
    });
    // Sin etiqueta (puerto no resolvió): null → el sheet cae al id truncado.
    expect(filas[1]).toMatchObject({ articuloClave: null, articuloNombre: null });
  });
});

// ── Almacén-por-línea PR4: helper de cabecera nivel 4 ──────────────────────

const BIN_A = '00000000-0000-0000-0000-0000000000aa';
const BIN_B = '00000000-0000-0000-0000-0000000000bb';

function fila(partial: Partial<FilaRecepcionValues>): FilaRecepcionValues {
  return {
    lineaOcId: '00000000-0000-0000-0000-000000000001',
    articuloId: '00000000-0000-0000-0000-000000000002',
    articuloClave: 'IPP60001',
    articuloNombre: 'Aceite de corte',
    posicion: 1,
    unidadMedida: 'PZA',
    cantidadSolicitada: 10,
    cantidadYaRecibida: 0,
    pendiente: 10,
    incluida: true,
    cantidad: 10,
    ubicacionReferencia: null,
    ubicacionId: null,
    comentario: null,
    ...partial,
  };
}

describe('indicesParaAutoAsignar (helper de cabecera)', () => {
  it('devuelve solo las filas SIN ubicación', () => {
    const filas = [
      fila({ ubicacionId: null }), // 0 → vacía, se auto-asigna
      fila({ ubicacionId: BIN_B }), // 1 → ya tiene otra, se respeta
      fila({ ubicacionId: null }), // 2 → vacía, se auto-asigna
      fila({ ubicacionId: BIN_A }), // 3 → ya coincide, se respeta
    ];
    expect(indicesParaAutoAsignar(filas)).toEqual([0, 2]);
  });

  it('no toca ninguna fila cuando todas ya tienen ubicación', () => {
    const filas = [fila({ ubicacionId: BIN_A }), fila({ ubicacionId: BIN_B })];
    expect(indicesParaAutoAsignar(filas)).toEqual([]);
  });

  it('auto-asigna también las filas no incluidas (si luego se incluyen, ya traen bin)', () => {
    const filas = [fila({ incluida: false, ubicacionId: null })];
    expect(indicesParaAutoAsignar(filas)).toEqual([0]);
  });
});

describe('contarLineasEnOtraUbicacion (aviso de divergencia)', () => {
  it('cuenta solo las INCLUIDAS que van a un bin distinto del helper', () => {
    const filas = [
      fila({ incluida: true, ubicacionId: BIN_B }), // diverge → cuenta
      fila({ incluida: true, ubicacionId: BIN_A }), // coincide → no cuenta
      fila({ incluida: true, ubicacionId: null }), // vacía → no cuenta
      fila({ incluida: false, ubicacionId: BIN_B }), // no incluida → no cuenta
    ];
    expect(contarLineasEnOtraUbicacion(filas, BIN_A)).toBe(1);
  });

  it('es 0 sin helper elegido (no hay contra qué divergir)', () => {
    const filas = [fila({ incluida: true, ubicacionId: BIN_B })];
    expect(contarLineasEnOtraUbicacion(filas, null)).toBe(0);
    expect(contarLineasEnOtraUbicacion(filas, undefined)).toBe(0);
  });

  it('es 0 cuando todas las incluidas coinciden con el helper', () => {
    const filas = [
      fila({ incluida: true, ubicacionId: BIN_A }),
      fila({ incluida: true, ubicacionId: BIN_A }),
    ];
    expect(contarLineasEnOtraUbicacion(filas, BIN_A)).toBe(0);
  });
});
