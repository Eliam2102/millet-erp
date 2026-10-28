import { describe, expect, it } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import {
  useCambiarCuentaRol,
  useCrearRol,
  useDescartarRechazo,
  useRechazosContables,
  useReprocesarRechazo,
  useRolesCuenta,
} from './motor';

const API = '*/api/v1/contabilidad/motor';

function rol(o: Record<string, unknown> = {}) {
  return {
    id: 'r1', codigo: 'IVA_ACREDITABLE', descripcion: 'FIX IVA acreditable', cuentaId: 'c1', cuentaCodigo: 'FIX-118',
    cuentaNombre: 'FIX IVA', sucursalId: null, moneda: null, vigenteDesde: '2026-01-01', vigenteHasta: null, version: 3, ...o,
  };
}

interface Visto {
  url: string;
  ifMatch: string | null;
  idem: string | null;
  body: unknown;
}

async function capturar(request: Request): Promise<Visto> {
  const texto = await request.text();
  return {
    url: request.url,
    ifMatch: request.headers.get('If-Match'),
    idem: request.headers.get('Idempotency-Key'),
    body: texto ? JSON.parse(texto) : null,
  };
}

describe('hooks del motor contable', () => {
  it('useRolesCuenta lee GET /roles', async () => {
    mswServer.use(http.get(`${API}/roles`, () => HttpResponse.json([rol()])));
    const { result } = renderHook(() => useRolesCuenta(), { wrapper: createQueryWrapper() });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.[0].codigo).toBe('IVA_ACREDITABLE');
  });

  it('useRechazosContables manda el estado como query param y sin él no filtra', async () => {
    const urls: string[] = [];
    mswServer.use(http.get(`${API}/rechazos`, ({ request }) => {
      urls.push(request.url);
      return HttpResponse.json([]);
    }));
    const pend = renderHook(() => useRechazosContables('Pendiente'), { wrapper: createQueryWrapper() });
    await waitFor(() => expect(pend.result.current.isSuccess).toBe(true));
    const todos = renderHook(() => useRechazosContables(), { wrapper: createQueryWrapper() });
    await waitFor(() => expect(todos.result.current.isSuccess).toBe(true));
    expect(urls[0]).toContain('/rechazos?estado=Pendiente');
    expect(urls[1]).not.toContain('?');
  });

  it('useCrearRol hace POST /roles con Idempotency-Key', async () => {
    const vistos: Visto[] = [];
    mswServer.use(http.post(`${API}/roles`, async ({ request }) => {
      vistos.push(await capturar(request));
      return HttpResponse.json(rol(), { status: 201 });
    }));
    const { result } = renderHook(() => useCrearRol(), { wrapper: createQueryWrapper() });
    await act(() => result.current.mutateAsync({ body: { codigo: 'IVA_ACREDITABLE', descripcion: 'FIX IVA' }, idempotencyKey: 'k-crear' }));
    expect(vistos[0].idem).toBe('k-crear');
    expect(vistos[0].body).toEqual({ codigo: 'IVA_ACREDITABLE', descripcion: 'FIX IVA' });
  });

  it('useCambiarCuentaRol hace PUT /roles/{id}/cuenta con If-Match de la versión e Idempotency-Key', async () => {
    const vistos: Visto[] = [];
    mswServer.use(http.put(`${API}/roles/:id/cuenta`, async ({ request }) => {
      vistos.push(await capturar(request));
      return HttpResponse.json(rol({ cuentaId: 'c2', version: 4 }));
    }));
    const { result } = renderHook(() => useCambiarCuentaRol(), { wrapper: createQueryWrapper() });
    await act(() => result.current.mutateAsync({
      rol: { id: 'r1', version: 3 }, body: { cuentaId: 'c2', motivo: 'FIX reclasificación' }, idempotencyKey: 'k-cuenta',
    }));
    expect(vistos[0].url).toContain('/motor/roles/r1/cuenta');
    expect(vistos[0].ifMatch).toBe('"3"');
    expect(vistos[0].idem).toBe('k-cuenta');
    expect(vistos[0].body).toEqual({ cuentaId: 'c2', motivo: 'FIX reclasificación' });
  });

  it('useReprocesarRechazo hace POST /rechazos/{id}/reprocesar con Idempotency-Key', async () => {
    const vistos: Visto[] = [];
    mswServer.use(http.post(`${API}/rechazos/:id/reprocesar`, async ({ request }) => {
      vistos.push(await capturar(request));
      return HttpResponse.json({
        poliza: { id: 'p9', folio: 'CP-000001', estado: 'Autorizada', idempotente: false },
        rechazoId: null, causa: null, detalle: null, mensaje: null, generada: true,
      });
    }));
    const { result } = renderHook(() => useReprocesarRechazo(), { wrapper: createQueryWrapper() });
    await act(() => result.current.mutateAsync({ id: 'x1', idempotencyKey: 'k-rep' }));
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(vistos[0].url).toContain('/motor/rechazos/x1/reprocesar');
    expect(vistos[0].idem).toBe('k-rep');
    expect(result.current.data?.poliza?.folio).toBe('CP-000001');
  });

  it('useDescartarRechazo manda el motivo en el body con Idempotency-Key', async () => {
    const vistos: Visto[] = [];
    mswServer.use(http.post(`${API}/rechazos/:id/descartar`, async ({ request }) => {
      vistos.push(await capturar(request));
      return HttpResponse.json({});
    }));
    const { result } = renderHook(() => useDescartarRechazo(), { wrapper: createQueryWrapper() });
    await act(() => result.current.mutateAsync({ id: 'x1', motivo: 'FIX evento duplicado', idempotencyKey: 'k-desc' }));
    expect(vistos[0].url).toContain('/motor/rechazos/x1/descartar');
    expect(vistos[0].idem).toBe('k-desc');
    expect(vistos[0].body).toEqual({ motivo: 'FIX evento duplicado' });
  });
});
