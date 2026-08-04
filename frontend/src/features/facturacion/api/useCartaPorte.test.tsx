import { describe, expect, it, vi } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import {
  createTestQueryClient,
  createQueryWrapper,
} from '@/test/test-query-client';
import {
  useListarCartaPorte,
  useCartaPorte,
  useEmitirCartaPorte,
  useSiguienteTramo,
} from '@/features/facturacion/api/useCartaPorte';
import { facturacionKeys } from '@/features/facturacion/api/keys';
import type { EmitirCartaPorteCommand } from '@/features/facturacion/api/types';

const BASE = '*/api/v1/facturacion/carta-porte';

describe('useListarCartaPorte / useCartaPorte', () => {
  it('bandeja', async () => {
    mswServer.use(
      http.get(BASE, () =>
        HttpResponse.json([
          {
            id: 'c-1',
            folio: 'CP-1',
            estado: 'Timbrado',
            uuid: 'U-1',
            tipo: 'T',
            tramo: 'Cancún → Mérida',
            fechaSalida: '2026-05-30T08:00:00Z',
          },
        ]),
      ),
    );
    const { result } = renderHook(() => useListarCartaPorte(undefined), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.[0].tramo).toBe('Cancún → Mérida');
  });

  it('detalle con mercancías', async () => {
    mswServer.use(
      http.get(`${BASE}/c-1`, () =>
        HttpResponse.json({
          id: 'c-1',
          folio: 'CP-1',
          estado: 'Timbrado',
          uuid: 'U-1',
          tipo: 'T',
          origen: 'Cancún',
          destino: 'Mérida',
          distanciaKm: 300,
          fechaSalida: '2026-05-30T08:00:00Z',
          fechaLlegadaEstimada: '2026-05-30T14:00:00Z',
          cartaPortePreviaId: null,
          total: 0,
          vehiculo: { id: 'v-1', placa: 'ABC123', configVehicular: 'C2', anioModelo: 2020 },
          operador: { id: 'o-1', rfc: 'XXXX010101XX1', nombre: 'Operador Demo', numLicencia: 'L-9' },
          mercancias: [
            {
              descripcion: 'Vidrio',
              bienesTransp: '43211503',
              claveUnidad: 'KGM',
              cantidad: 10,
              pesoEnKg: 500,
              materialPeligroso: false,
            },
          ],
        }),
      ),
    );
    const { result } = renderHook(() => useCartaPorte('c-1'), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.vehiculo?.placa).toBe('ABC123');
    expect(result.current.data?.mercancias).toHaveLength(1);
  });
});

describe('useEmitirCartaPorte', () => {
  const command: EmitirCartaPorteCommand = {
    sucursalId: '11111111-1111-4111-8111-111111111111',
    cajaId: null,
    tipoCfdi: 'T',
    receptorRfc: 'AAA010101AAA',
    receptorNombre: 'Cliente Demo',
    receptorRegimenFiscal: '601',
    receptorCodigoPostal: '97000',
    receptorUsoCfdi: 'S01',
    receptorPais: 'MEX',
    rfcEmisor: 'BBB010101BBB',
    regimenFiscalEmisor: '601',
    moneda: 'MXN',
    origen: 'Cancún',
    destino: 'Mérida',
    origenCodigoPostal: '77500',
    origenEstado: 'ROO',
    destinoCodigoPostal: '97000',
    destinoEstado: 'YUC',
    distanciaKm: 300,
    vehiculoId: '22222222-2222-4222-8222-222222222222',
    operadorId: '33333333-3333-4333-8333-333333333333',
    pedidoFacturableId: null,
    fechaSalida: '2026-05-30T08:00:00Z',
    fechaLlegadaEstimada: '2026-05-30T14:00:00Z',
    montoServicio: 0,
    tasaIvaServicio: null,
    mercancias: [
      {
        descripcion: 'Vidrio',
        bienesTransp: '43211503',
        claveUnidad: 'KGM',
        cantidad: 10,
        pesoEnKg: 500,
        materialPeligroso: false,
      },
    ],
  };

  it('201 OK e invalida la familia', async () => {
    let idem: string | null = null;
    mswServer.use(
      http.post(`${BASE}/`, ({ request }) => {
        idem = request.headers.get('Idempotency-Key');
        return HttpResponse.json(
          {
            id: 'c-new',
            tipoCfdi: 'T',
            estado: 'Timbrado',
            uuid: 'U-N',
            folio: 'CP-9',
            total: 0,
            cartaPortePreviaId: null,
          },
          { status: 201 },
        );
      }),
    );
    const queryClient = createTestQueryClient();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');
    const { result } = renderHook(() => useEmitirCartaPorte(), {
      wrapper: createQueryWrapper(queryClient),
    });
    result.current.mutate({ command, idempotencyKey: 'idem-cp1' });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.folio).toBe('CP-9');
    expect(idem).toBe('idem-cp1');
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: facturacionKeys.cartaPorte(),
    });
  });
});

describe('useSiguienteTramo', () => {
  it('POST /{previaId}/siguiente-tramo con Idempotency-Key', async () => {
    let idem: string | null = null;
    mswServer.use(
      http.post(`${BASE}/c-1/siguiente-tramo`, ({ request }) => {
        idem = request.headers.get('Idempotency-Key');
        return HttpResponse.json({
          id: 'c-2',
          tipoCfdi: 'T',
          estado: 'Timbrado',
          uuid: 'U-2',
          folio: 'CP-2',
          total: 0,
          cartaPortePreviaId: 'c-1',
        });
      }),
    );
    const { result } = renderHook(() => useSiguienteTramo(), {
      wrapper: createQueryWrapper(),
    });
    result.current.mutate({
      previaId: 'c-1',
      command: {
        tipoCfdi: 'T',
        sucursalId: '11111111-1111-4111-8111-111111111111',
        origen: 'Mérida',
        destino: 'Campeche',
        origenCodigoPostal: '97000',
        origenEstado: 'YUC',
        destinoCodigoPostal: '24000',
        destinoEstado: 'CAM',
        distanciaKm: 170,
        vehiculoId: '22222222-2222-4222-8222-222222222222',
        operadorId: '33333333-3333-4333-8333-333333333333',
        fechaSalida: '2026-05-31T08:00:00Z',
        fechaLlegadaEstimada: '2026-05-31T14:00:00Z',
        montoServicio: 0,
        tasaIvaServicio: null,
      },
      idempotencyKey: 'idem-st1',
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.cartaPortePreviaId).toBe('c-1');
    expect(idem).toBe('idem-st1');
  });
});
