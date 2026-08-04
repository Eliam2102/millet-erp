import { describe, expect, it } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import {
  useAutorizacionesCredito,
  useCancelarAutorizacionCredito,
  useCrearAutorizacionCredito,
  useDecidirLiberacion,
  useDecisionesLiberacion,
} from '@/features/cxc/api/useLiberaciones';
import {
  EstadoAutorizacionCredito,
  ReglaAplicadaLiberacion,
  ResultadoLiberacion,
} from '@/features/cxc/api/types';

const BASE = '*/api/v1/cuentas-por-cobrar/liberaciones';
const BASE_AUT = '*/api/v1/cuentas-por-cobrar/autorizaciones';

const decision = {
  id: 'dl-1',
  pedidoRef: '3000123456',
  clienteId: 'cli-1',
  moneda: 'MXN',
  montoPedido: 15000,
  creditoDisponibleSnapshot: 38000,
  resultado: ResultadoLiberacion.Liberado,
  reglaAplicada: ReglaAplicadaLiberacion.Credito,
  overrideId: null,
  decididoPor: 'u-1',
  decididoEn: '2026-07-14T10:00:00Z',
};

const autorizacion = {
  id: 'ac-1',
  supervisorUsuarioId: 'u-boss',
  beneficiarioUsuarioId: 'u-1',
  motivo: 'Pago en tránsito',
  clienteOPedidoRef: '3000123456',
  fechaAutorizacion: '2026-07-14T09:00:00Z',
  vigenteHasta: '2026-07-15T09:00:00Z',
  estado: EstadoAutorizacionCredito.Autorizada,
  decisionLiberacionId: null,
  version: 1,
};

describe('useDecisionesLiberacion', () => {
  it('bandeja con filtro por resultado', async () => {
    let url: URL | null = null;
    mswServer.use(
      http.get(BASE, ({ request }) => {
        url = new URL(request.url);
        return HttpResponse.json({
          items: [decision],
          offset: 0,
          limit: 200,
          total: 1,
        });
      }),
    );
    const { result } = renderHook(
      () => useDecisionesLiberacion({ resultado: ResultadoLiberacion.Liberado }),
      { wrapper: createQueryWrapper() },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(url!.searchParams.get('resultado')).toBe('1');
    expect(result.current.data?.items[0].pedidoRef).toBe('3000123456');
  });
});

describe('useDecidirLiberacion', () => {
  it('POST con Idempotency-Key devuelve el resultado del backend', async () => {
    let idem: string | null = null;
    mswServer.use(
      http.post(BASE, ({ request }) => {
        idem = request.headers.get('Idempotency-Key');
        return HttpResponse.json(
          {
            ...decision,
            resultado: ResultadoLiberacion.LiberadoConOverride,
            reglaAplicada: ReglaAplicadaLiberacion.Override,
            overrideId: 'ac-1',
          },
          { status: 201 },
        );
      }),
    );
    const { result } = renderHook(() => useDecidirLiberacion(), {
      wrapper: createQueryWrapper(),
    });
    result.current.mutate({
      command: {
        pedidoRef: '3000123456',
        clienteId: 'cli-1',
        moneda: 'MXN',
        montoPedido: 15000,
        overrideId: 'ac-1',
      },
      idempotencyKey: 'idem-d1',
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(idem).toBe('idem-d1');
    expect(result.current.data?.resultado).toBe(
      ResultadoLiberacion.LiberadoConOverride,
    );
  });
});

describe('useAutorizacionesCredito', () => {
  it('filtra por estado y beneficiario', async () => {
    let url: URL | null = null;
    mswServer.use(
      http.get(BASE_AUT, ({ request }) => {
        url = new URL(request.url);
        return HttpResponse.json({
          items: [autorizacion],
          offset: 0,
          limit: 200,
          total: 1,
        });
      }),
    );
    const { result } = renderHook(
      () =>
        useAutorizacionesCredito({
          estado: EstadoAutorizacionCredito.Autorizada,
          beneficiarioUsuarioId: 'u-1',
        }),
      { wrapper: createQueryWrapper() },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(url!.searchParams.get('estado')).toBe('1');
    expect(url!.searchParams.get('beneficiarioUsuarioId')).toBe('u-1');
  });
});

describe('useCrearAutorizacionCredito', () => {
  it('POST con Idempotency-Key', async () => {
    let idem: string | null = null;
    mswServer.use(
      http.post(BASE_AUT, ({ request }) => {
        idem = request.headers.get('Idempotency-Key');
        return HttpResponse.json(autorizacion, { status: 201 });
      }),
    );
    const { result } = renderHook(() => useCrearAutorizacionCredito(), {
      wrapper: createQueryWrapper(),
    });
    result.current.mutate({
      command: {
        beneficiarioUsuarioId: 'u-1',
        motivo: 'Pago en tránsito',
        clienteOPedidoRef: '3000123456',
        vigenciaHoras: 24,
      },
      idempotencyKey: 'idem-a1',
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(idem).toBe('idem-a1');
  });
});

describe('useCancelarAutorizacionCredito', () => {
  it('POST cancelar con X-Expected-Version', async () => {
    let version: string | null = null;
    mswServer.use(
      http.post(`${BASE_AUT}/ac-1/cancelar`, ({ request }) => {
        version = request.headers.get('X-Expected-Version');
        return HttpResponse.json({
          ...autorizacion,
          estado: EstadoAutorizacionCredito.Cancelada,
          version: 2,
        });
      }),
    );
    const { result } = renderHook(() => useCancelarAutorizacionCredito(), {
      wrapper: createQueryWrapper(),
    });
    result.current.mutate({
      id: 'ac-1',
      versionEsperada: 1,
      idempotencyKey: 'idem-c1',
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(version).toBe('1');
    expect(result.current.data?.estado).toBe(
      EstadoAutorizacionCredito.Cancelada,
    );
  });
});
