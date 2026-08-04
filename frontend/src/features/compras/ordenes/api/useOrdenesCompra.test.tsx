import { describe, expect, it } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useOrdenesCompra } from '@/features/compras/ordenes/api/useOrdenesCompra';
import {
  EstadoOrdenCompra,
  SubEstadoFacturacion,
  SubEstadoPago,
  SubEstadoRecepcion,
} from '@/features/compras/ordenes/api/types';

function makeOrdenCompraResumen(
  overrides: Partial<Record<string, unknown>> = {},
) {
  return {
    id: 'oc-1',
    folio: 'OC-2026-000042',
    folioAnio: 2026,
    estado: EstadoOrdenCompra.Borrador,
    subEstadoRecepcion: SubEstadoRecepcion.SinRecepcion,
    subEstadoFacturacion: SubEstadoFacturacion.SinFactura,
    subEstadoPago: SubEstadoPago.SinPago,
    proveedorId: 'p-1',
    compradorTitularId: 'u-1',
    moneda: 'MXN',
    fechaDocumento: '2026-05-09',
    referenciaProveedor: null,
    ...overrides,
  };
}

describe('useOrdenesCompra', () => {
  it('200 OK sin filtros: pega al endpoint base con paged response', async () => {
    let urlCapturada = '';
    mswServer.use(
      http.get('*/api/v1/compras/ordenes', ({ request }) => {
        urlCapturada = request.url;
        return HttpResponse.json({
          items: [makeOrdenCompraResumen()],
          page: 1,
          pageSize: 50,
          totalCount: 1,
        });
      }),
    );

    const { result } = renderHook(() => useOrdenesCompra(), {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.items).toHaveLength(1);
    expect(result.current.data?.page).toBe(1);
    expect(result.current.data?.pageSize).toBe(50);
    expect(result.current.data?.totalCount).toBe(1);
    // Sin filtros explícitos, no se manda ningún query param.
    expect(new URL(urlCapturada).search).toBe('');
  });

  it('manda solo los filtros presentes (omite undefined/null/empty)', async () => {
    let qs: URLSearchParams | null = null;
    mswServer.use(
      http.get('*/api/v1/compras/ordenes', ({ request }) => {
        qs = new URL(request.url).searchParams;
        return HttpResponse.json({
          items: [],
          page: 2,
          pageSize: 25,
          totalCount: 0,
        });
      }),
    );

    const { result } = renderHook(
      () =>
        useOrdenesCompra({
          estado: EstadoOrdenCompra.EnAutorizacionJefeCompras,
          subEstadoRecepcion: SubEstadoRecepcion.Parcial,
          proveedorId: 'p-9',
          referenciaProveedor: '',
          page: 2,
          pageSize: 25,
        }),
      { wrapper: createQueryWrapper() },
    );

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    const params = qs as unknown as URLSearchParams;
    expect(params.get('estado')).toBe(
      String(EstadoOrdenCompra.EnAutorizacionJefeCompras),
    );
    expect(params.get('subEstadoRecepcion')).toBe(
      String(SubEstadoRecepcion.Parcial),
    );
    expect(params.get('proveedorId')).toBe('p-9');
    expect(params.get('page')).toBe('2');
    expect(params.get('pageSize')).toBe('25');
    // Vacíos NO se mandan.
    expect(params.has('referenciaProveedor')).toBe(false);
    expect(params.has('compradorTitularId')).toBe(false);
    expect(params.has('fechaDesde')).toBe(false);
  });

  it('403 PERMISO_DENEGADO: lanza ApiError 403', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/ordenes', () =>
        HttpResponse.json(
          { type: 'about:blank', title: 'Forbidden', status: 403 },
          {
            status: 403,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );

    const { result } = renderHook(() => useOrdenesCompra(), {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    const err = result.current.error as { status: number };
    expect(err.status).toBe(403);
  });

  it('500 ERROR_INTERNO: lanza ApiError 500', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/ordenes', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Internal Server Error',
            status: 500,
          },
          {
            status: 500,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );

    const { result } = renderHook(() => useOrdenesCompra(), {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isError).toBe(true), {
      timeout: 3000,
    });
    const err = result.current.error as { status: number };
    expect(err.status).toBe(500);
  });
});
