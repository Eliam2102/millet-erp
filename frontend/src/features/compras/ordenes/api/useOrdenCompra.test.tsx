import { describe, expect, it } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import {
  createTestQueryClient,
  createQueryWrapper,
} from '@/test/test-query-client';
import {
  useEtagOc,
  useOrdenCompra,
} from '@/features/compras/ordenes/api/useOrdenCompra';
import { ordenesKeys } from '@/features/compras/ordenes/api/keys';
import {
  EstadoOrdenCompra,
  SubEstadoFacturacion,
  SubEstadoPago,
  SubEstadoRecepcion,
} from '@/features/compras/ordenes/api/types';

function makeOrdenCompraResponse(
  overrides: Partial<Record<string, unknown>> = {},
) {
  return {
    id: 'oc-1',
    empresaId: 'e-1',
    folio: 'OC-2026-000042',
    folioAnio: 2026,
    proveedorId: 'p-1',
    sucursalDestinoId: 's-1',
    condicionesPagoId: 'cp-1',
    usoPrincipalId: 'up-1',
    moneda: 'MXN',
    tipoCambio: null,
    compradorTitularId: 'u-1',
    encargadoComprasId: 'u-1',
    observaciones: null,
    sinRequisicionPrevia: false,
    esImportacion: false,
    cotizacionExcepcionada: false,
    fechaDocumento: '2026-05-09',
    fechaContabilizacion: null,
    fechaEntregaEsperada: null,
    estado: EstadoOrdenCompra.Borrador,
    subEstadoRecepcion: SubEstadoRecepcion.SinRecepcion,
    subEstadoFacturacion: SubEstadoFacturacion.SinFactura,
    subEstadoPago: SubEstadoPago.SinPago,
    motivoSinRequisicion: null,
    motivoCancelacion: null,
    motivoRechazoId: null,
    motivoRechazoTexto: null,
    ocOrigenId: null,
    version: 12,
    createdAt: '2026-05-09T10:00:00Z',
    updatedAt: '2026-05-09T10:00:00Z',
    ...overrides,
  };
}

describe('useOrdenCompra', () => {
  it('200 OK: devuelve detalle y captura ETag en el cache', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/ordenes/oc-1', () =>
        HttpResponse.json(makeOrdenCompraResponse(), {
          headers: { ETag: '"12"' },
        }),
      ),
    );

    const queryClient = createTestQueryClient();
    const { result } = renderHook(() => useOrdenCompra('oc-1'), {
      wrapper: createQueryWrapper(queryClient),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.folio).toBe('OC-2026-000042');
    expect(result.current.data?.version).toBe(12);

    // El cache crudo expone { data, etag }; el select solo deja data
    // visible para los consumidores. useEtagOc(id) lee el etag.
    const cached = queryClient.getQueryData(ordenesKeys.detail('oc-1')) as {
      data: unknown;
      etag?: string;
    };
    expect(cached?.etag).toBe('12');
  });

  it('useEtagOc(id) lee el ETag guardado tras la query', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/ordenes/oc-1', () =>
        HttpResponse.json(makeOrdenCompraResponse(), {
          headers: { ETag: '"7"' },
        }),
      ),
    );

    const queryClient = createTestQueryClient();
    const wrapper = createQueryWrapper(queryClient);

    const { result: ocResult } = renderHook(() => useOrdenCompra('oc-1'), {
      wrapper,
    });
    await waitFor(() => expect(ocResult.current.isSuccess).toBe(true));

    const { result: etagResult } = renderHook(() => useEtagOc('oc-1'), {
      wrapper,
    });
    expect(etagResult.current).toBe('7');
  });

  it('useEtagOc devuelve undefined si la query no se ejecutó', () => {
    const { result } = renderHook(() => useEtagOc('no-cargada'), {
      wrapper: createQueryWrapper(),
    });
    expect(result.current).toBeUndefined();
  });

  it('id null inhabilita el query (no fetch)', async () => {
    let llamado = false;
    mswServer.use(
      http.get('*/api/v1/compras/ordenes/anything', () => {
        llamado = true;
        return HttpResponse.json({});
      }),
    );

    const { result } = renderHook(() => useOrdenCompra(null), {
      wrapper: createQueryWrapper(),
    });

    await new Promise((r) => setTimeout(r, 10));
    expect(result.current.fetchStatus).toBe('idle');
    expect(llamado).toBe(false);
  });

  it('404 ORDEN_COMPRA_NO_ENCONTRADA: lanza ApiError 404', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/ordenes/oc-404', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'No encontrada',
            status: 404,
            code: 'ORDEN_COMPRA_NO_ENCONTRADA',
          },
          {
            status: 404,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );

    const { result } = renderHook(() => useOrdenCompra('oc-404'), {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    const err = result.current.error as { status: number };
    expect(err.status).toBe(404);
  });

  it('403 PERMISO_DENEGADO: lanza ApiError 403', async () => {
    mswServer.use(
      http.get('*/api/v1/compras/ordenes/oc-403', () =>
        HttpResponse.json(
          { type: 'about:blank', title: 'Forbidden', status: 403 },
          {
            status: 403,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );

    const { result } = renderHook(() => useOrdenCompra('oc-403'), {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    const err = result.current.error as { status: number };
    expect(err.status).toBe(403);
  });
});
