import { describe, expect, it, vi } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import {
  createTestQueryClient,
  createQueryWrapper,
} from '@/test/test-query-client';
import {
  useListarPedidos,
  useCrearPedidoManual,
  useEditarPedido,
  useExcepciones,
  useResolverExcepcion,
  usePedido,
  usePedidoComprobantes,
  type CrearPedidoManualCommand,
  type EditarPedidoCommand,
} from '@/features/facturacion/api/usePedidos';
import { facturacionKeys } from '@/features/facturacion/api/keys';
import { ComportamientoFiscal } from '@/features/facturacion/api/types';

const BASE = '*/api/v1/facturacion/pedidos-facturables';

const commandValido: CrearPedidoManualCommand = {
  numeroPedido: null,
  sucursalId: '11111111-1111-4111-8111-111111111111',
  clienteId: '22222222-2222-4222-8222-222222222222',
  clienteNombre: 'Cliente Demo',
  // Id del catálogo compartido.canales_venta (FAC-ING-PR3).
  canalVenta: 1,
  comportamientoFiscal: ComportamientoFiscal.MostradorInmediato,
  moneda: 'MXN',
  obraId: null,
  obraNombre: null,
  comentarios: null,
  lineas: [
    {
      productoId: null,
      productoDescripcion: 'Vidrio 6mm',
      claveProdServSat: '01010101',
      claveUnidadSat: 'H87',
      cantidad: 2,
      precio: 100,
      descuento: 0,
      requierePedimento: false,
    },
  ],
};

describe('useListarPedidos', () => {
  it('200 OK: devuelve la lista de pedidos', async () => {
    mswServer.use(
      http.get(BASE, () =>
        HttpResponse.json({
          items: [
            {
              id: 'p-1',
              numeroPedido: 'MID-001',
              origen: 'Manual',
              estado: 'Importado',
              clienteNombre: 'Cliente Demo',
              total: 232,
              moneda: 'MXN',
              createdAt: '2026-05-30T10:00:00Z',
            },
          ],
          sinAsignarCount: null,
        }),
      ),
    );

    const { result } = renderHook(() => useListarPedidos({ limit: 200 }), {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    // CAJAS-PR6: el hook expone el envelope {items, sinAsignarCount}.
    expect(result.current.data?.items).toHaveLength(1);
    expect(result.current.data?.items[0].estado).toBe('Importado');
  });
});

describe('usePedido', () => {
  it('200 OK: devuelve el detalle con líneas', async () => {
    mswServer.use(
      http.get(`${BASE}/p-1`, () =>
        HttpResponse.json({
          id: 'p-1',
          numeroPedido: 'MID-001',
          origen: 'Manual',
          estado: 'Importado',
          clienteId: '22222222-2222-4222-8222-222222222222',
          clienteNombre: 'Cliente Demo',
          canalVentaId: 1,
          canalVenta: 'Tienda Cancún',
          comportamientoFiscal: 'MostradorInmediato',
          moneda: 'MXN',
          obraId: null,
          obraNombre: null,
          comentarios: null,
          comprobanteVigenteId: null,
          total: 232,
          ranura: null,
          version: 1,
          lineas: [
            {
              posicion: 1,
              productoId: null,
              productoDescripcion: 'Vidrio',
              claveProdServSat: '01010101',
              claveUnidadSat: 'H87',
              cantidad: 2,
              precio: 100,
              descuento: 0,
              requierePedimento: false,
            },
          ],
        }),
      ),
    );
    const { result } = renderHook(() => usePedido('p-1'), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.estado).toBe('Importado');
    expect(result.current.data?.lineas).toHaveLength(1);
  });

  it('no dispara la query con id null', () => {
    const { result } = renderHook(() => usePedido(null), {
      wrapper: createQueryWrapper(),
    });
    expect(result.current.fetchStatus).toBe('idle');
  });
});

describe('usePedidoComprobantes', () => {
  it('200 OK: devuelve el historial con el vigente marcado', async () => {
    mswServer.use(
      http.get(`${BASE}/p-1/comprobantes`, () =>
        HttpResponse.json([
          {
            id: 'f-1',
            folio: 'A-1',
            estado: 'Cancelado',
            uuid: '11111111-2222-3333-4444-555555555555',
            total: 116,
            moneda: 'MXN',
            fechaTimbrado: '2026-05-30T10:00:00Z',
            vigente: false,
          },
          {
            id: 'f-2',
            folio: 'A-2',
            estado: 'Timbrado',
            uuid: '99999999-2222-3333-4444-555555555555',
            total: 116,
            moneda: 'MXN',
            fechaTimbrado: '2026-05-30T11:00:00Z',
            vigente: true,
          },
        ]),
      ),
    );
    const { result } = renderHook(() => usePedidoComprobantes('p-1'), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data).toHaveLength(2);
    expect(result.current.data?.find((c) => c.vigente)?.folio).toBe('A-2');
  });
});

describe('useCrearPedidoManual', () => {
  it('201 OK: devuelve { id, estado, total, version } e invalida la familia', async () => {
    let bodyVisto: unknown = null;
    let idempotencyKeyVisto: string | null = null;

    mswServer.use(
      http.post(`${BASE}/`, async ({ request }) => {
        bodyVisto = await request.json();
        idempotencyKeyVisto = request.headers.get('Idempotency-Key');
        return HttpResponse.json(
          { id: 'p-new', estado: 'Importado', total: 232, version: 1 },
          { status: 201 },
        );
      }),
    );

    const queryClient = createTestQueryClient();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useCrearPedidoManual(), {
      wrapper: createQueryWrapper(queryClient),
    });

    result.current.mutate({ command: commandValido, idempotencyKey: 'idem-1' });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.id).toBe('p-new');
    expect(idempotencyKeyVisto).toBe('idem-1');
    expect(bodyVisto).toMatchObject({
      sucursalId: commandValido.sucursalId,
      clienteId: commandValido.clienteId,
    });
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: facturacionKeys.pedidos(),
    });
  });

  it('422 con errores[]: el caller puede mapear applyServerErrors', async () => {
    mswServer.use(
      http.post(`${BASE}/`, () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Validation failed',
            status: 422,
            code: 'CAMPO_INVALIDO',
            errores: [
              { campo: 'clienteNombre', codigo: 'REQUIRED', mensaje: 'Requerido' },
            ],
          },
          {
            status: 422,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );

    const { result } = renderHook(() => useCrearPedidoManual(), {
      wrapper: createQueryWrapper(),
    });

    result.current.mutate({ command: commandValido, idempotencyKey: 'idem-2' });

    await waitFor(() => expect(result.current.isError).toBe(true));
    const err = result.current.error as {
      status: number;
      problem: { errores?: { campo: string }[] };
    };
    expect(err.status).toBe(422);
    expect(err.problem.errores?.[0].campo).toBe('clienteNombre');
  });
});

const editCommand: EditarPedidoCommand = {
  clienteId: '22222222-2222-4222-8222-222222222222',
  clienteNombre: 'Cliente Demo Editado',
  canalVenta: 1,
  comportamientoFiscal: ComportamientoFiscal.MostradorInmediato,
  moneda: 'MXN',
  obraId: null,
  obraNombre: null,
  comentarios: null,
  lineas: [
    {
      productoId: null,
      productoDescripcion: 'Vidrio 6mm',
      claveProdServSat: '01010101',
      claveUnidadSat: 'H87',
      cantidad: 3,
      precio: 100,
      descuento: 0,
      requierePedimento: false,
    },
  ],
};

describe('useEditarPedido', () => {
  it('200 OK: PUT con If-Match (versión) e invalida pedido + familia', async () => {
    let ifMatchVisto: string | null = null;
    mswServer.use(
      http.put(`${BASE}/p-1`, ({ request }) => {
        ifMatchVisto = request.headers.get('If-Match');
        return HttpResponse.json({
          id: 'p-1',
          estado: 'Importado',
          total: 348,
          version: 2,
        });
      }),
    );
    const { result } = renderHook(() => useEditarPedido(), {
      wrapper: createQueryWrapper(),
    });
    result.current.mutate({ id: 'p-1', version: 1, command: editCommand });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.version).toBe(2);
    expect(ifMatchVisto).toBe('"1"');
  });

  it('409 CONCURRENCY_CONFLICT: el error llega al caller', async () => {
    mswServer.use(
      http.put(`${BASE}/p-1`, () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Conflicto de concurrencia',
            status: 409,
            code: 'CONCURRENCY_CONFLICT',
          },
          {
            status: 409,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );
    const { result } = renderHook(() => useEditarPedido(), {
      wrapper: createQueryWrapper(),
    });
    result.current.mutate({ id: 'p-1', version: 1, command: editCommand });
    await waitFor(() => expect(result.current.isError).toBe(true));
    const err = result.current.error as { status: number; code?: string };
    expect(err.status).toBe(409);
  });
});

describe('useExcepciones / useResolverExcepcion', () => {
  it('200 OK: lista de excepciones (soloPendientes)', async () => {
    let urlVista = '';
    mswServer.use(
      http.get(`${BASE}/excepciones`, ({ request }) => {
        urlVista = request.url;
        return HttpResponse.json([
          {
            id: 'ex-1',
            origen: 'Aw',
            pedidoRef: 'AW-123',
            motivo: 'ClienteNoExiste',
            detalle: 'RFC sin alta',
            resuelto: false,
            createdAt: '2026-05-30T10:00:00Z',
          },
        ]);
      }),
    );
    const { result } = renderHook(() => useExcepciones(true), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.[0].motivo).toBe('ClienteNoExiste');
    expect(urlVista).toContain('soloPendientes=true');
  });

  it('resolver: POST idempotente e invalida excepciones', async () => {
    let idemVisto: string | null = null;
    mswServer.use(
      http.post(`${BASE}/excepciones/ex-1/resolver`, ({ request }) => {
        idemVisto = request.headers.get('Idempotency-Key');
        return HttpResponse.json({ id: 'ex-1', resuelto: true });
      }),
    );
    const queryClient = createTestQueryClient();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');
    const { result } = renderHook(() => useResolverExcepcion(), {
      wrapper: createQueryWrapper(queryClient),
    });
    result.current.mutate({ id: 'ex-1', idempotencyKey: 'idem-ex1' });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.resuelto).toBe(true);
    expect(idemVisto).toBe('idem-ex1');
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: facturacionKeys.pedidosExcepciones(),
    });
  });
});
