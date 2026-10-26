import { expect, it } from 'vitest';
import { agruparDepreciacion } from './agrupacion';
import type { Depreciacion } from './libro';
it('agrupa por identidad contable, suma centavos y conserva nombres', () => {
  const filas = [
    {
      claseId: 'maq',
      clase: 'MAQ',
      sucursalId: 'cnk',
      sucursal: 'Conkal',
      centroCostoId: 'uno',
      centroCosto: 'Producción',
      importe: 4166.67,
    },
    {
      claseId: 'maq',
      clase: 'MAQ',
      sucursalId: 'cun',
      sucursal: 'Cancún',
      centroCostoId: 'dos',
      centroCosto: 'Producción',
      importe: 500,
    },
  ] as Depreciacion[];
  expect(agruparDepreciacion(filas, 'clase')).toEqual([
    { grupo: 'MAQ', importe: 4666.67, activos: 2 },
  ]);
  expect(agruparDepreciacion(filas, 'sucursal')).toHaveLength(2);
  expect(agruparDepreciacion(filas, 'centroCosto')).toHaveLength(2);
});
