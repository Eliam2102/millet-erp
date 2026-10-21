import { act, renderHook } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { usePaginacionLocal } from './use-paginacion-local';

describe('Paginación de registros ya cargados', () => {
  const items = Array.from({ length: 23 }, (_, i) => i);
  it('muestra diez registros y conserva la última página parcial', () => {
    const { result } = renderHook(() => usePaginacionLocal(items, 'todos'));
    expect(result.current.visibles).toEqual(items.slice(0, 10));
    act(() => result.current.onChange(20));
    expect(result.current.visibles).toEqual([20, 21, 22]);
  });
  it('vuelve al inicio al cambiar filtros', () => {
    const { result, rerender } = renderHook(({ filtro }) => usePaginacionLocal(items, filtro), {
      initialProps: { filtro: 'todos' },
    });
    act(() => result.current.onChange(20));
    rerender({ filtro: 'pendientes' });
    expect(result.current.offset).toBe(0);
  });
  it('ajusta la página cuando una actualización reduce los registros', () => {
    const { result, rerender } = renderHook(({ datos }) => usePaginacionLocal(datos, 'todos'), {
      initialProps: { datos: items },
    });
    act(() => result.current.onChange(20));
    rerender({ datos: [0, 1] });
    expect(result.current.visibles).toEqual([0, 1]);
    expect(result.current.offset).toBe(0);
    rerender({ datos: [] });
    expect(result.current.visibles).toEqual([]);
    expect(result.current.offset).toBe(0);
  });
});
