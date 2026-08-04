import { describe, expect, it } from 'vitest';
import { renderHook } from '@testing-library/react';
import { useCollaboration } from '@/components/erp/collaboration/useCollaboration';

describe('useCollaboration (stub)', () => {
  it('devuelve presencia vacía con id válido', () => {
    const { result } = renderHook(() =>
      useCollaboration('requisicion', 'rq-1'),
    );
    expect(result.current).toEqual({ viendo: [], editando: [] });
  });

  it('devuelve presencia vacía con id null/undefined (P4 nueva)', () => {
    const { result: resA } = renderHook(() =>
      useCollaboration('requisicion', null),
    );
    expect(resA.current.viendo).toEqual([]);
    expect(resA.current.editando).toEqual([]);

    const { result: resB } = renderHook(() =>
      useCollaboration('requisicion', undefined),
    );
    expect(resB.current.viendo).toEqual([]);
    expect(resB.current.editando).toEqual([]);
  });

  it('retorna la misma referencia (objeto frozen) entre llamadas — evita re-renders inútiles', () => {
    const { result, rerender } = renderHook(() =>
      useCollaboration('requisicion', 'rq-1'),
    );
    const primera = result.current;
    rerender();
    expect(result.current).toBe(primera);
  });
});
