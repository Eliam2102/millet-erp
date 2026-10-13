import { describe, expect, it, vi } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper, createTestQueryClient } from '@/test/test-query-client';
import { F1A_QUERY_KEY } from './f1a';
import {
  useActualizarCliente,
  useIniciarSincronizacion,
  useReintentarCliente,
} from './clientes';
import type { ActualizarClientePayload, EstadoEjecucionSync } from './types';
import { useInvalidarF1AAlTerminar } from './use-invalidar-f1a-al-terminar';

// Invalidación del panel F1A (ligados) tras las mutaciones que cambian
// clientes o la sincronización. Se espía invalidateQueries del mismo
// QueryClient que usa el wrapper.

describe('Invalidación de la clave F1A tras mutaciones de clientes', () => {
  it('useActualizarCliente invalida F1A tras éxito', async () => {
    mswServer.use(
      http.patch('*/api/v1/datos-maestros/clientes/c1', () => new HttpResponse(null, { status: 204 })),
    );
    const qc = createTestQueryClient();
    const spy = vi.spyOn(qc, 'invalidateQueries');
    const { result } = renderHook(() => useActualizarCliente(), { wrapper: createQueryWrapper(qc) });

    await act(async () => {
      await result.current.mutateAsync({
        id: 'c1',
        payload: {} as ActualizarClientePayload,
        idempotencyKey: 'k-1',
      });
    });

    expect(spy).toHaveBeenCalledWith({ queryKey: F1A_QUERY_KEY });
  });

  it('useIniciarSincronizacion invalida F1A tras éxito', async () => {
    mswServer.use(
      http.post('*/api/v1/datos-maestros/clientes/sincronizacion/ejecuciones', () =>
        HttpResponse.json({ id: 'e1', estado: 'Pendiente' }, { status: 202 }),
      ),
    );
    const qc = createTestQueryClient();
    const spy = vi.spyOn(qc, 'invalidateQueries');
    const { result } = renderHook(() => useIniciarSincronizacion(), { wrapper: createQueryWrapper(qc) });

    await act(async () => {
      await result.current.mutateAsync();
    });

    expect(spy).toHaveBeenCalledWith({ queryKey: F1A_QUERY_KEY });
  });

  it('useReintentarCliente invalida F1A tras éxito', async () => {
    mswServer.use(
      http.post('*/api/v1/datos-maestros/clientes/sincronizacion/reintentos', () =>
        HttpResponse.json({
          ejecucion: { id: 'e1', estado: 'Completa' },
          errores: [],
          erroresTruncados: false,
        }),
      ),
    );
    const qc = createTestQueryClient();
    const spy = vi.spyOn(qc, 'invalidateQueries');
    const { result } = renderHook(() => useReintentarCliente(), { wrapper: createQueryWrapper(qc) });

    await act(async () => {
      await result.current.mutateAsync({ referencia: 'REF-1' });
    });

    expect(spy).toHaveBeenCalledWith({ queryKey: F1A_QUERY_KEY });
  });
});

describe('useInvalidarF1AAlTerminar (sheet de sincronización)', () => {
  type Ejecucion = { id: string; estado: EstadoEjecucionSync };

  it('invalida F1A una sola vez cuando la ejecución viva pasa a terminal', () => {
    const qc = createTestQueryClient();
    const spy = vi.spyOn(qc, 'invalidateQueries');
    const { rerender } = renderHook(
      ({ items }: { items: Ejecucion[] }) => useInvalidarF1AAlTerminar(items),
      {
        wrapper: createQueryWrapper(qc),
        initialProps: { items: [{ id: 'e1', estado: 'EnCurso' }] },
      },
    );
    expect(spy).not.toHaveBeenCalled();

    rerender({ items: [{ id: 'e1', estado: 'Completa' }] });
    expect(spy).toHaveBeenCalledTimes(1);
    expect(spy).toHaveBeenCalledWith({ queryKey: F1A_QUERY_KEY });

    // Nueva referencia del arreglo con el mismo estado terminal: no repite.
    rerender({ items: [{ id: 'e1', estado: 'Completa' }] });
    expect(spy).toHaveBeenCalledTimes(1);
  });

  it('no invalida si la ejecución ya llega terminal sin haberla visto viva', () => {
    const qc = createTestQueryClient();
    const spy = vi.spyOn(qc, 'invalidateQueries');
    renderHook(() => useInvalidarF1AAlTerminar([{ id: 'e2', estado: 'Fallida' }]), {
      wrapper: createQueryWrapper(qc),
    });
    expect(spy).not.toHaveBeenCalled();
  });
});
