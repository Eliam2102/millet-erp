import { describe, expect, it } from 'vitest';
import {
  clasificarEntrega,
  construirFilasDeRq,
} from './nueva-salida-helpers';
import type { LineaResponse } from '@/features/compras/api/types';

function linea(partial: Partial<LineaResponse>): LineaResponse {
  return {
    id: '00000000-0000-0000-0000-000000000001',
    posicion: 1,
    articuloId: '00000000-0000-0000-0000-000000000002',
    articuloClave: 'IPP60001',
    articuloNombre: 'Aceite de corte',
    cantidad: 10,
    unidadMedida: 'PZA',
    precioEstimadoMonto: 100,
    precioEstimadoMoneda: 'MXN',
    cuentaContableId: null,
    centroCostoId: null,
    proyecto: null,
    fechaRequerida: null,
    notas: null,
    cantDeAlmacen: 10,
    cantDeCompra: 0,
    cantRecibida: 0,
    cantPendiente: 0,
    reservaId: null,
    ...partial,
  };
}

describe('construirFilasDeRq', () => {
  it('hereda el CC-Máquina (id + clave + nombre) de la línea de RQ para el display bloqueado', () => {
    const filas = construirFilasDeRq([
      linea({
        centroCostoId: '0c000000-0000-0000-0000-000000000001',
        centroCostoClave: 'MCLC101',
        centroCostoNombre: 'Gantry',
      }),
    ]);
    expect(filas[0]).toMatchObject({
      centroCostoId: '0c000000-0000-0000-0000-000000000001',
      centroCostoClave: 'MCLC101',
      centroCostoNombre: 'Gantry',
    });
  });

  it('usa el pendiente derivado del backend (cantPendienteEntregar) cuando viene', () => {
    const filas = construirFilasDeRq([
      linea({
        cantidad: 10,
        cantDeAlmacen: 10,
        cantEntregadoDeAlmacen: 4,
        cantPendienteEntregar: 6,
      }),
    ]);
    expect(filas).toHaveLength(1);
    expect(filas[0]).toMatchObject({
      pendienteEntregar: 6,
      cantidadYaEntregada: 4,
      cantidad: 6, // default = pendiente
      incluida: false,
    });
  });

  it('fallback: pendiente = (cantDeAlmacen + cantRecibida) − entregado cuando falta cantPendienteEntregar', () => {
    const filas = construirFilasDeRq([
      // sin cantPendienteEntregar (respuesta vieja): debe sumar lo recibido
      linea({
        cantDeAlmacen: 4,
        cantRecibida: 3,
        cantEntregadoDeAlmacen: 2,
      }),
    ]);
    expect(filas[0].pendienteEntregar).toBe(5); // (4 + 3) − 2
  });

  it('línea 100% compra ya recibida es entregable (fallback incluye cantRecibida)', () => {
    const filas = construirFilasDeRq([
      // todo el cubrimiento fue por OC; el material ya se recibió
      linea({
        cantidad: 10,
        cantDeAlmacen: 0,
        cantDeCompra: 10,
        cantRecibida: 10,
      }),
    ]);
    // Antes (fallback solo cantDeAlmacen) daba 0 → quedaba inentregable.
    expect(filas[0].pendienteEntregar).toBe(10);
  });

  it('fallback clamp a ≥ 0 si se entregó de más (defensivo)', () => {
    const filas = construirFilasDeRq([
      linea({ cantDeAlmacen: 5, cantRecibida: 0, cantEntregadoDeAlmacen: 7 }),
    ]);
    expect(filas[0].pendienteEntregar).toBe(0);
  });

  it('NO filtra líneas sin pendiente — se muestran todas (entrega en parcialidades)', () => {
    const filas = construirFilasDeRq([
      linea({ id: 'l-1', cantPendienteEntregar: 0 }), // ya entregada del todo
      linea({ id: 'l-2', cantPendienteEntregar: 3 }),
    ]);
    expect(filas).toHaveLength(2);
    expect(filas.map((f) => f.lineaRqId)).toEqual(['l-1', 'l-2']);
  });

  it('preserva metadata de la línea (artículo, UM, posición, solicitada)', () => {
    const filas = construirFilasDeRq([
      linea({
        id: 'l-x',
        posicion: 3,
        articuloId: 'art-x',
        unidadMedida: 'KG',
        cantidad: 42,
      }),
    ]);
    expect(filas[0]).toMatchObject({
      lineaRqId: 'l-x',
      posicion: 3,
      articuloId: 'art-x',
      unidadMedida: 'KG',
      cantidadSolicitada: 42,
    });
  });

  it('propaga la etiqueta legible del artículo (clave + nombre, ADR-0042)', () => {
    const filas = construirFilasDeRq([
      linea({ articuloClave: 'ACC86024', articuloNombre: 'PVB acústico' }),
      linea({ id: 'l-2', articuloClave: null, articuloNombre: null }),
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

describe('clasificarEntrega', () => {
  it('sin-entregar cuando no se ha entregado nada', () => {
    expect(clasificarEntrega(0, 10)).toBe('sin-entregar');
    expect(clasificarEntrega(-1, 10)).toBe('sin-entregar');
  });

  it('parcial cuando 0 < entregado < solicitada', () => {
    expect(clasificarEntrega(3, 10)).toBe('parcial');
    expect(clasificarEntrega(9.999, 10)).toBe('parcial');
  });

  it('entregada cuando entregado ≥ solicitada', () => {
    expect(clasificarEntrega(10, 10)).toBe('entregada');
    expect(clasificarEntrega(12, 10)).toBe('entregada');
  });
});
