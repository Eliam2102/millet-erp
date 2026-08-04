import { describe, expect, it } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import {
  useCaja,
  useCrearCaja,
  useListarCajas,
  useReemplazarSucursalesCaja,
  useUsuarioAlcances,
} from '@/features/facturacion/api/useCajas';

const BASE = '*/api/v1/facturacion/cajas';

const detalle = {
  id: 'c-1',
  nombre: 'Caja Mostrador',
  descripcion: null,
  estatus: 'Activo',
  sucursales: [{ sucursalId: 's-1' }],
  canales: [{ canalVentaId: 1, nombre: 'Mostrador' }],
  usuarios: [],
  version: 3,
};

describe('useListarCajas / useCaja', () => {
  it('bandeja: lista cajas', async () => {
    mswServer.use(
      http.get(BASE, () =>
        HttpResponse.json([
          {
            id: 'c-1',
            nombre: 'Caja Mostrador',
            descripcion: null,
            estatus: 'Activo',
            sucursales: 0,
            canales: 0,
            usuarios: 1,
          },
        ]),
      ),
    );
    const { result } = renderHook(() => useListarCajas(), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.[0].nombre).toBe('Caja Mostrador');
  });

  it('detalle: expone el DTO (el ETag queda en cache para el If-Match)', async () => {
    mswServer.use(
      http.get(`${BASE}/c-1`, () =>
        HttpResponse.json(detalle, { headers: { ETag: '"3"' } }),
      ),
    );
    const { result } = renderHook(() => useCaja('c-1'), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.version).toBe(3);
    expect(result.current.data?.canales[0].nombre).toBe('Mostrador');
  });
});

describe('mutaciones de caja', () => {
  it('crear envía Idempotency-Key', async () => {
    let idempotencyKey: string | null = null;
    mswServer.use(
      http.post(BASE, ({ request }) => {
        idempotencyKey = request.headers.get('Idempotency-Key');
        return HttpResponse.json({ id: 'c-9', version: 1 }, { status: 201 });
      }),
    );
    const { result } = renderHook(() => useCrearCaja(), {
      wrapper: createQueryWrapper(),
    });
    result.current.mutate({
      command: { nombre: 'Caja 9', descripcion: null },
      idempotencyKey: 'idem-123',
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(idempotencyKey).toBe('idem-123');
    expect(result.current.data?.id).toBe('c-9');
  });

  it('replace-set de sucursales manda If-Match con el ETag cacheado del detalle', async () => {
    let ifMatch: string | null = null;
    mswServer.use(
      http.get(`${BASE}/c-1`, () =>
        HttpResponse.json(detalle, { headers: { ETag: '"3"' } }),
      ),
      http.put(`${BASE}/c-1/sucursales`, ({ request }) => {
        ifMatch = request.headers.get('If-Match');
        return HttpResponse.json({ id: 'c-1', version: 4 });
      }),
    );
    const wrapper = createQueryWrapper();
    const caja = renderHook(() => useCaja('c-1'), { wrapper });
    await waitFor(() => expect(caja.result.current.isSuccess).toBe(true));

    const { result } = renderHook(() => useReemplazarSucursalesCaja(), { wrapper });
    result.current.mutate({ id: 'c-1', body: { sucursalIds: ['s-1', 's-2'] } });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(ifMatch).toBe('"3"');
  });

  it('sin header ETag (CORS/proxy lo filtra): If-Match cae al version del DTO', async () => {
    let ifMatch: string | null = null;
    mswServer.use(
      // GET SIN header ETag — simula un browser que no puede leerlo.
      http.get(`${BASE}/c-1`, () => HttpResponse.json(detalle)),
      http.put(`${BASE}/c-1/sucursales`, ({ request }) => {
        ifMatch = request.headers.get('If-Match');
        return HttpResponse.json({ id: 'c-1', version: 4 });
      }),
    );
    const wrapper = createQueryWrapper();
    const caja = renderHook(() => useCaja('c-1'), { wrapper });
    await waitFor(() => expect(caja.result.current.isSuccess).toBe(true));

    const { result } = renderHook(() => useReemplazarSucursalesCaja(), { wrapper });
    result.current.mutate({ id: 'c-1', body: { sucursalIds: ['s-2'] } });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(ifMatch).toBe('"3"'); // version: 3 del DTO, envuelto en comillas
  });
});

describe('useUsuarioAlcances', () => {
  it('lista las concesiones de un usuario', async () => {
    mswServer.use(
      http.get(`${BASE}/usuario-alcances`, ({ request }) => {
        const url = new URL(request.url);
        expect(url.searchParams.get('usuarioId')).toBe('u-1');
        return HttpResponse.json([
          { usuarioId: 'u-1', sucursalId: 's-1', canalVentaId: null },
        ]);
      }),
    );
    const { result } = renderHook(() => useUsuarioAlcances('u-1'), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.[0].canalVentaId).toBeNull();
  });
});
