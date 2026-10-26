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

const aplicacionesAnticipo = {
  anticipos: [
    {
      anticipoId: 'ant-1',
      facturaAnticipoId: 'fa-1',
      clienteId: 'cli-1',
      clienteNombre: 'Cliente Maquila',
      folio: 'FA-10',
      uuid: 'uuid-fa-10',
      fechaTimbrado: '2026-10-01T16:00:00Z',
      sucursalId: 'suc-1',
      sucursalNombre: 'MID — Mérida',
      moneda: 'MXN',
      estado: 'Parcial',
      cobrado: 30000,
      aplicado: 20000,
      comprometido: 0,
      saldo: 10000,
      saldoSistema: 10000,
      pedidoOrigenRef: '3000123456',
      observacion: null,
      aplicaciones: [
        {
          tipo: 'Aplicación',
          facturaFinalFolio: 'FV-200',
          facturaFinalUuid: 'uuid-fv-200',
          egresoFolio: 'NC-7',
          egresoUuid: 'uuid-nc-7',
          fechaAplicacion: '2026-10-05T16:00:00Z',
          estadoEgreso: 'Timbrado',
          importe: 20000,
        },
      ],
    },
    {
      anticipoId: 'ant-2',
      facturaAnticipoId: 'fa-2',
      clienteId: 'cli-2',
      clienteNombre: 'Vidrios del Golfo',
      folio: 'FA-11',
      uuid: 'uuid-fa-11',
      fechaTimbrado: '2026-10-02T16:00:00Z',
      sucursalId: 'suc-1',
      sucursalNombre: 'MID — Mérida',
      moneda: 'USD',
      estado: 'Pendiente',
      cobrado: 5000,
      aplicado: 0,
      comprometido: 0,
      saldo: 5000,
      saldoSistema: 4000,
      pedidoOrigenRef: null,
      observacion: 'Saldo en sistema $4,000.00',
      aplicaciones: [],
    },
  ],
  totalesPorMoneda: [
    { moneda: 'MXN', cobrado: 30000, aplicado: 20000, comprometido: 0, saldo: 10000 },
    { moneda: 'USD', cobrado: 5000, aplicado: 0, comprometido: 0, saldo: 5000 },
  ],
};

describe('<AnticiposCxcPage> — smoke', () => {
  it('sin cliente: lista los anticipos de todos los clientes con KPIs por moneda', async () => {
    let params: URLSearchParams | null = null;
    mswServer.use(
      http.get(`${BASE}/anticipos/aplicaciones`, ({ request }) => {
        params = new URL(request.url).searchParams;
        return HttpResponse.json(aplicacionesAnticipo);
      }),
    );
    render(<AnticiposCxcPage />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('FA-10')).toBeInTheDocument();
    expect(params!.has('clienteId')).toBe(false);
    // Columna Cliente visible sin filtro de cliente.
    expect(screen.getByText('Cliente Maquila')).toBeInTheDocument();
    expect(screen.getByText('Vidrios del Golfo')).toBeInTheDocument();
    expect(screen.getByText('3000123456')).toBeInTheDocument();
    expect(screen.getByText('Saldo en sistema $4,000.00')).toBeInTheDocument();
    // KPIs separados por divisa (nunca cross-divisa).
    expect(screen.getByText('Cobrado MXN')).toBeInTheDocument();
    expect(screen.getByText('Aplicado MXN')).toBeInTheDocument();
    expect(screen.getByText('Saldo MXN')).toBeInTheDocument();
    expect(screen.getByText('Saldo USD')).toBeInTheDocument();
  });

  it('la fila expandible muestra la factura final y el egreso 07', async () => {
    mswServer.use(
      http.get(`${BASE}/anticipos/aplicaciones`, () => HttpResponse.json(aplicacionesAnticipo)),
    );
    render(<AnticiposCxcPage />, { wrapper: createQueryWrapper() });
    const expandir = await screen.findByRole('button', { name: /Ver aplicaciones de FA-10/i });
    expect(expandir).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByText('FV-200')).not.toBeInTheDocument();

    fireEvent.click(expandir);

    expect(expandir).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByText('Aplicaciones a factura final')).toBeInTheDocument();
    expect(screen.getByText('FV-200')).toBeInTheDocument();
    expect(screen.getByText('NC-7')).toBeInTheDocument();
    expect(screen.getByText('uuid-nc-7')).toBeInTheDocument();
    expect(screen.getByText('Timbrado')).toBeInTheDocument();
  });

  it('anticipo sin aplicaciones: estado vacío al expandir', async () => {
    mswServer.use(
      http.get(`${BASE}/anticipos/aplicaciones`, () => HttpResponse.json(aplicacionesAnticipo)),
    );
    render(<AnticiposCxcPage />, { wrapper: createQueryWrapper() });
    fireEvent.click(await screen.findByRole('button', { name: /Ver aplicaciones de FA-11/i }));
    expect(screen.getByText('Sin aplicaciones')).toBeInTheDocument();
  });

  it('tabs por estado filtran las filas', async () => {
    mswServer.use(
      http.get(`${BASE}/anticipos/aplicaciones`, () => HttpResponse.json(aplicacionesAnticipo)),
    );
    render(<AnticiposCxcPage />, { wrapper: createQueryWrapper() });
    await screen.findByText('FA-10');
    fireEvent.mouseDown(screen.getByRole('tab', { name: /Parcial/i }));
    fireEvent.click(screen.getByRole('tab', { name: /Parcial/i }));
    await waitFor(() => expect(screen.queryByText('FA-11')).not.toBeInTheDocument());
    expect(screen.getByText('FA-10')).toBeInTheDocument();
  });

  it('con cliente: manda el clienteId, oculta la columna Cliente y muestra vacío', async () => {
    searchMock.current = { clienteId: 'cli-1' };
    let params: URLSearchParams | null = null;
    mswServer.use(
      http.get(`${BASE}/anticipos/aplicaciones`, ({ request }) => {
        params = new URL(request.url).searchParams;
        return HttpResponse.json({ anticipos: [], totalesPorMoneda: [] });
      }),
    );
    render(<AnticiposCxcPage />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText(/El cliente no tiene anticipos/i)).toBeInTheDocument();
    expect(params!.get('clienteId')).toBe('cli-1');
  });

  it('enlace a la comparativa de Facturación solo con facturacion.reportes.leer', async () => {
    mswServer.use(
      http.get(`${BASE}/anticipos/aplicaciones`, () => HttpResponse.json(aplicacionesAnticipo)),
    );
    const { unmount } = render(<AnticiposCxcPage />, { wrapper: createQueryWrapper() });
    await screen.findByText('FA-10');
    expect(screen.queryByText(/Ver comparativa en Facturación/i)).not.toBeInTheDocument();
    unmount();

    useAuthStore.setState({
      permisos: ['cuentas_por_cobrar.cartera.leer', 'facturacion.reportes.leer'],
    });
    render(<AnticiposCxcPage />, { wrapper: createQueryWrapper() });
    const enlace = await screen.findByText(/Ver comparativa en Facturación/i);
    expect(enlace.closest('a')).toHaveAttribute(
      'href',
      '/facturacion/reportes/comparativa-anticipos',
    );
  });
});
