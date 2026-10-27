import { describe, expect, it } from 'vitest';
import type { RecepcionLineaItem } from '@/features/almacen/api/types';
import { cantidadM2, medidaDe } from './medida-hoja';

const linea = {
  id: 'l1',
  cantidad: 178.608,
  productoAwVarianteId: 'v1',
  anchoMm: 2440,
  altoMm: 3660,
} as RecepcionLineaItem;

describe('devolución a proveedor de vidrio por medida', () => {
  it('muestra medida y hojas recibidas', () => {
    expect(medidaDe(linea)?.texto).toBe('2440 × 3660 mm · 20 hojas recibidas');
  });
  it('envía hojas × m² por hoja', () => {
    expect(cantidadM2({ cantidad: 2, lineaRecepcionOrigenId: 'l1' }, [linea])).toBe(17.8608);
  });
  it('sin medida no convierte', () => {
    const sin = { ...linea, productoAwVarianteId: null } as RecepcionLineaItem;
    expect(medidaDe(sin)).toBeNull();
    expect(cantidadM2({ cantidad: 3.5, lineaRecepcionOrigenId: 'l1' }, [sin])).toBe(3.5);
  });
});
