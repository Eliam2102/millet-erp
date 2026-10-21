import { describe, expect, it } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import {
  useAnticiposCliente,
  useAntiguedadSaldos,
  useEnviarEstadoCuenta,
  useEnviosEstadoCuenta,
  useEstadoCuentaCliente,
  usePendientesEstadoCuenta,
} from '@/features/cxc/api/useCartera';
import {
  AlineacionColumnaCxc,
  TipoColumnaReporteCxc,
} from '@/features/cxc/api/types';

const BASE = '*/api/v1/cuentas-por-cobrar';

const reporte = {
  titulo: 'Antigüedad de saldos',
  generadoEn: '2026-07-14T12:00:00Z',
  filtrosAplicados: [],
  columnas: [
    {
      key: 'cliente',
      label: 'Cliente',
      tipo: TipoColumnaReporteCxc.Texto,
      alineacion: AlineacionColumnaCxc.Izquierda,
    },
  ],
  filas: [{ cliente: 'ACME SA' }],
  totales: null,
};

describe('useAntiguedadSaldos', () => {
  it('manda filtros como query params', async () => {
    let url: URL | null = null;
    mswServer.use(
      http.get(`${BASE}/cartera/antiguedad`, ({ request }) => {
        url = new URL(request.url);
        return HttpResponse.json(reporte);
      }),
    );
    const { result } = renderHook(
      () =>
        useAntiguedadSaldos({
          fechaCorte: '2026-07-14',
          moneda: 'MXN',
        }),
      { wrapper: createQueryWrapper() },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(url!.searchParams.get('fechaCorte')).toBe('2026-07-14');
    expect(url!.searchParams.get('moneda')).toBe('MXN');
    expect(result.current.data?.titulo).toBe('Antigüedad de saldos');
  });
});

describe('useEstadoCuentaCliente', () => {
  it('GET por cliente; null → deshabilitada', async () => {
    mswServer.use(
      http.get(`${BASE}/cartera/estado-cuenta/cli-1`, () =>
        HttpResponse.json({ ...reporte, titulo: 'Estado de cuenta' }),
      ),
    );
    const { result } = renderHook(() => useEstadoCuentaCliente('cli-1'), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.titulo).toBe('Estado de cuenta');

    const { result: sinCliente } = renderHook(
      () => useEstadoCuentaCliente(null),
      { wrapper: createQueryWrapper() },
    );
    expect(sinCliente.current.fetchStatus).toBe('idle');
  });
});

describe('useAnticiposCliente', () => {
  it('lista saldos del read port por cliente', async () => {
    mswServer.use(
      http.get(`${BASE}/anticipos`, ({ request }) => {
        const url = new URL(request.url);
        expect(url.searchParams.get('clienteId')).toBe('cli-1');
        return HttpResponse.json([
          {
            anticipoId: 'ant-1',
            clienteId: 'cli-1',
            estado: 'Abierto',
            montoCobrado: 100000,
            montoAmortizado: 40000,
            saldo: 60000,
            saldoDisponible: 60000,
            moneda: 'MXN',
            pedidoOrigenRef: '3000123456',
          },
        ]);
      }),
    );
    const { result } = renderHook(() => useAnticiposCliente('cli-1'), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.[0].saldoDisponible).toBe(60000);
  });

  it('sin cliente → deshabilitada', () => {
    const { result } = renderHook(() => useAnticiposCliente(null), {
      wrapper: createQueryWrapper(),
    });
    expect(result.current.fetchStatus).toBe('idle');
  });
});

describe('usePendientesEstadoCuenta (C7.8)', () => {
  it('GET de pagos que bloquean el estado de cuenta; null → deshabilitada', async () => {
    mswServer.use(
      http.get(`${BASE}/cartera/estado-cuenta/cli-1/pendientes`, () =>
        HttpResponse.json({
          emitible: false,
          pendientes: [
            {
              id: 'rp-1',
              fechaValor: '2026-10-01',
              fechaLimite: '2026-11-05',
              monto: 1160,
              moneda: 'MXN',
              referencia: 'SPEI 123',
              motivo: 'SinRep',
            },
          ],
          recibosSinReflejar: [],
          emailCliente: null,
        }),
      ),
    );
    const { result } = renderHook(() => usePendientesEstadoCuenta('cli-1'), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.emitible).toBe(false);
    expect(result.current.data?.pendientes[0].referencia).toBe('SPEI 123');

    const { result: sinCliente } = renderHook(() => usePendientesEstadoCuenta(null), {
      wrapper: createQueryWrapper(),
    });
    expect(sinCliente.current.fetchStatus).toBe('idle');
  });
});

describe('useEnviosEstadoCuenta (C7.8)', () => {
  it('lista el historial de envíos del cliente', async () => {
    mswServer.use(
      http.get(`${BASE}/cartera/estado-cuenta/cli-1/envios`, () =>
        HttpResponse.json([
          {
            id: 'env-1',
            clienteId: 'cli-1',
            destinatario: 'cobranza@cliente.mx',
            mensaje: null,
            usuarioId: 'u-1',
            usuarioNombre: 'Uziel',
            fecha: '2026-10-20T12:00:00Z',
            estado: 'Enviado',
            detalleError: null,
            numeroFilas: 3,
            nombreArchivo: 'estado-cuenta.pdf',
            pdfSha256: 'a'.repeat(64),
            totales: [],
          },
        ]),
      ),
    );
    const { result } = renderHook(() => useEnviosEstadoCuenta('cli-1'), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.[0].estado).toBe('Enviado');
  });
});

describe('useEnviarEstadoCuenta (C7.8)', () => {
  it('POST multipart con PDF, destinatario e Idempotency-Key fresca', async () => {
    const llaves: string[] = [];
    let cuerpo = '';
    mswServer.use(
      http.post(`${BASE}/cartera/estado-cuenta/cli-1/envios`, async ({ request }) => {
        llaves.push(request.headers.get('Idempotency-Key') ?? '');
        // jsdom y undici no comparten la clase File: se inspecciona el multipart crudo.
        cuerpo = await request.text();
        return HttpResponse.json({ id: 'env-1', estado: 'Enviado' }, { status: 201 });
      }),
    );
    const { result } = renderHook(() => useEnviarEstadoCuenta(), {
      wrapper: createQueryWrapper(),
    });
    const archivo = new Blob(['%PDF-1.4'], { type: 'application/pdf' });
    await act(async () => {
      await result.current.mutateAsync({
        clienteId: 'cli-1',
        destinatario: 'cobranza@cliente.mx',
        mensaje: 'Saludos',
        pdf: archivo,
        nombreArchivo: 'estado-cuenta.pdf',
      });
      await result.current.mutateAsync({
        clienteId: 'cli-1',
        destinatario: 'cobranza@cliente.mx',
        pdf: archivo,
        nombreArchivo: 'estado-cuenta.pdf',
      });
    });
    expect(cuerpo).toContain('name="destinatario"');
    expect(cuerpo).toContain('cobranza@cliente.mx');
    // El contenido del Blob de jsdom no viaja por el FormData de undici; en el
    // navegador sí. Aquí basta con la parte "pdf" tipada como application/pdf.
    expect(cuerpo).toMatch(/name="pdf"; filename="[^"]+"\r?\nContent-Type: application\/pdf/);
    expect(llaves).toHaveLength(2);
    expect(llaves[0]).not.toBe('');
    expect(llaves[0]).not.toBe(llaves[1]);
  });
});
