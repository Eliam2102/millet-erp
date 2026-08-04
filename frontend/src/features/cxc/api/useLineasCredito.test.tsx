import { describe, expect, it } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import {
  useBloquearLineaCredito,
  useClientesLookupCxc,
  useCreditoDisponible,
  useCrearLineaCredito,
  useLineaCredito,
  useLineasCredito,
} from '@/features/cxc/api/useLineasCredito';
import {
  EstadoLineaCredito,
  OrigenLineaCredito,
} from '@/features/cxc/api/types';

const BASE = '*/api/v1/cuentas-por-cobrar/lineas-credito';

const linea = {
  id: 'lc-1',
  clienteId: 'cli-1',
  moneda: 'MXN',
  limite: 500000,
  origen: OrigenLineaCredito.Solunion,
  plazoDias: 30,
  clasificacion: 'A',
  estado: EstadoLineaCredito.Activa,
  motivoBloqueo: null,
  version: 1,
};

describe('useLineasCredito', () => {
  it('bandeja paginada con filtros server-side', async () => {
    let url: URL | null = null;
    mswServer.use(
      http.get(BASE, ({ request }) => {
        url = new URL(request.url);
        return HttpResponse.json({
          items: [linea],
          offset: 0,
          limit: 50,
          total: 1,
        });
      }),
    );
    const { result } = renderHook(
      () => useLineasCredito({ estado: EstadoLineaCredito.Activa, moneda: 'MXN' }),
      { wrapper: createQueryWrapper() },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.items[0].clienteId).toBe('cli-1');
    expect(url!.searchParams.get('estado')).toBe('1');
    expect(url!.searchParams.get('moneda')).toBe('MXN');
  });

  it('propaga error 5xx', async () => {
    mswServer.use(
      http.get(BASE, () => HttpResponse.json({}, { status: 500 })),
    );
    const { result } = renderHook(() => useLineasCredito(), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});

describe('useLineaCredito', () => {
  it('detalle por id', async () => {
    mswServer.use(
      http.get(`${BASE}/lc-1`, () => HttpResponse.json(linea)),
    );
    const { result } = renderHook(() => useLineaCredito('lc-1'), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.limite).toBe(500000);
    expect(result.current.data?.version).toBe(1);
  });

  it('id null → query deshabilitada (no fetch)', () => {
    const { result } = renderHook(() => useLineaCredito(null), {
      wrapper: createQueryWrapper(),
    });
    expect(result.current.fetchStatus).toBe('idle');
  });
});

describe('useCreditoDisponible', () => {
  it('evaluación por línea con bandera datoIncompleto', async () => {
    mswServer.use(
      http.get('*/api/v1/cuentas-por-cobrar/credito-disponible/cli-1', () =>
        HttpResponse.json({
          clienteId: 'cli-1',
          lineas: [
            {
              lineaCreditoId: 'lc-1',
              moneda: 'MXN',
              limite: 500000,
              facturado: 120000,
              liberadoSinFactura: 0,
              disponible: 380000,
              estado: EstadoLineaCredito.Activa,
              origen: OrigenLineaCredito.Solunion,
              plazoDias: 30,
            },
          ],
          datoIncompleto: true,
        }),
      ),
    );
    const { result } = renderHook(() => useCreditoDisponible('cli-1'), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.datoIncompleto).toBe(true);
    expect(result.current.data?.lineas[0].disponible).toBe(380000);
  });
});

describe('useClientesLookupCxc', () => {
  it('modo ids manda csv y devuelve items', async () => {
    let url: URL | null = null;
    mswServer.use(
      http.get('*/api/v1/cuentas-por-cobrar/clientes-lookup', ({ request }) => {
        url = new URL(request.url);
        return HttpResponse.json([
          { id: 'cli-1', clave: 'C001', rfc: 'AAA010101AAA', razonSocial: 'ACME SA' },
        ]);
      }),
    );
    const { result } = renderHook(
      () => useClientesLookupCxc({ ids: ['cli-1', 'cli-2'] }),
      { wrapper: createQueryWrapper() },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(url!.searchParams.get('ids')).toBe('cli-1,cli-2');
    expect(result.current.data?.[0].razonSocial).toBe('ACME SA');
  });
});

describe('useCrearLineaCredito', () => {
  it('POST con Idempotency-Key', async () => {
    let idem: string | null = null;
    mswServer.use(
      http.post(BASE, ({ request }) => {
        idem = request.headers.get('Idempotency-Key');
        return HttpResponse.json(linea, { status: 201 });
      }),
    );
    const { result } = renderHook(() => useCrearLineaCredito(), {
      wrapper: createQueryWrapper(),
    });
    result.current.mutate({
      command: {
        clienteId: 'cli-1',
        moneda: 'MXN',
        limite: 500000,
        origen: OrigenLineaCredito.Solunion,
        plazoDias: 30,
        clasificacion: 'A',
      },
      idempotencyKey: 'idem-lc1',
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(idem).toBe('idem-lc1');
    expect(result.current.data?.id).toBe('lc-1');
  });
});

describe('useBloquearLineaCredito', () => {
  it('POST bloquear con X-Expected-Version y motivo', async () => {
    let version: string | null = null;
    let body: unknown = null;
    mswServer.use(
      http.post(`${BASE}/lc-1/bloquear`, async ({ request }) => {
        version = request.headers.get('X-Expected-Version');
        body = await request.json();
        return HttpResponse.json({
          ...linea,
          estado: EstadoLineaCredito.Bloqueada,
          motivoBloqueo: 'Cartera vencida',
          version: 2,
        });
      }),
    );
    const { result } = renderHook(() => useBloquearLineaCredito(), {
      wrapper: createQueryWrapper(),
    });
    result.current.mutate({
      id: 'lc-1',
      versionEsperada: 1,
      motivo: 'Cartera vencida',
      idempotencyKey: 'idem-b1',
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(version).toBe('1');
    expect(body).toEqual({ motivo: 'Cartera vencida' });
    expect(result.current.data?.estado).toBe(EstadoLineaCredito.Bloqueada);
  });

  it('409 de concurrencia se propaga como ApiError', async () => {
    mswServer.use(
      http.post(`${BASE}/lc-1/bloquear`, () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Conflicto de concurrencia',
            status: 409,
            code: 'CONCURRENCY_CONFLICT',
          },
          { status: 409, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    const { result } = renderHook(() => useBloquearLineaCredito(), {
      wrapper: createQueryWrapper(),
    });
    result.current.mutate({
      id: 'lc-1',
      versionEsperada: 1,
      motivo: 'x',
      idempotencyKey: 'idem-b2',
    });
    await waitFor(() => expect(result.current.isError).toBe(true));
  });
});
