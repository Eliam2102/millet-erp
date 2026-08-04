import { describe, expect, it } from 'vitest';
import { renderHook } from '@testing-library/react';
import {
  idempotencyHeader,
  useBodyScopedIdempotencyKey,
  useFormIdempotencyKey,
} from '@/lib/api/idempotency';

describe('idempotencyHeader', () => {
  it('emite el header con la key recibida', () => {
    expect(idempotencyHeader('abc-123')).toEqual({ 'Idempotency-Key': 'abc-123' });
  });

  it('devuelve {} con key vacía, null o undefined', () => {
    expect(idempotencyHeader('')).toEqual({});
    expect(idempotencyHeader(null)).toEqual({});
    expect(idempotencyHeader(undefined)).toEqual({});
  });
});

describe('useFormIdempotencyKey', () => {
  it('produce un UUID v4 estable entre re-renders del mismo componente', () => {
    const { result, rerender } = renderHook(() => useFormIdempotencyKey());
    const primero = result.current;
    expect(primero).toMatch(
      /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i,
    );
    rerender();
    expect(result.current).toBe(primero);
  });

  it('genera keys distintas en montajes distintos del componente', () => {
    const { result: a } = renderHook(() => useFormIdempotencyKey());
    const { result: b } = renderHook(() => useFormIdempotencyKey());
    expect(a.current).not.toBe(b.current);
  });
});

/**
 * Semántica que previene el pago duplicado (hallazgo Tesorería #1) sin
 * reintroducir el 422 KEY_REUSED_WITH_DIFFERENT_BODY.
 */
describe('useBodyScopedIdempotencyKey', () => {
  it('devuelve la MISMA key mientras el body no cambie (reintento seguro tras timeout)', () => {
    const { result } = renderHook(() => useBodyScopedIdempotencyKey());
    const k1 = result.current({ monto: 100, cuenta: 'A' });
    // Mismo contenido, objeto distinto (reintento manual del usuario).
    const k2 = result.current({ monto: 100, cuenta: 'A' });
    expect(k1).toMatch(
      /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i,
    );
    expect(k2).toBe(k1);
  });

  it('regenera la key cuando cambia el body (corrección tras rechazo → sin KEY_REUSED)', () => {
    const { result } = renderHook(() => useBodyScopedIdempotencyKey());
    const k1 = result.current({ monto: 100, cuenta: 'A' });
    const k2 = result.current({ monto: 100, cuenta: 'B' });
    expect(k2).not.toBe(k1);
  });

  it('vuelve a estabilizarse en el nuevo body tras corregir', () => {
    const { result } = renderHook(() => useBodyScopedIdempotencyKey());
    result.current({ monto: 100, cuenta: 'A' });
    const corregida1 = result.current({ monto: 250, cuenta: 'A' });
    const corregida2 = result.current({ monto: 250, cuenta: 'A' });
    expect(corregida2).toBe(corregida1);
  });

  it('mantiene un holder independiente por instancia del hook', () => {
    const a = renderHook(() => useBodyScopedIdempotencyKey());
    const b = renderHook(() => useBodyScopedIdempotencyKey());
    const ka = a.result.current({ x: 1 });
    const kb = b.result.current({ x: 1 });
    expect(ka).not.toBe(kb);
  });
});
