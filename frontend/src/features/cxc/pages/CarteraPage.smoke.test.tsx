import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { AlineacionColumnaCxc, TipoColumnaReporteCxc } from '@/features/cxc/api/types';
import { useAuthStore } from '@/lib/auth/auth-store';

const searchMock = vi.hoisted(() => ({ current: {} as Record<string, unknown> }));

const pdfMock = vi.hoisted(() => ({
  generar: vi.fn(async () => new Blob(['%PDF-1.4 prueba'], { type: 'application/pdf' })),
  descargar: vi.fn(),
}));

vi.mock('@/components/erp/reportes/exporters/reporte-pdf-exporter', () => ({
  generarReportePdfBlob: pdfMock.generar,
  descargarBlob: pdfMock.descargar,
  descargarReportePdf: vi.fn(),
  nombreArchivoPdf: (nombre: string) => `${nombre}.pdf`,
}));

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
  useNavigate: () => () => {},
  useSearch: () => searchMock.current,
}));

import { CarteraPage } from '@/features/cxc/pages/CarteraPage';
import { AnticiposCxcPage } from '@/features/cxc/pages/AnticiposCxcPage';
import { EstadoCuentaPage } from '@/features/cxc/pages/EstadoCuentaPage';

const BASE = '*/api/v1/cuentas-por-cobrar';

beforeEach(() => {
  searchMock.current = {};
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: ['cuentas_por_cobrar.cartera.leer', 'cuentas_por_cobrar.lineas-credito.leer'],
    errorMessage: null,
  });
  mswServer.use(http.get(`${BASE}/clientes-lookup`, () => HttpResponse.json([])));
});

afterEach(() => {
  useAuthStore.setState({
    status: 'idle',
    accessToken: null,
    expiresAt: null,
    user: null,
    empresas: [],
    currentEmpresaId: null,
    permisos: [],
    errorMessage: null,
  });
});

describe('<CarteraPage> — smoke', () => {
  it('renderiza el reporte con buckets dinámicos del backend', async () => {
    mswServer.use(
      http.get(`${BASE}/cartera/antiguedad`, () =>
        HttpResponse.json({
          titulo: 'Antigüedad de saldos',
          generadoEn: '2026-07-14T12:00:00Z',
          filtrosAplicados: [{ label: 'Fecha de corte', valor: '2026-07-14' }],
          columnas: [
            {
              key: 'cliente',
              label: 'Cliente',
              tipo: TipoColumnaReporteCxc.Texto,
              alineacion: AlineacionColumnaCxc.Izquierda,
            },
            {
              key: 'bucket_16_30',
              label: '16-30 días',
              tipo: TipoColumnaReporteCxc.Moneda,
              alineacion: AlineacionColumnaCxc.Derecha,
            },
          ],
          filas: [{ cliente: 'ACME SA', bucket_16_30: 1234.5 }],
          totales: { bucket_16_30: 1234.5 },
        }),
      ),
    );
    render(<CarteraPage />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('16-30 días')).toBeInTheDocument();
    expect(screen.getByText('ACME SA')).toBeInTheDocument();
  });

  it('estado error', async () => {
    mswServer.use(
      http.get(`${BASE}/cartera/antiguedad`, () =>
        HttpResponse.json(
          { type: 'about:blank', title: 'Boom', status: 500 },
          { status: 500, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    render(<CarteraPage />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText(/No se pudo cargar el reporte/i)).toBeInTheDocument();
  });
});

const reporteEstadoCuenta = {
  titulo: 'Estado de cuenta',
  generadoEn: '2026-07-14T12:00:00Z',
  filtrosAplicados: [],
  columnas: [
    {
      key: 'movimiento',
      label: 'Movimiento',
      tipo: TipoColumnaReporteCxc.Texto,
      alineacion: AlineacionColumnaCxc.Izquierda,
    },
  ],
  filas: [{ movimiento: 'Factura F-100' }],
  totales: null,
};

function sinPendientes(emailCliente: string | null = null) {
  return http.get(`${BASE}/cartera/estado-cuenta/cli-1/pendientes`, () =>
    HttpResponse.json({ emitible: true, pendientes: [], recibosSinReflejar: [], emailCliente }),
  );
}

describe('<EstadoCuentaPage> — smoke', () => {
  beforeEach(() => {
    mswServer.use(
      http.get(`${BASE}/cartera/estado-cuenta/cli-1/envios`, () => HttpResponse.json([])),
    );
  });

  it('sin cliente: CTA de selección', async () => {
    render(<EstadoCuentaPage />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText(/Elige un cliente para generar su estado/i)).toBeInTheDocument();
  });

  it('con cliente: renderiza el reporte', async () => {
    searchMock.current = { clienteId: 'cli-1' };
    mswServer.use(
      sinPendientes(),
      http.get(`${BASE}/cartera/estado-cuenta/cli-1`, () => HttpResponse.json(reporteEstadoCuenta)),
    );
    render(<EstadoCuentaPage />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('Factura F-100')).toBeInTheDocument();
  });

  it('C7.8 · CA7.14: con pagos sin REP muestra el bloqueo y no permite enviar', async () => {
    searchMock.current = { clienteId: 'cli-1' };
    useAuthStore.setState({
      permisos: ['cuentas_por_cobrar.cartera.leer', 'cuentas_por_cobrar.cobranza.registrar'],
    });
    mswServer.use(
      http.get(`${BASE}/cartera/estado-cuenta/cli-1`, () =>
        HttpResponse.json(
          {
            title: 'Regla de negocio',
            status: 422,
            code: 'ESTADO_CUENTA_REPP_PENDIENTE',
            detail: 'Hay pagos confirmados pendientes de REP.',
          },
          { status: 422, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
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
    render(<EstadoCuentaPage />, { wrapper: createQueryWrapper() });
    expect(
      await screen.findByText(/No se puede emitir: hay pagos aplicados sin REP/i),
    ).toBeInTheDocument();
    expect(await screen.findByText('SPEI 123')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /REP pendientes/i })).toHaveAttribute(
      'href',
      '/facturacion/repp/pendientes',
    );
    expect(screen.queryByRole('button', { name: /Emitir y enviar/i })).not.toBeInTheDocument();
    expect(screen.queryByText(/No se pudo cargar el reporte/i)).not.toBeInTheDocument();
  });

  it('C7.8-a: sin pendientes, «Emitir y enviar» genera el PDF y registra el envío', async () => {
    searchMock.current = { clienteId: 'cli-1' };
    useAuthStore.setState({
      permisos: ['cuentas_por_cobrar.cartera.leer', 'cuentas_por_cobrar.cobranza.registrar'],
    });
    let cuerpo = '';
    let llave: string | null = null;
    mswServer.use(
      sinPendientes('cobranza@cliente.mx'),
      http.get(`${BASE}/cartera/estado-cuenta/cli-1`, () => HttpResponse.json(reporteEstadoCuenta)),
      http.post(`${BASE}/cartera/estado-cuenta/cli-1/envios`, async ({ request }) => {
        llave = request.headers.get('Idempotency-Key');
        // jsdom y undici no comparten la clase File: se inspecciona el multipart crudo.
        cuerpo = await request.text();
        return HttpResponse.json({ id: 'env-1', estado: 'Enviado' }, { status: 201 });
      }),
    );
    render(<EstadoCuentaPage />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('Factura F-100')).toBeInTheDocument();

    fireEvent.click(await screen.findByRole('button', { name: /Emitir y enviar/i }));
    const correo = await screen.findByLabelText(/Destinatario/i);
    await waitFor(() => expect(correo).toHaveValue('cobranza@cliente.mx'));
    fireEvent.click(screen.getByRole('button', { name: /^Enviar$/i }));

    await waitFor(() => expect(cuerpo).toContain('cobranza@cliente.mx'));
    expect(cuerpo).toContain('name="pdf"');
    expect(llave).toBeTruthy();
    expect(pdfMock.generar).toHaveBeenCalled();
    expect(pdfMock.descargar).toHaveBeenCalled();
  });
});

describe('<AnticiposCxcPage> — smoke', () => {
  it('sin cliente: CTA de selección', async () => {
    render(<AnticiposCxcPage />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText(/Elige un cliente para ver sus anticipos/i)).toBeInTheDocument();
  });

  it('con cliente: tabla + totales por moneda (nunca cross-divisa)', async () => {
    searchMock.current = { clienteId: 'cli-1' };
    mswServer.use(
      http.get(`${BASE}/anticipos`, () =>
        HttpResponse.json([
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
          {
            anticipoId: 'ant-2',
            clienteId: 'cli-1',
            estado: 'Abierto',
            montoCobrado: 5000,
            montoAmortizado: 0,
            saldo: 5000,
            saldoDisponible: 5000,
            moneda: 'USD',
            pedidoOrigenRef: null,
          },
        ]),
      ),
    );
    render(<AnticiposCxcPage />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('3000123456')).toBeInTheDocument();
    // Totales separados por divisa.
    expect(screen.getByText(/Saldo MXN/i)).toBeInTheDocument();
    expect(screen.getByText(/Saldo USD/i)).toBeInTheDocument();
  });

  it('con cliente sin anticipos: empty', async () => {
    searchMock.current = { clienteId: 'cli-1' };
    mswServer.use(http.get(`${BASE}/anticipos`, () => HttpResponse.json([])));
    render(<AnticiposCxcPage />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText(/El cliente no tiene anticipos/i)).toBeInTheDocument();
  });
});
