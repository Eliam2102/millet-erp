import { expect, it } from 'vitest';
import { DEFAULT_PARTIDAS_ABIERTAS_SEARCH, PartidasAbiertasSearchSchema } from './partidas-abiertas-search-schema';
it('sin cantidad conserva la página inicial y deja decidir a la preferencia', () => {
  expect(PartidasAbiertasSearchSchema.parse({})).toEqual({ page: 1 });
  expect(DEFAULT_PARTIDAS_ABIERTAS_SEARCH).toEqual({ page: 1 });
});
it('conserva página y cantidad explícitas, incluyendo cantidades automáticas menores de10', () => {
  expect(PartidasAbiertasSearchSchema.parse({ page: '2', pageSize: '10' })).toMatchObject({ page: 2, pageSize: 10 });
  expect(PartidasAbiertasSearchSchema.parse({ pageSize: 5 }).pageSize).toBe(5);
});
it('mantiene enteros y límites del tamaño de página', () => {
  for (const pageSize of [0, 201, 5.5]) expect(() => PartidasAbiertasSearchSchema.parse({ pageSize })).toThrow();
});
