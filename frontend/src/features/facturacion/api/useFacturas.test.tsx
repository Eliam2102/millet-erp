import { describe, expect, it, vi } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import {
  createTestQueryClient,
  createQueryWrapper,
} from '@/test/test-query-client';
import {
  useListarFacturas,
  useComprobante,
  useComprobanteEnvios,
  useReenviarCorreo,
  useEmitirFactura,
  useCancelacionEstatus,
  useSolicitarCancelacion,
  useEmitirNcBonificacion,
  useAplicarPedimento,
} from '@/features/facturacion/api/useFacturas';
import { facturacionKeys } from '@/features/facturacion/api/keys';
import {
  ComportamientoFiscal,
  type EmitirFacturaVentaCommand,
} from '@/features/facturacion/api/types';

const BASE = '*/api/v1/facturacion/facturas';

const command: EmitirFacturaVentaCommand = {
  sucursalId: '11111111-1111-4111-8111-111111111111',
  receptorRfc: 'XAXX010101000',
  receptorNombre: 'Público en general',
  receptorRegimenFiscal: '616',
  receptorCodigoPostal: '97000',
  receptorUsoCfdi: 'S01',
  receptorPais: 'MEX',
  rfcEmisor: 'AAA010101AAA',
  regimenFiscalEmisor: '601',
  metodoPago: 'PUE',
  formaPago: '01',
  moneda: 'MXN',
  tipoCambio: null,
  // Id del catálogo compartido.canales_venta (FAC-ING-PR3).
  canalVenta: 1,
  comportamientoFiscal: ComportamientoFiscal.MostradorInmediato,
  obraId: null,
  obraNombre: null,
  facturaAgrupada: false,
  lineas: [
    {
      productoId: null,
      claveProdServSat: '01010101',
      descripcion: 'Vidrio',
      claveUnidadSat: 'H87',
      cantidad: 1,
      valorUnitario: 100,
      descuento: 0,
      objetoImp: '02',
      tasaIvaTraslado: 0.16,
      tasaRetencionIva: null,
      tasaRetencionIsr: null,
      requierePedimento: false,
    },
  ],
};

describe('useListarFacturas', () => {
  it('200 OK: devuelve la lista', async () => {
    mswServer.use(
      http.get(BASE, () =>
        HttpResponse.json({
          items: [
            {
              id: 'f-1',
              folio: 'A-1',
              estado: 'Timbrado',
              uuid: '11111111-2222-3333-4444-555555555555',
              receptorNombre: 'Público en general',
              receptorRfc: 'XAXX010101000',
              total: 116,
              moneda: 'MXN',
              fechaTimbrado: '2026-05-30T10:00:00Z',
            },
          ],
          sinAsignarCount: null,
        }),
      ),
    );
    const { result } = renderHook(() => useListarFacturas({ limit: 200 }), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    // CAJAS-PR6: el hook expone el envelope {items, sinAsignarCount}.
    expect(result.current.data?.items[0].estado).toBe('Timbrado');
    expect(result.current.data?.sinAsignarCount).toBeNull();
  });
});

describe('useComprobante', () => {
  it('200 OK: devuelve el detalle con líneas', async () => {
    mswServer.use(
      http.get(`${BASE}/f-1`, () =>
        HttpResponse.json({
          id: 'f-1',
          tipo: 'Ingreso',
          folio: 'A-1',
          estado: 'Timbrado',
          uuid: '11111111-2222-3333-4444-555555555555',
          receptorRfc: 'XAXX010101000',
          receptorNombre: 'Público en general',
          moneda: 'MXN',
          subtotal: 100,
          descuento: 0,
          impuestosTrasladados: 16,
          retenciones: 0,
          total: 116,
          fechaTimbrado: '2026-05-30T10:00:00Z',
          version: 2,
          lineas: [
            {
              posicion: 1,
              claveProdServSat: '01010101',
              descripcion: 'Vidrio',
              claveUnidadSat: 'H87',
              cantidad: 1,
              valorUnitario: 100,
              descuento: 0,
              importe: 100,
            },
          ],
        }),
      ),
    );
    const { result } = renderHook(() => useComprobante('f-1'), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.total).toBe(116);
    expect(result.current.data?.lineas).toHaveLength(1);
  });

  it('no dispara la query con id null', () => {
    const { result } = renderHook(() => useComprobante(null), {
      wrapper: createQueryWrapper(),
    });
    expect(result.current.fetchStatus).toBe('idle');
  });
});

describe('useEmitirFactura', () => {
  it('201 OK: devuelve { id, estado, uuid, folio } e invalida la familia', async () => {
    let idempotencyKeyVisto: string | null = null;
    mswServer.use(
      http.post(`${BASE}/`, ({ request }) => {
        idempotencyKeyVisto = request.headers.get('Idempotency-Key');
        return HttpResponse.json(
          {
            id: 'f-new',
            estado: 'Timbrado',
            uuid: 'AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE',
            folio: 'A-100',
            total: 116,
            version: 1,
          },
          { status: 201 },
        );
      }),
    );

    const queryClient = createTestQueryClient();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    const { result } = renderHook(() => useEmitirFactura(), {
      wrapper: createQueryWrapper(queryClient),
    });
    result.current.mutate({ command, idempotencyKey: 'idem-f1' });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.uuid).toBeTruthy();
    expect(idempotencyKeyVisto).toBe('idem-f1');
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: facturacionKeys.facturas(),
    });
  });

  it('422 catálogo SAT inválido: el error llega al caller', async () => {
    mswServer.use(
      http.post(`${BASE}/`, () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Regla de negocio',
            status: 422,
            code: 'FORMA_PAGO_INVALIDA',
            detail: "La forma de pago '99' no existe en el catálogo SAT.",
          },
          {
            status: 422,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );
    const { result } = renderHook(() => useEmitirFactura(), {
      wrapper: createQueryWrapper(),
    });
    result.current.mutate({ command, idempotencyKey: 'idem-f2' });
    await waitFor(() => expect(result.current.isError).toBe(true));
    const err = result.current.error as { status: number; code?: string };
    expect(err.status).toBe(422);
    expect(err.code).toBe('FORMA_PAGO_INVALIDA');
  });
});

describe('useComprobanteEnvios', () => {
  it('200 OK: devuelve la bitácora de envíos', async () => {
    mswServer.use(
      http.get(`${BASE}/f-1/envios`, () =>
        HttpResponse.json([
          {
            id: 'e-1',
            destinatario: 'cliente@demo.com',
            estado: 'Enviado',
            intentos: 1,
            enviadoAt: '2026-05-30T12:00:00Z',
            ultimoError: null,
          },
        ]),
      ),
    );
    const { result } = renderHook(() => useComprobanteEnvios('f-1'), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.[0].destinatario).toBe('cliente@demo.com');
  });
});

describe('useReenviarCorreo', () => {
  it('202/200 OK: POST con Idempotency-Key e invalida la bitácora', async () => {
    let idemVisto: string | null = null;
    mswServer.use(
      http.post(`${BASE}/f-1/reenviar-correo`, ({ request }) => {
        idemVisto = request.headers.get('Idempotency-Key');
        return HttpResponse.json({ bitacoraId: 'b-1', estado: 'Encolado' });
      }),
    );
    const queryClient = createTestQueryClient();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');
    const { result } = renderHook(() => useReenviarCorreo(), {
      wrapper: createQueryWrapper(queryClient),
    });
    result.current.mutate({
      id: 'f-1',
      destinatario: 'cliente@demo.com',
      idempotencyKey: 'idem-r1',
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.bitacoraId).toBe('b-1');
    expect(idemVisto).toBe('idem-r1');
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: facturacionKeys.facturaEnvios('f-1'),
    });
  });
});

describe('cancelación + NC bonificación (FE-F5)', () => {
  it('useCancelacionEstatus: GET estatus', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/comprobantes/f-1/cancelar', () =>
        HttpResponse.json({
          comprobanteId: 'f-1',
          estadoComprobante: 'CancelacionPendiente',
          solicitudId: 's-1',
          estadoSolicitud: 'EnProceso',
          motivoSat: '02',
          estatusSat: null,
          mensajeError: null,
          solicitadaEn: '2026-05-30T12:00:00Z',
          resueltaEn: null,
        }),
      ),
    );
    const { result } = renderHook(() => useCancelacionEstatus('f-1'), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.estadoSolicitud).toBe('EnProceso');
  });

  it('useSolicitarCancelacion: POST con motivo + Idempotency-Key', async () => {
    let body: Record<string, unknown> | null = null;
    let idem: string | null = null;
    mswServer.use(
      http.post('*/api/v1/facturacion/comprobantes/f-1/cancelar', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        idem = request.headers.get('Idempotency-Key');
        return HttpResponse.json({
          solicitudId: 's-1',
          comprobanteId: 'f-1',
          estadoComprobante: 'CancelacionPendiente',
          estadoSolicitud: 'EnProceso',
          estatusSat: null,
        });
      }),
    );
    const { result } = renderHook(() => useSolicitarCancelacion(), {
      wrapper: createQueryWrapper(),
    });
    result.current.mutate({
      id: 'f-1',
      motivoSat: '01',
      uuidSustituto: 'UUID-SUS',
      idempotencyKey: 'idem-c1',
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(body).toMatchObject({ motivoSat: '01', uuidSustituto: 'UUID-SUS' });
    expect(idem).toBe('idem-c1');
  });

  it('useEmitirNcBonificacion: POST 201', async () => {
    mswServer.use(
      http.post('*/api/v1/facturacion/notas-credito/bonificacion', () =>
        HttpResponse.json(
          { id: 'nc-1', estado: 'Timbrado', uuid: 'U-NC', folio: 'NC-1', total: 116 },
          { status: 201 },
        ),
      ),
    );
    const { result } = renderHook(() => useEmitirNcBonificacion(), {
      wrapper: createQueryWrapper(),
    });
    result.current.mutate({
      command: {
        facturaVentaId: 'f-1',
        montoTotal: 100,
        tasaIva: 0.16,
        descripcion: 'Bonificación',
      },
      idempotencyKey: 'idem-nc1',
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.folio).toBe('NC-1');
  });
});

describe('useAplicarPedimento (FE-F7)', () => {
  it('POST /pedimento con Idempotency-Key', async () => {
    let idem: string | null = null;
    let body: Record<string, unknown> | null = null;
    mswServer.use(
      http.post('*/api/v1/facturacion/facturas/f-1/pedimento', async ({ request }) => {
        idem = request.headers.get('Idempotency-Key');
        body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json({
          id: 'f-1',
          estado: 'TimbradoEnProceso',
          uuid: null,
          folio: 'A-1',
        });
      }),
    );
    const { result } = renderHook(() => useAplicarPedimento(), {
      wrapper: createQueryWrapper(),
    });
    result.current.mutate({
      id: 'f-1',
      command: {
        pedimento: '15  47  3807  5000123',
        fechaDocAduanero: '2026-05-20',
        identificacionMercancia: null,
      },
      idempotencyKey: 'idem-p1',
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(idem).toBe('idem-p1');
    expect(body).toMatchObject({ pedimento: '15  47  3807  5000123' });
  });
});
