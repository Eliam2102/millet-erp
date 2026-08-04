import { describe, expect, it, vi } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import {
  createTestQueryClient,
  createQueryWrapper,
} from '@/test/test-query-client';
import {
  useEmitirAnticipo,
  useControlAnticipos,
  useEstadoCuentaAnticipos,
} from '@/features/facturacion/api/useAnticipos';
import { facturacionKeys } from '@/features/facturacion/api/keys';
import { TipoAnticipo, type EmitirAnticipoCommand } from '@/features/facturacion/api/types';

const command: EmitirAnticipoCommand = {
  sucursalId: '11111111-1111-4111-8111-111111111111',
  clienteId: '22222222-2222-4222-8222-222222222222',
  receptorRfc: 'AAA010101AAA',
  receptorNombre: 'Cliente Demo',
  receptorRegimenFiscal: '601',
  receptorCodigoPostal: '97000',
  receptorUsoCfdi: 'G03',
  receptorPais: 'MEX',
  rfcEmisor: 'BBB010101BBB',
  regimenFiscalEmisor: '601',
  metodoPago: 'PUE',
  formaPago: '03',
  moneda: 'MXN',
  tipoCambio: null,
  tipoAnticipo: TipoAnticipo.ClientesMxp,
  montoBase: 1000,
  tasaIvaTraslado: 0.16,
  descripcion: null,
  pedidoFacturableId: null,
  pedidoOrigenRef: null,
  obraId: null,
  obraNombre: null,
};

describe('useEmitirAnticipo', () => {
  it('201 OK: devuelve { anticipoId, folio, saldo } e invalida anticipos+facturas', async () => {
    let idemVisto: string | null = null;
    mswServer.use(
      http.post('*/api/v1/facturacion/anticipos/', ({ request }) => {
        idemVisto = request.headers.get('Idempotency-Key');
        return HttpResponse.json(
          {
            anticipoId: 'a-new',
            facturaAnticipoId: 'f-ant',
            estado: 'Abierto',
            uuid: 'AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE',
            folio: 'FANT-1',
            total: 1160,
            saldo: 1160,
          },
          { status: 201 },
        );
      }),
    );
    const queryClient = createTestQueryClient();
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');
    const { result } = renderHook(() => useEmitirAnticipo(), {
      wrapper: createQueryWrapper(queryClient),
    });
    result.current.mutate({ command, idempotencyKey: 'idem-a1' });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.anticipoId).toBe('a-new');
    expect(idemVisto).toBe('idem-a1');
    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: facturacionKeys.anticipos(),
    });
  });

  it('422 catálogo inválido: el error llega al caller', async () => {
    mswServer.use(
      http.post('*/api/v1/facturacion/anticipos/', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Regla de negocio',
            status: 422,
            code: 'USO_CFDI_INVALIDO',
          },
          {
            status: 422,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );
    const { result } = renderHook(() => useEmitirAnticipo(), {
      wrapper: createQueryWrapper(),
    });
    result.current.mutate({ command, idempotencyKey: 'idem-a2' });
    await waitFor(() => expect(result.current.isError).toBe(true));
    const err = result.current.error as { status: number; code?: string };
    expect(err.status).toBe(422);
    expect(err.code).toBe('USO_CFDI_INVALIDO');
  });
});

describe('useControlAnticipos / useEstadoCuentaAnticipos', () => {
  it('control: devuelve el shape de reporte', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/anticipos/control', () =>
        HttpResponse.json({
          titulo: 'Control de Anticipos',
          generadoEn: '2026-05-30T10:00:00Z',
          filtrosAplicados: {},
          columnas: [
            { clave: 'folio', etiqueta: 'Folio', tipo: 'texto' },
            { clave: 'saldo', etiqueta: 'Saldo', tipo: 'moneda' },
          ],
          filas: [
            {
              anticipoId: 'a-1',
              folio: 'FANT-1',
              cliente: 'Cliente Demo',
              obra: null,
              tipoAnticipo: 'ClientesMxp',
              moneda: 'MXN',
              montoCobrado: 1160,
              montoAmortizado: 0,
              saldo: 1160,
              estado: 'Abierto',
              pedidoOrigenRef: null,
              fechaEmision: '2026-05-30T09:00:00Z',
            },
          ],
          totales: { saldo: 1160 },
        }),
      ),
    );
    const { result } = renderHook(() => useControlAnticipos({}), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.filas).toHaveLength(1);
    expect(result.current.data?.totales?.saldo).toBe(1160);
  });

  it('estado de cuenta por cliente', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/anticipos/control/cli-1', () =>
        HttpResponse.json({
          clienteId: 'cli-1',
          generadoEn: '2026-05-30T10:00:00Z',
          totalCobrado: 1160,
          totalAmortizado: 500,
          totalSaldo: 660,
          anticipos: [
            {
              anticipoId: 'a-1',
              folio: 'FANT-1',
              tipoAnticipo: 'ClientesMxp',
              montoCobrado: 1160,
              montoAmortizado: 500,
              saldo: 660,
              estado: 'Abierto',
              pedidoOrigenRef: null,
              vinculaciones: [
                {
                  facturaVentaId: 'f-1',
                  facturaFolio: 'A-9',
                  importe: 500,
                  ncAmortizacionId: 'nc-1',
                  ncFolio: 'NC-3',
                  ncTimbrada: true,
                },
              ],
            },
          ],
        }),
      ),
    );
    const { result } = renderHook(() => useEstadoCuentaAnticipos('cli-1'), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.totalSaldo).toBe(660);
    expect(result.current.data?.anticipos[0].vinculaciones[0].ncFolio).toBe('NC-3');
  });
});
