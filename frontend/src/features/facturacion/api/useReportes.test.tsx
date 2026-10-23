import { describe, expect, it } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import {
  useLiquidacionCaja,
  useEstadosAnticipos,
  useComparativaAnticipos,
  useMonitorCfdi,
  useConciliacionFacturadoIngresado,
  useVerificarEstatusSat,
} from '@/features/facturacion/api/useReportes';
import { EstadoComparativaAnticipo } from '@/features/facturacion/api/types';

function reporteStub(titulo: string) {
  return {
    titulo,
    generadoEn: '2026-05-30T10:00:00Z',
    filtrosAplicados: {},
    columnas: [
      { clave: 'concepto', etiqueta: 'Concepto', tipo: 'texto' },
      { clave: 'monto', etiqueta: 'Monto', tipo: 'moneda' },
    ],
    filas: [{ concepto: 'Efectivo', monto: 500 }],
    totales: { monto: 500 },
  };
}

describe('useLiquidacionCaja', () => {
  it('manda desde/hasta y devuelve el reporte', async () => {
    let urlVista = '';
    mswServer.use(
      http.get('*/api/v1/facturacion/reportes/liquidacion-caja', ({ request }) => {
        urlVista = request.url;
        return HttpResponse.json(reporteStub('Liquidación de caja'));
      }),
    );
    const { result } = renderHook(
      () => useLiquidacionCaja({ desde: '2026-05-01', hasta: '2026-05-30' }),
      { wrapper: createQueryWrapper() },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.filas).toHaveLength(1);
    expect(urlVista).toContain('desde=');
    expect(urlVista).toContain('hasta=');
  });

  it('no dispara sin rango de fechas', () => {
    const { result } = renderHook(
      () => useLiquidacionCaja({ desde: '', hasta: '' }),
      { wrapper: createQueryWrapper() },
    );
    expect(result.current.fetchStatus).toBe('idle');
  });
});

describe('useEstadosAnticipos', () => {
  it('devuelve el reporte', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/reportes/estados-anticipos', () =>
        HttpResponse.json(reporteStub('Estados de anticipos')),
      ),
    );
    const { result } = renderHook(() => useEstadosAnticipos({}), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.totales?.monto).toBe(500);
  });
});

describe('useComparativaAnticipos', () => {
  it('manda filtros con periodo en hora de México y estado numérico', async () => {
    let urlVista = '';
    mswServer.use(
      http.get(
        '*/api/v1/facturacion/reportes/comparativa-anticipos',
        ({ request }) => {
          urlVista = request.url;
          return HttpResponse.json(reporteStub('Comparativa de anticipos'));
        },
      ),
    );
    const { result } = renderHook(
      () =>
        useComparativaAnticipos({
          sucursalId: 'suc-1',
          clienteId: 'cli-1',
          desde: '2026-10-01',
          hasta: '2026-10-31',
          corte: '2026-10-15',
          estado: EstadoComparativaAnticipo.Parcial,
        }),
      { wrapper: createQueryWrapper() },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    const params = new URL(urlVista).searchParams;
    expect(params.get('sucursalId')).toBe('suc-1');
    expect(params.get('clienteId')).toBe('cli-1');
    // Inicio del día en America/Mexico_City (UTC-6) → 06:00Z.
    expect(params.get('desde')).toBe('2026-10-01T06:00:00.000Z');
    // Fin del día local → 05:59:59.999Z del día siguiente.
    expect(params.get('hasta')).toBe('2026-11-01T05:59:59.999Z');
    expect(params.get('corte')).toBe('2026-10-16T05:59:59.999Z');
    expect(params.get('estado')).toBe('2');
    expect(result.current.data?.totales?.monto).toBe(500);
  });

  it('sin filtros llama la ruta sin query string', async () => {
    let urlVista = '';
    mswServer.use(
      http.get(
        '*/api/v1/facturacion/reportes/comparativa-anticipos',
        ({ request }) => {
          urlVista = request.url;
          return HttpResponse.json(reporteStub('Comparativa de anticipos'));
        },
      ),
    );
    const { result } = renderHook(() => useComparativaAnticipos({}), {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(new URL(urlVista).search).toBe('');
  });
});

// F6.7 — Cierre del día por sucursal (monitor de CFDI + facturado vs ingresado).
const SUC = '11111111-1111-4111-8111-111111111111';
const CAJERO = '22222222-2222-4222-8222-222222222222';

function monitorStub() {
  return {
    sucursal: 'Matriz',
    dia: '2026-10-21',
    cajero: null,
    fuenteSat: 'Simulado (adaptador de prueba)',
    resumen: {
      timbrados: 3,
      cancelados: 1,
      cancelacionEnTramite: 0,
      sinTimbrar: 1,
      fallidos: 1,
      pedidosSinTimbrar: 2,
      sinVerificarSat: 4,
      discrepancias: 0,
      totalVigente: 1500,
    },
    comprobantes: reporteStub('Monitor de CFDI'),
    pedidosSinTimbrar: reporteStub('Pedidos sin timbrar'),
  };
}

describe('useMonitorCfdi', () => {
  it('manda sucursalId, dia y cajeroId', async () => {
    let urlVista = '';
    mswServer.use(
      http.get('*/api/v1/facturacion/reportes/monitor-cfdi', ({ request }) => {
        urlVista = request.url;
        return HttpResponse.json(monitorStub());
      }),
    );
    const { result } = renderHook(
      () => useMonitorCfdi({ sucursalId: SUC, dia: '2026-10-21', cajeroId: CAJERO }),
      { wrapper: createQueryWrapper() },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    const params = new URL(urlVista).searchParams;
    expect(params.get('sucursalId')).toBe(SUC);
    expect(params.get('dia')).toBe('2026-10-21');
    expect(params.get('cajeroId')).toBe(CAJERO);
    expect(result.current.data?.resumen.timbrados).toBe(3);
  });

  it('no dispara sin sucursal', () => {
    const { result } = renderHook(() => useMonitorCfdi({ dia: '2026-10-21' }), {
      wrapper: createQueryWrapper(),
    });
    expect(result.current.fetchStatus).toBe('idle');
  });
});

describe('useConciliacionFacturadoIngresado', () => {
  it('manda sucursalId y dia sin cajero cuando no se elige', async () => {
    let urlVista = '';
    mswServer.use(
      http.get(
        '*/api/v1/facturacion/reportes/conciliacion-facturado-ingresado',
        ({ request }) => {
          urlVista = request.url;
          return HttpResponse.json({
            sucursal: 'Matriz',
            dia: '2026-10-21',
            cajero: null,
            resumen: {
              facturadoContado: 1000,
              facturasContado: 2,
              notasCredito: 0,
              porCobrar: 1000,
              ingresado: 1000,
              ingresadoEfectivo: 1000,
              ingresadoTpv: 0,
              ingresadoTransferencia: 0,
              ingresadoOtras: 0,
              diferencia: 0,
              cuadra: true,
              facturadoCredito: 0,
              facturasCredito: 0,
            },
            porCajero: reporteStub('Por cajero'),
            partidas: reporteStub('Partidas'),
          });
        },
      ),
    );
    const { result } = renderHook(
      () => useConciliacionFacturadoIngresado({ sucursalId: SUC, dia: '2026-10-21' }),
      { wrapper: createQueryWrapper() },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    const params = new URL(urlVista).searchParams;
    expect(params.get('sucursalId')).toBe(SUC);
    expect(params.get('dia')).toBe('2026-10-21');
    expect(params.has('cajeroId')).toBe(false);
    expect(result.current.data?.resumen.cuadra).toBe(true);
  });

  it('no dispara sin sucursal', () => {
    const { result } = renderHook(
      () => useConciliacionFacturadoIngresado({ dia: '2026-10-21' }),
      { wrapper: createQueryWrapper() },
    );
    expect(result.current.fetchStatus).toBe('idle');
  });
});

describe('useVerificarEstatusSat', () => {
  it('hace POST con Idempotency-Key, sucursal y día', async () => {
    let urlVista = '';
    let llave: string | null = null;
    let metodo = '';
    mswServer.use(
      http.post(
        '*/api/v1/facturacion/reportes/monitor-cfdi/verificar-sat',
        ({ request }) => {
          urlVista = request.url;
          metodo = request.method;
          llave = request.headers.get('Idempotency-Key');
          return HttpResponse.json({
            fuente: 'Simulado (adaptador de prueba)',
            consultados: 3,
            vigentes: 2,
            cancelados: 1,
            noEncontrados: 0,
            errores: 0,
            pendientes: 0,
          });
        },
      ),
    );
    const { result } = renderHook(() => useVerificarEstatusSat(), {
      wrapper: createQueryWrapper(),
    });
    result.current.mutate({ sucursalId: SUC, dia: '2026-10-21', idempotencyKey: 'idem-sat-1' });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(metodo).toBe('POST');
    expect(llave).toBe('idem-sat-1');
    const params = new URL(urlVista).searchParams;
    expect(params.get('sucursalId')).toBe(SUC);
    expect(params.get('dia')).toBe('2026-10-21');
    expect(result.current.data?.consultados).toBe(3);
  });
});
