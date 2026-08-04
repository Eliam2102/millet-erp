import { describe, expect, it } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import {
  useAnticiposCliente,
  useAntiguedadSaldos,
  useEstadoCuentaCliente,
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
