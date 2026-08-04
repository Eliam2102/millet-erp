import { describe, expect, it } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useMotivosRechazo } from '@/features/compras/api/useMotivosRechazo';
import {
  MotivoRechazoAplicaA,
  aplicaABitmaskIncluye,
} from '@/features/compras/api/types';

describe('useMotivosRechazo', () => {
  it('200 OK: devuelve lista de motivos', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/motivos-rechazo', () =>
        HttpResponse.json([
          {
            id: 'm-1',
            clave: 'OTRO',
            descripcion: 'Otro (especificar en texto)',
            permiteTextoLibre: true,
            aplicaA: MotivoRechazoAplicaA.Todos,
          },
          {
            id: 'm-2',
            clave: 'PRESUPUESTO_NO_DISPONIBLE',
            descripcion: 'Presupuesto no disponible',
            permiteTextoLibre: false,
            aplicaA: MotivoRechazoAplicaA.Rechazo,
          },
        ]),
      ),
    );

    const { result } = renderHook(() => useMotivosRechazo(), {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toHaveLength(2);
    expect(result.current.data?.[0].clave).toBe('OTRO');
    expect(result.current.data?.[0].permiteTextoLibre).toBe(true);
  });

  it('aplicaABitmaskIncluye filtra correctamente por flag', () => {
    expect(
      aplicaABitmaskIncluye(MotivoRechazoAplicaA.Todos, MotivoRechazoAplicaA.Rechazo),
    ).toBe(true);
    expect(
      aplicaABitmaskIncluye(
        MotivoRechazoAplicaA.Todos,
        MotivoRechazoAplicaA.Cancelacion,
      ),
    ).toBe(true);
    expect(
      aplicaABitmaskIncluye(MotivoRechazoAplicaA.Rechazo, MotivoRechazoAplicaA.Eliminacion),
    ).toBe(false);
    // Bitmask con flag=Ninguno siempre es false (no se debe usar
    // aplicaABitmaskIncluye(_, Ninguno) — el helper lo previene).
    expect(
      aplicaABitmaskIncluye(MotivoRechazoAplicaA.Todos, MotivoRechazoAplicaA.Ninguno),
    ).toBe(false);
  });

  it('500 server error: lanza ApiError', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/motivos-rechazo', () =>
        HttpResponse.json(
          { type: 'about:blank', title: 'Error', status: 500 },
          {
            status: 500,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );

    const { result } = renderHook(() => useMotivosRechazo(), {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});
