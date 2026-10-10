import { expect, it } from 'vitest';
import { pendientesComprobados } from './pendientes';
it('ausencia de datos y datos configurados no inventan pendientes ni validación de Millet', () => {
  expect(pendientesComprobados({})).toEqual([]);
  expect(
    pendientesComprobados({
      equivalencias: [{ observaciones: 'Validado' }],
      series: { total: 3, items: [] },
      parametros: {
        items: [
          {
            clave: 'system.dias-festivos',
            valor: '[]',
            id: 'test',
            tipo: 3,
            modulo: null,
            descripcion: '',
            version: 1,
          },
        ],
      },
    }),
  ).toEqual([]);
});
it('respuestas reales permiten verificar DEMO, cero series propias y calendario ausente', () => {
  expect(
    pendientesComprobados({
      equivalencias: [{ observaciones: 'DEMO · pendiente' }],
      series: { total: 0, items: [] },
      parametros: { items: [] },
    }).map((item) => [item.entry.href, item.estado]),
  ).toEqual([
    ['/centros-costo/configuracion', 'Datos de ejemplo'],
    ['/admin/series', 'Por validar'],
    ['/admin/parametros', 'Por validar'],
  ]);
});
