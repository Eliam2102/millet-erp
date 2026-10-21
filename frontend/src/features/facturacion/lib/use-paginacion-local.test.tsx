import { act, renderHook } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { usePaginacionLocal } from './use-paginacion-local';

describe('paginación de registros cargados', () => {
  it('muestra diez, permite la última página y regresa al inicio al filtrar', () => {
    const items = Array.from({ length: 23 }, (_, id) => ({ id }));
    const { result, rerender } = renderHook(
      ({ filtro, filas }) => usePaginacionLocal(filas, filtro),
      {
        initialProps: { filtro: '', filas: items },
      },
    );
    expect(result.current.visibles.map((i) => i.id)).toEqual([0, 1, 2, 3, 4, 5, 6, 7, 8, 9]);
    act(() => result.current.onChange(20));
    expect(result.current.visibles.map((i) => i.id)).toEqual([20, 21, 22]);
    rerender({ filtro: 'cliente', filas: items.slice(0, 2) });
    expect(result.current.offset).toBe(0);
    expect(result.current.visibles).toHaveLength(2);
    rerender({ filtro: 'cliente', filas: [] });
    expect(result.current.visibles).toEqual([]);
    expect(result.current.offset).toBe(0);
  });
});
