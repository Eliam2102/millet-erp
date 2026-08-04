import { describe, expect, it, vi } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import {
  createTestQueryClient,
  createQueryWrapper,
} from '@/test/test-query-client';
import {
  useListarRepp,
  useRepp,
  useEmitirRepp,
  useFacturasCobrablesPpd,
} from '@/features/facturacion/api/useRepp';
import { facturacionKeys } from '@/features/facturacion/api/keys';
import type { EmitirReppCommand } from '@/features/facturacion/api/types';

const BASE = '*/api/v1/facturacion/repp';

describe('useListarRepp / useRepp', () => {
  it('bandeja: lista REPP', async () => {
    mswServer.use(
      http.get(BASE, () =>
        HttpResponse.json({
          items: [
            {
              id: 'r-1',
              folio: 'REPP-1',
              estado: 'Timbrado',
              uuid: 'U-1',
              receptorNombre: 'Cliente Demo',
              importeTotalPago: 1000,
              fechaPago: '2026-05-30T12:00:00Z',
            },
          ],
          sinAsignarCount: null,
        }),
      ),
    );
    const { result } = renderHook(() => useListarRepp(undefined), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    // CAJAS-PR6: el hook expone el envelope {items, sinAsignarCount}.
    expect(result.current.data?.items[0].folio).toBe('REPP-1');
  });

  it('detalle: facturas cubiertas', async () => {
    mswServer.use(
      http.get(`${BASE}/r-1`, () =>
        HttpResponse.json({
          id: 'r-1',
          folio: 'REPP-1',
          estado: 'Timbrado',
          uuid: 'U-1',
          receptorNombre: 'Cliente Demo',
          monedaPago: 'MXN',
          importeTotalPago: 1000,
          fechaPago: '2026-05-30T12:00:00Z',
          facturasCubiertas: [
            {
              facturaVentaId: 'f-1',
              folio: 'A-9',
              facturaUuid: 'FU-9',
              numParcialidad: 1,
              importePagado: 1000,
              saldoInsoluto: 0,
              gananciaPerdidaCambiaria: 0,
            },
          ],
        }),
      ),
    );
    const { result } = renderHook(() => useRepp('r-1'), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.facturasCubiertas[0].folio).toBe('A-9');
  });
});

describe('useFacturasCobrablesPpd', () => {
  it('lista facturas PPD con saldo; propaga receptorRfc', async () => {
    let rfcParam: string | null = null;
    mswServer.use(
      http.get(`${BASE}/facturas-cobrables`, ({ request }) => {
        rfcParam = new URL(request.url).searchParams.get('receptorRfc');
        return HttpResponse.json([
          {
            facturaVentaId: 'f-1',
            folio: 'A-9',
            receptorRfc: 'AAA010101AAA',
            receptorNombre: 'Cliente Demo',
            moneda: 'MXN',
            total: 1000,
            acreditadoNc: 400,
            pagadoRepp: 100,
            saldo: 500,
            numParcialidadSiguiente: 2,
            fechaTimbrado: '2026-05-30T12:00:00Z',
          },
        ]);
      }),
    );
    const { result } = renderHook(() => useFacturasCobrablesPpd('AAA010101AAA'), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(rfcParam).toBe('AAA010101AAA');
    expect(result.current.data?.[0].saldo).toBe(500);
    expect(result.current.data?.[0].numParcialidadSiguiente).toBe(2);
  });
});

describe('useEmitirRepp', () => {
  const command: EmitirReppCommand = {
    sucursalId: '11111111-1111-4111-8111-111111111111',
    fechaPago: '2026-05-30T12:00:00Z',
    monedaPago: 'MXN',
    tcPago: null,
    formaPagoReal: '03',
    cuentaOrdenante: null,
    cuentaBeneficiaria: null,
    referenciaPago: null,
    facturas: [{ facturaVentaId: 'f-1', importePagado: 1000 }],
  };

  it('201 OK: emite e invalida repp + facturas', async () => {
    let idem: string | null = null;
    mswServer.use(
      http.post(`${BASE}/`, ({ request }) => {
        idem = request.headers.get('Idempotency-Key');
        return HttpResponse.json(
          {
            id: 'r-new',
            estado: 'Timbrado',
            uuid: 'U-N',
            folio: 'REPP-9',
            importeTotalPago: 1000,
            gananciaPerdidaCambiariaTotal: 0,
            facturas: [],
          },
          { status: 201 },
        );
      }),
    );
    const queryClient = createTestQueryClient();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');
    const { result } = renderHook(() => useEmitirRepp(), {
      wrapper: createQueryWrapper(queryClient),
    });
    result.current.mutate({ command, idempotencyKey: 'idem-r1' });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.folio).toBe('REPP-9');
    expect(idem).toBe('idem-r1');
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: facturacionKeys.repp(),
    });
  });
});
