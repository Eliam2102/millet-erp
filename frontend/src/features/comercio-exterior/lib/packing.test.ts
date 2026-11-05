import { describe, expect, it } from 'vitest';
import { totalesPacking } from './packing';
import type { ContenedoresDetalle } from '../api/types';

describe('Peso y bultos de las partidas', () => {
  const datos = {
    contenedores: [{ id: 'c', pesoKg: 10, bultos: 3 }],
    partidas: [
      { contenedorId: 'c', estado: 1, pesoKg: 6, bultos: 2 },
      { contenedorId: 'c', estado: 1, pesoKg: 4, bultos: 1 },
      { contenedorId: 'c', estado: 3, pesoKg: 50, bultos: 50 },
    ],
  } as unknown as ContenedoresDetalle;
  it('suma solo las partidas vigentes y compara contra lo declarado', () => {
    expect(totalesPacking(datos, 'c')).toEqual({ pesoKg: 10, bultos: 3, cuadra: true });
    expect(
      totalesPacking({ ...datos, contenedores: [{ ...datos.contenedores[0], pesoKg: 11 }] }, 'c')
        .cuadra,
    ).toBe(false);
  });
});
