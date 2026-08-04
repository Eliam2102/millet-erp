import { describe, expect, it } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import {
  useConfirmarPropuesta,
  useCrearPropuestaAplicacion,
  useFacturasAbiertas,
  usePropuestasAplicacion,
  useRechazarPropuesta,
  useToleranciasNoFiscal,
} from '@/features/cxc/api/useAplicaciones';
import { EstadoPropuestaAplicacion } from '@/features/cxc/api/types';

const BASE = '*/api/v1/cuentas-por-cobrar/propuestas-aplicacion';
const FACTURAS = '*/api/v1/cuentas-por-cobrar/cartera/facturas-abiertas';

const propuesta = {
  id: 'pap-1',
  clienteId: 'cli-1',
  depositoRef: 'SPEI #123',
  montoDeposito: 25000,
  moneda: 'MXN',
  remittanceRef: 'REM-1',
  ajusteNoFiscal: -120,
  estado: EstadoPropuestaAplicacion.Propuesta,
  motivoRechazo: null,
  resueltaPor: null,
  resueltaEn: null,
  facturas: [
    {
      facturaCarteraId: 'fc-1',
      facturaUuid: 'uuid-1',
      folio: 'F-100',
      importeAplicado: 25120,
      numParcialidad: null,
    },
  ],
  version: 1,
};

describe('usePropuestasAplicacion', () => {
  it('bandeja con filtro por estado', async () => {
    let url: URL | null = null;
    mswServer.use(
      http.get(BASE, ({ request }) => {
        url = new URL(request.url);
        return HttpResponse.json({
          items: [propuesta],
          offset: 0,
          limit: 200,
          total: 1,
        });
      }),
    );
    const { result } = renderHook(
      () =>
        usePropuestasAplicacion({
          estado: EstadoPropuestaAplicacion.Propuesta,
        }),
      { wrapper: createQueryWrapper() },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(url!.searchParams.get('estado')).toBe('1');
    expect(result.current.data?.items[0].ajusteNoFiscal).toBe(-120);
  });
});

describe('useFacturasAbiertas', () => {
  it('lista por cliente y moneda; sin cliente → idle', async () => {
    let url: URL | null = null;
    mswServer.use(
      http.get(FACTURAS, ({ request }) => {
        url = new URL(request.url);
        return HttpResponse.json([
          {
            facturaCarteraId: 'fc-1',
            uuid: 'uuid-1',
            folio: 'F-100',
            moneda: 'MXN',
            metodoPago: 'PPD',
            total: 30000,
            montoPagado: 0,
            montoNc: 0,
            saldo: 30000,
            fechaTimbrado: '2026-06-14T10:00:00Z',
            fechaVencimiento: '2026-07-14T10:00:00Z',
          },
        ]);
      }),
    );
    const { result } = renderHook(() => useFacturasAbiertas('cli-1', 'MXN'), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(url!.searchParams.get('clienteId')).toBe('cli-1');
    expect(url!.searchParams.get('moneda')).toBe('MXN');
    expect(result.current.data?.[0].saldo).toBe(30000);

    const { result: sinCliente } = renderHook(
      () => useFacturasAbiertas(null, 'MXN'),
      { wrapper: createQueryWrapper() },
    );
    expect(sinCliente.current.fetchStatus).toBe('idle');
  });
});

describe('useToleranciasNoFiscal', () => {
  it('lee la config por moneda', async () => {
    mswServer.use(
      http.get(`${BASE}/tolerancias`, () =>
        HttpResponse.json({ MXN: 1000, USD: 50 }),
      ),
    );
    const { result } = renderHook(() => useToleranciasNoFiscal(), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.MXN).toBe(1000);
  });
});

describe('useCrearPropuestaAplicacion', () => {
  it('POST con Idempotency-Key y líneas del matching', async () => {
    let idem: string | null = null;
    let body: Record<string, unknown> | null = null;
    mswServer.use(
      http.post(BASE, async ({ request }) => {
        idem = request.headers.get('Idempotency-Key');
        body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(propuesta, { status: 201 });
      }),
    );
    const { result } = renderHook(() => useCrearPropuestaAplicacion(), {
      wrapper: createQueryWrapper(),
    });
    result.current.mutate({
      command: {
        clienteId: 'cli-1',
        depositoRef: 'SPEI #123',
        montoDeposito: 25000,
        moneda: 'MXN',
        remittanceRef: 'REM-1',
        facturas: [
          { facturaUuid: 'uuid-1', importeAplicado: 25120, numParcialidad: null },
        ],
      },
      idempotencyKey: 'idem-p1',
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(idem).toBe('idem-p1');
    expect((body!.facturas as unknown[]).length).toBe(1);
  });
});

describe('confirmar / rechazar (Ingresos)', () => {
  it('confirmar manda X-Expected-Version', async () => {
    let version: string | null = null;
    mswServer.use(
      http.post(`${BASE}/pap-1/confirmar`, ({ request }) => {
        version = request.headers.get('X-Expected-Version');
        return HttpResponse.json({
          ...propuesta,
          estado: EstadoPropuestaAplicacion.Confirmada,
          version: 2,
        });
      }),
    );
    const { result } = renderHook(() => useConfirmarPropuesta(), {
      wrapper: createQueryWrapper(),
    });
    result.current.mutate({
      id: 'pap-1',
      versionEsperada: 1,
      idempotencyKey: 'idem-c1',
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(version).toBe('1');
  });

  it('rechazar manda motivo', async () => {
    let body: Record<string, unknown> | null = null;
    mswServer.use(
      http.post(`${BASE}/pap-1/rechazar`, async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json({
          ...propuesta,
          estado: EstadoPropuestaAplicacion.Rechazada,
          motivoRechazo: 'Cliente equivocado',
          version: 2,
        });
      }),
    );
    const { result } = renderHook(() => useRechazarPropuesta(), {
      wrapper: createQueryWrapper(),
    });
    result.current.mutate({
      id: 'pap-1',
      versionEsperada: 1,
      motivo: 'Cliente equivocado',
      idempotencyKey: 'idem-r1',
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(body).toEqual({ motivo: 'Cliente equivocado' });
  });
});
