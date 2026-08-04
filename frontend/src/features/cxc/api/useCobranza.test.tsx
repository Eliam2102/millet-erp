import { describe, expect, it } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import {
  useRegistrarSeguimientoCobranza,
  useSeguimientosCobranza,
} from '@/features/cxc/api/useCobranza';
import { CanalCobranza, ResultadoCobranza } from '@/features/cxc/api/types';

const BASE = '*/api/v1/cuentas-por-cobrar/cobranza';

const gestion = {
  id: 'sc-1',
  clienteId: 'cli-1',
  fecha: '2026-07-14T10:00:00Z',
  usuarioId: 'u-1',
  canal: CanalCobranza.Llamada,
  resultado: ResultadoCobranza.PromesaPago,
  montoComprometido: 25000,
  fechaComprometida: '2026-07-21',
  nota: 'Cliente promete pagar la próxima semana.',
};

describe('useSeguimientosCobranza', () => {
  it('lista por cliente (query param obligatorio)', async () => {
    let url: URL | null = null;
    mswServer.use(
      http.get(BASE, ({ request }) => {
        url = new URL(request.url);
        return HttpResponse.json({
          items: [gestion],
          offset: 0,
          limit: 100,
          total: 1,
        });
      }),
    );
    const { result } = renderHook(
      () => useSeguimientosCobranza({ clienteId: 'cli-1' }),
      { wrapper: createQueryWrapper() },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(url!.searchParams.get('clienteId')).toBe('cli-1');
    expect(result.current.data?.items[0].nota).toContain('promete');
  });

  it('sin cliente → query deshabilitada (filtro obligatorio §2)', () => {
    const { result } = renderHook(
      () => useSeguimientosCobranza({ clienteId: null }),
      { wrapper: createQueryWrapper() },
    );
    expect(result.current.fetchStatus).toBe('idle');
  });
});

describe('useRegistrarSeguimientoCobranza', () => {
  it('POST con Idempotency-Key y compromiso de promesa', async () => {
    let idem: string | null = null;
    let body: Record<string, unknown> | null = null;
    mswServer.use(
      http.post(BASE, async ({ request }) => {
        idem = request.headers.get('Idempotency-Key');
        body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(gestion, { status: 201 });
      }),
    );
    const { result } = renderHook(() => useRegistrarSeguimientoCobranza(), {
      wrapper: createQueryWrapper(),
    });
    result.current.mutate({
      command: {
        clienteId: 'cli-1',
        canal: CanalCobranza.Llamada,
        resultado: ResultadoCobranza.PromesaPago,
        montoComprometido: 25000,
        fechaComprometida: '2026-07-21',
        nota: 'Cliente promete pagar la próxima semana.',
      },
      idempotencyKey: 'idem-sc1',
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(idem).toBe('idem-sc1');
    expect(body!.montoComprometido).toBe(25000);
    expect(body!.fechaComprometida).toBe('2026-07-21');
  });

  it('422 del backend se propaga como ApiError', async () => {
    mswServer.use(
      http.post(BASE, () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Regla de negocio',
            status: 422,
            code: 'SC_PROMESA_SIN_MONTO',
          },
          { status: 422, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    const { result } = renderHook(() => useRegistrarSeguimientoCobranza(), {
      wrapper: createQueryWrapper(),
    });
    result.current.mutate({
      command: {
        clienteId: 'cli-1',
        canal: CanalCobranza.Llamada,
        resultado: ResultadoCobranza.PromesaPago,
        montoComprometido: null,
        fechaComprometida: null,
        nota: 'x',
      },
      idempotencyKey: 'idem-sc2',
    });
    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});
