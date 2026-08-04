import { describe, expect, it } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import {
  useAbrirSesion,
  useAjustesCaja,
  useCerrarSesion,
  useLiquidarRuta,
  useRegistrarCobro,
  useSesionActual,
} from '@/features/facturacion/api/useCajaSesiones';

const BASE = '*/api/v1/facturacion/cajas';

const sesion = {
  id: 'ses-1',
  cajaId: 'c-1',
  cajaNombre: 'Caja Mostrador',
  sucursalId: 's-1',
  responsableUsuarioId: 'u-1',
  estado: 'Abierta',
  diaOperacion: '2026-07-11',
  fechaApertura: '2026-07-11T14:00:00Z',
  fechaCierre: null,
  fondoApertura: 500,
  efectivoTeorico: null,
  efectivoDeclarado: null,
  diferencia: null,
  cierreExtemporaneo: false,
  notasCierre: null,
  autorizacionAperturaId: null,
  version: 2,
  cortes: [],
  movimientos: [],
  totalesPorForma: [{ formaPago: '01', montoSistema: 500, montoDeclarado: null }],
};

describe('useSesionActual', () => {
  it('devuelve la sesión vigente con el flag de día anterior', async () => {
    mswServer.use(
      http.get(`${BASE}/sesion-actual`, () =>
        HttpResponse.json({ sesion, diaAnteriorPendiente: false }),
      ),
    );
    const { result } = renderHook(() => useSesionActual(), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.sesion?.cajaNombre).toBe('Caja Mostrador');
    expect(result.current.data?.diaAnteriorPendiente).toBe(false);
  });
});

describe('mutaciones de sesión', () => {
  it('abrir envía Idempotency-Key y el body de apertura', async () => {
    let idempotencyKey: string | null = null;
    let body: unknown = null;
    mswServer.use(
      http.post(`${BASE}/c-1/sesiones`, async ({ request }) => {
        idempotencyKey = request.headers.get('Idempotency-Key');
        body = await request.json();
        return HttpResponse.json(
          { id: 'ses-1', version: 0, estado: 'Abierta', diaOperacion: '2026-07-11' },
          { status: 201 },
        );
      }),
    );
    const { result } = renderHook(() => useAbrirSesion(), {
      wrapper: createQueryWrapper(),
    });
    result.current.mutate({
      cajaId: 'c-1',
      body: { sucursalId: 's-1', fondoApertura: 500, autorizacionAperturaId: null },
      idempotencyKey: 'idem-abrir',
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(idempotencyKey).toBe('idem-abrir');
    expect(body).toMatchObject({ sucursalId: 's-1', fondoApertura: 500 });
  });

  it('cerrar manda If-Match con la versión de la sesión', async () => {
    let ifMatch: string | null = null;
    mswServer.use(
      http.post(`${BASE}/sesiones/ses-1/cierre`, ({ request }) => {
        ifMatch = request.headers.get('If-Match');
        return HttpResponse.json({
          id: 'ses-1',
          version: 3,
          estado: 'Cerrada',
          diaOperacion: '2026-07-11',
        });
      }),
    );
    const { result } = renderHook(() => useCerrarSesion(), {
      wrapper: createQueryWrapper(),
    });
    result.current.mutate({
      sesionId: 'ses-1',
      version: 2,
      body: { efectivoDeclarado: 480, notasCierre: 'faltante' },
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(ifMatch).toBe('"2"');
  });
});

describe('useRegistrarCobro', () => {
  it('registra el cobro con formas de pago y origen', async () => {
    let body: unknown = null;
    mswServer.use(
      http.post('*/api/v1/facturacion/cobros', async ({ request }) => {
        body = await request.json();
        return HttpResponse.json(
          {
            id: 'cobro-1',
            comprobanteId: 'f-1',
            cajaSesionId: 'ses-1',
            estado: 'Registrado',
            total: 1000,
          },
          { status: 201 },
        );
      }),
    );
    const { result } = renderHook(() => useRegistrarCobro(), {
      wrapper: createQueryWrapper(),
    });
    result.current.mutate({
      body: {
        comprobanteId: 'f-1',
        formasPago: [
          { formaPago: '01', importe: 600 },
          { formaPago: '04', importe: 400, referencia: 'AUTH-9' },
        ],
        origen: 2,
      },
      idempotencyKey: 'idem-cobro',
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(body).toMatchObject({ comprobanteId: 'f-1', origen: 2 });
    expect(result.current.data?.total).toBe(1000);
  });
});

describe('useLiquidarRuta (CAJAS-PR7)', () => {
  it('envía el batch con Idempotency-Key y expone el total', async () => {
    let idempotencyKey: string | null = null;
    let body: unknown = null;
    mswServer.use(
      http.post('*/api/v1/facturacion/cobros/liquidacion-ruta', async ({ request }) => {
        idempotencyKey = request.headers.get('Idempotency-Key');
        body = await request.json();
        return HttpResponse.json(
          {
            cajaSesionId: 'ses-1',
            total: 1150,
            cobros: [
              { id: 'c-1', comprobanteId: 'f-1', cajaSesionId: 'ses-1', estado: 'Registrado', total: 400 },
              { id: 'c-2', comprobanteId: 'r-1', cajaSesionId: 'ses-1', estado: 'Registrado', total: 750 },
            ],
          },
          { status: 201 },
        );
      }),
    );
    const { result } = renderHook(() => useLiquidarRuta(), {
      wrapper: createQueryWrapper(),
    });
    result.current.mutate({
      cobros: [
        { comprobanteId: 'f-1', formasPago: [{ formaPago: '01', importe: 400 }] },
        { comprobanteId: 'r-1', formasPago: [{ formaPago: '01', importe: 750 }] },
      ],
      idempotencyKey: 'idem-ruta',
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(idempotencyKey).toBe('idem-ruta');
    expect(body).toMatchObject({ cobros: [{ comprobanteId: 'f-1' }, { comprobanteId: 'r-1' }] });
    expect(result.current.data?.total).toBe(1150);
  });
});

describe('useAjustesCaja (CAJAS-PR7)', () => {
  it('lista los ajustes pendientes de una caja', async () => {
    mswServer.use(
      http.get(`${BASE}/c-1/ajustes`, ({ request }) => {
        expect(new URL(request.url).searchParams.get('incluirAplicados')).toBeNull();
        return HttpResponse.json([
          {
            id: 'a-1',
            cobroMostradorId: 'c-9',
            comprobanteFolio: 'F-9',
            importe: -300,
            formaPago: '01',
            motivo: 'Cancelación',
            creadoEn: '2026-07-11T10:00:00Z',
            aplicadoEnSesionId: null,
          },
        ]);
      }),
    );
    const { result } = renderHook(() => useAjustesCaja('c-1'), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.[0].importe).toBe(-300);
    expect(result.current.data?.[0].aplicadoEnSesionId).toBeNull();
  });
});
