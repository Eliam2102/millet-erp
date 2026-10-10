import { expect, it } from 'vitest';
import { contadoresMenu } from './contadores-menu';
import type { IndicadorInicio } from './api/useResumenInicio';
import { calcularPrioridad } from './prioridad';
function indicador(modulo: string, total: number): IndicadorInicio {
  return {
    fila: { id: modulo, modulo, titulo: modulo, descripcion: '', to: '/' },
    prioridad: calcularPrioridad({ total }, new Date('2026-10-10')),
    estado: 'listo',
    refetch: () => {},
    isFetching: false,
  };
}
it('suma los conteos reales de las bandejas y usa nombres completos', () => {
  expect(
    contadoresMenu({
      filas: [indicador('Compras', 2), indicador('Proveedores', 3), indicador('CxC', 4)],
      errores: [],
      cargando: false,
    }),
  ).toEqual({ Compras: 5, Cobranza: 4 });
});
it('no presenta un total parcial cuando carga o falla una bandeja del módulo', () => {
  const filas = [indicador('Compras', 2), indicador('CxC', 4)];
  expect(contadoresMenu({ filas, errores: [], cargando: true })).toEqual({});
  expect(
    contadoresMenu({ filas, errores: [indicador('Proveedores', 0)], cargando: false }),
  ).toEqual({ Cobranza: 4 });
});
