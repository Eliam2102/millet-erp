import { act, renderHook } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { usePaginacionLocal } from './use-paginacion-local';

describe('Paginación del lote recibido', () => {
  const items = Array.from({ length: 23 }, (_, i) => i + 1);
  it('recorre el lote sin perder registros y limita la última página', () => {
    const { result } = renderHook(() => usePaginacionLocal(items, 'cliente-a'));
    const primera = result.current.visibles;
    act(() => result.current.cambiar(10));
    const segunda = result.current.visibles;
    act(() => result.current.cambiar(20));
    expect([...primera, ...segunda, ...result.current.visibles]).toEqual(items);
    expect(result.current.visibles).toHaveLength(3);
  });
  it('vuelve al inicio al cambiar de cliente o filtros', () => {
    const { result, rerender } = renderHook(({ clave }) => usePaginacionLocal(items, clave), {
      initialProps: { clave: 'cliente-a' },
    });
    act(() => result.current.cambiar(20));
    rerender({ clave: 'cliente-b' });
    expect(result.current.offset).toBe(0);
    expect(result.current.visibles).toEqual(items.slice(0, 10));
  });
  it('mantiene una página válida si el lote se reduce al refrescar', () => {
    const { result, rerender } = renderHook(({ filas }) => usePaginacionLocal(filas, 'cliente-a'), {
      initialProps: { filas: items },
    });
    act(() => result.current.cambiar(20));
    rerender({ filas: [1, 2] });
    expect(result.current.offset).toBe(0);
    expect(result.current.visibles).toEqual([1, 2]);
    rerender({ filas: [] });
    expect(result.current.offset).toBe(0);
    expect(result.current.visibles).toEqual([]);
  });
});
