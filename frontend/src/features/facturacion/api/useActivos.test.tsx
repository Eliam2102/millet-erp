import { describe, expect, it } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import {
  useAutorizacionesActivo,
  useAutorizarVentaActivo,
} from '@/features/facturacion/api/useActivos';

const BASE = '*/api/v1/facturacion/activos';

describe('useAutorizacionesActivo', () => {
  it('bandeja de autorizaciones', async () => {
    mswServer.use(
      http.get(`${BASE}/autorizaciones`, () =>
        HttpResponse.json([
          {
            id: 'au-1',
            activoRef: 'AF-1',
            descripcion: 'Montacargas',
            precioVenta: 50000,
            valorNetoEnLibros: 30000,
            utilidadOPerdida: 20000,
            autorizadoPor: 'u-1',
            fechaAutorizacion: '2026-05-30T10:00:00Z',
            estado: 'Autorizada',
          },
        ]),
      ),
    );
    const { result } = renderHook(() => useAutorizacionesActivo(undefined), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.[0].descripcion).toBe('Montacargas');
  });
});

describe('useAutorizarVentaActivo', () => {
  it('POST autorizar con Idempotency-Key', async () => {
    let idem: string | null = null;
    mswServer.use(
      http.post(`${BASE}/autorizar`, ({ request }) => {
        idem = request.headers.get('Idempotency-Key');
        return HttpResponse.json({
          autorizacionId: 'au-new',
          descripcion: 'Montacargas',
          valorNetoEnLibros: 30000,
          utilidadOPerdida: 20000,
        });
      }),
    );
    const { result } = renderHook(() => useAutorizarVentaActivo(), {
      wrapper: createQueryWrapper(),
    });
    result.current.mutate({
      command: { activoRef: 'AF-1', precioVenta: 50000 },
      idempotencyKey: 'idem-au1',
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.autorizacionId).toBe('au-new');
    expect(idem).toBe('idem-au1');
  });
});
