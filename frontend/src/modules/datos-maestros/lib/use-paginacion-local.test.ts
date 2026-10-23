import { act, renderHook } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { usePaginacionLocal } from './use-paginacion-local';

const items = Array.from({ length: 95 }, (_, i) => i);

describe('usePaginacionLocal', () => {
  it('por omisión pagina de 10 en 10', () => {
    const { result } = renderHook(() => usePaginacionLocal(items));
    expect(result.current.limit).toBe(10);
    expect(result.current.visibles).toHaveLength(10);
  });

  it('cambia el tamaño, vuelve al inicio y salta al final', () => {
    const { result } = renderHook(() => usePaginacionLocal(items));
    act(() => result.current.onChange(20));
    act(() => result.current.onChange(0, 50));
    expect(result.current.limit).toBe(50);
    expect(result.current.offset).toBe(0);
    expect(result.current.visibles).toHaveLength(50);
    act(() => result.current.onChange(50, 50));
    expect(result.current.visibles).toHaveLength(45);
  });
});
