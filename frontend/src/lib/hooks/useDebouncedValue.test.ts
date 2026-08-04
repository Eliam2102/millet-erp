import { describe, expect, it, vi } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';

describe('useDebouncedValue', () => {
  it('devuelve el valor inicial inmediatamente', () => {
    const { result } = renderHook(() => useDebouncedValue('inicial', 300));
    expect(result.current).toBe('inicial');
  });

  it('debounce: actualiza solo tras delayMs', () => {
    vi.useFakeTimers();
    const { result, rerender } = renderHook(
      ({ value }) => useDebouncedValue(value, 300),
      { initialProps: { value: 'a' } },
    );

    rerender({ value: 'b' });
    expect(result.current).toBe('a'); // sigue con el valor viejo
    act(() => vi.advanceTimersByTime(150));
    expect(result.current).toBe('a');
    act(() => vi.advanceTimersByTime(150));
    expect(result.current).toBe('b');

    vi.useRealTimers();
  });

  it('cambios consecutivos resetean el timer (solo el último vale)', () => {
    vi.useFakeTimers();
    const { result, rerender } = renderHook(
      ({ value }) => useDebouncedValue(value, 300),
      { initialProps: { value: 'a' } },
    );

    rerender({ value: 'b' });
    act(() => vi.advanceTimersByTime(200));
    rerender({ value: 'c' });
    act(() => vi.advanceTimersByTime(200));
    expect(result.current).toBe('a'); // 'b' nunca se aplicó (timer reset)
    act(() => vi.advanceTimersByTime(150));
    expect(result.current).toBe('c');

    vi.useRealTimers();
  });
});
