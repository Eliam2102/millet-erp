import { describe, expect, it } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { MutationObserver } from '@tanstack/react-query';
import { ApiError } from '@/lib/api/error';
import { queryClient } from '@/lib/query-client';
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

/**
 * Revisión 2026-09 (ADR-0020): la key se renueva al terminar bien la
 * operación que la usó. Evita que una lista/página que sigue montada
 * reciba la respuesta cacheada de la acción anterior.
 */
async function ejecutarMutacion(variables: unknown, resultado: 'ok' | Error = 'ok') {
  const observer = new MutationObserver<string, Error, unknown>(queryClient, {
    mutationFn: async () => {
      if (resultado !== 'ok') throw resultado;
      return 'hecho';
    },
    retry: false,
  });
  await act(async () => {
    await observer.mutate(variables).catch(() => undefined);
  });
}

function falloPrevio(): ApiError {
  return new ApiError(
    {
      type: 'about:blank',
      title: 'La operación anterior falló',
      status: 409,
      code: 'IDEMPOTENCY_PREVIOUS_FAILURE',
    },
    409,
  );
}

describe('rotación de key tras éxito', () => {
  it('useFormIdempotencyKey: rota cuando la mutación que la usó termina bien', async () => {
    const { result } = renderHook(() => useFormIdempotencyKey());
    const k1 = result.current;
    await ejecutarMutacion({ id: 'puesto-a', idempotencyKey: k1 });
    expect(result.current).not.toBe(k1);
  });

  it('useFormIdempotencyKey: conserva la key tras un error normal (reintento seguro)', async () => {
    const { result } = renderHook(() => useFormIdempotencyKey());
    const k1 = result.current;
    await ejecutarMutacion({ id: 'puesto-a', idempotencyKey: k1 }, new Error('red caída'));
    expect(result.current).toBe(k1);
  });

  it('useFormIdempotencyKey: rota tras IDEMPOTENCY_PREVIOUS_FAILURE', async () => {
    const { result } = renderHook(() => useFormIdempotencyKey());
    const k1 = result.current;
    await ejecutarMutacion({ command: { idempotencyKey: k1 } }, falloPrevio());
    expect(result.current).not.toBe(k1);
  });

  it('useFormIdempotencyKey: no rota por mutaciones con otra key', async () => {
    const { result } = renderHook(() => useFormIdempotencyKey());
    const k1 = result.current;
    await ejecutarMutacion({ idempotencyKey: crypto.randomUUID() });
    expect(result.current).toBe(k1);
  });

  it('useBodyScopedIdempotencyKey: mismo body tras un éxito ⇒ key nueva', async () => {
    const { result } = renderHook(() => useBodyScopedIdempotencyKey());
    const body = { monto: 100, cuenta: 'A' };
    const k1 = result.current(body);
    await ejecutarMutacion({ command: body, idempotencyKey: k1 });
    expect(result.current(body)).not.toBe(k1);
  });
});
