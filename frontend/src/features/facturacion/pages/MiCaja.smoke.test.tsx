import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { MiCaja } from '@/features/facturacion/pages/MiCaja';
import { useAuthStore } from '@/lib/auth/auth-store';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
  useNavigate: () => () => {},
  useSearch: () => ({}),
}));

const BASE = '*/api/v1/facturacion/cajas';

const sesionAbierta = {
  id: 'ses-1',
  cajaId: 'c-1',
  cajaNombre: 'Caja Mostrador Conkal',
  sucursalId: 's-1',
  responsableUsuarioId: 'u-test',
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
  version: 1,
  cortes: [],
  movimientos: [
    {
      id: 'm-1',
      tipo: 'FondoApertura',
      formaPago: '01',
      importe: 500,
      moneda: 'MXN',
      descripcion: 'Fondo de apertura',
      referencia: null,
      cobroMostradorId: null,
      usuarioId: 'u-test',
      createdAt: '2026-07-11T14:00:00Z',
    },
  ],
  totalesPorForma: [{ formaPago: '01', montoSistema: 500, montoDeclarado: null }],
};

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: ['facturacion.caja.operar'],
    errorMessage: null,
  });
  mswServer.use(
    http.get('*/api/v1/facturacion/cobros', () => HttpResponse.json([])),
  );
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

describe('<MiCaja> — smoke', () => {
  it('19-oct #52 muestra totales por forma de 7510 en cobros más 500 de fondo', async () => {
    mswServer.use(http.get(`${BASE}/sesion-actual`, () => HttpResponse.json({
      sesion: { ...sesionAbierta, movimientos: [
        ...sesionAbierta.movimientos,
        ...[['01', 2000], ['04', 3000], ['03', 2510]].map(([formaPago, importe], i) => ({
          ...sesionAbierta.movimientos[0], id: `c-${i}`, tipo: 'CobroCliente',
          formaPago, importe, cobroMostradorId: `cobro-${i}`,
        })),
      ], totalesPorForma: [
        { formaPago: '01', montoSistema: 2500, montoDeclarado: null },
        { formaPago: '04', montoSistema: 3000, montoDeclarado: null },
        { formaPago: '03', montoSistema: 2510, montoDeclarado: null },
      ] }, diaAnteriorPendiente: false,
    })));
    render(<MiCaja />, { wrapper: createQueryWrapper() });
    const efectivo = (await screen.findByText('Efectivo en caja')).closest('.shadow-card')!;
    expect(within(efectivo as HTMLElement).getByText('$2,500.00')).toBeInTheDocument();
    const cobrado = screen.getByText('Cobrado en la sesión').closest('.shadow-card')!;
    expect(within(cobrado as HTMLElement).getByText('$7,510.00')).toBeInTheDocument();
    expect(screen.getByText('3 cobros')).toBeInTheDocument();
    expect(screen.getByText('Fondo $500.00 · cobros $2,000.00')).toBeInTheDocument();
    expect(screen.getByText('$3,000.00')).toBeInTheDocument();
    expect(screen.getByText('$2,510.00')).toBeInTheDocument();
  });

  it('en arqueo muestra el efectivo esperado del corte', async () => {
    mswServer.use(http.get(`${BASE}/sesion-actual`, () => HttpResponse.json({
      sesion: { ...sesionAbierta, estado: 'EnArqueo', cortes: [
        { formaPago: '01', montoSistema: 450, montoDeclarado: null },
      ] }, diaAnteriorPendiente: false,
    })));
    render(<MiCaja />, { wrapper: createQueryWrapper() });
    const tarjeta = (await screen.findByText('Efectivo esperado')).closest('.shadow-card')!;
    expect(within(tarjeta as HTMLElement).getByText('$450.00')).toBeInTheDocument();
    expect(screen.queryByText('Efectivo en caja')).not.toBeInTheDocument();
  });

  it('sin sesión: muestra el form de apertura', async () => {
    mswServer.use(
      http.get(`${BASE}/sesion-actual`, () =>
        HttpResponse.json({ sesion: null, diaAnteriorPendiente: false }),
      ),
      http.get(BASE, () =>
        HttpResponse.json([
          {
            id: 'c-1',
            nombre: 'Caja Mostrador Conkal',
            descripcion: null,
            estatus: 'Activo',
            sucursales: 1,
            canales: 1,
            usuarios: 1,
          },
        ]),
      ),
    );
    render(<MiCaja />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/Abrir sesión/i, { selector: 'h2' })).toBeInTheDocument(),
    );
    expect(screen.getByLabelText(/Fondo de apertura/i)).toBeInTheDocument();
  });

  it('con sesión abierta: totales, movimientos y acciones', async () => {
    mswServer.use(
      http.get(`${BASE}/sesion-actual`, () =>
        HttpResponse.json({ sesion: sesionAbierta, diaAnteriorPendiente: false }),
      ),
    );
    render(<MiCaja />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText('Caja Mostrador Conkal')).toBeInTheDocument(),
    );
    expect(screen.getByText('Abierta')).toBeInTheDocument();
    expect(screen.getByText(/Fondo de apertura/)).toBeInTheDocument(); // movimiento
    expect(screen.getByRole('button', { name: /Iniciar arqueo/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Liquidar ruta/i })).toBeInTheDocument(); // PR7
  });

  it('sesión de día anterior: banner de bloqueo §5.2', async () => {
    mswServer.use(
      http.get(`${BASE}/sesion-actual`, () =>
        HttpResponse.json({ sesion: sesionAbierta, diaAnteriorPendiente: true }),
      ),
    );
    render(<MiCaja />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/día anterior/i)).toBeInTheDocument(),
    );
    expect(screen.queryByRole('button', { name: /Registrar movimiento/i })).toBeNull();
  });

  it('sin permiso operar: no consulta la sesión (403) ni muestra acciones de operación', async () => {
    useAuthStore.setState({ permisos: ['facturacion.caja.supervisar'] });
    const consultada = vi.fn();
    mswServer.use(
      http.get(`${BASE}/sesion-actual`, () => {
        consultada();
        return HttpResponse.json({}, { status: 403 });
      }),
      http.get('*/api/v1/facturacion/cajas/autorizaciones-apertura', () => HttpResponse.json([])),
      http.get(BASE, () => HttpResponse.json([])),
    );
    render(<MiCaja />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('Mi caja')).toBeInTheDocument();
    await new Promise((r) => setTimeout(r, 50));
    expect(consultada).not.toHaveBeenCalled();
    expect(screen.queryByText(/No se pudo cargar tu sesión/i)).toBeNull();
    expect(screen.queryByRole('button', { name: /Abrir sesión/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /Iniciar arqueo/i })).toBeNull();
    expect(screen.queryByRole('button', { name: /Registrar movimiento/i })).toBeNull();
  });

  describe('86e3tfdwu · enlace «Cobrar en caja» con la caja sin abrir', () => {
    beforeEach(() => {
      window.history.pushState({}, '', '/facturacion/caja?cobrar=cmp-1&folio=FV-77');
    });
    afterEach(() => {
      window.history.pushState({}, '', '/');
    });

    it('caja en arqueo: avisa «La caja no está abierta» con el folio', async () => {
      mswServer.use(
        http.get(`${BASE}/sesion-actual`, () =>
          HttpResponse.json({
            sesion: { ...sesionAbierta, estado: 'EnArqueo', cortes: [] },
            diaAnteriorPendiente: false,
          }),
        ),
      );
      render(<MiCaja />, { wrapper: createQueryWrapper() });
      const aviso = (await screen.findByText(/La caja no está abierta/)).closest('[role="status"]');
      expect(aviso).toHaveTextContent('FV-77');
    });

    it('día anterior pendiente: también avisa', async () => {
      mswServer.use(
        http.get(`${BASE}/sesion-actual`, () =>
          HttpResponse.json({ sesion: sesionAbierta, diaAnteriorPendiente: true }),
        ),
      );
      render(<MiCaja />, { wrapper: createQueryWrapper() });
      expect(await screen.findByText(/La caja no está abierta/)).toBeInTheDocument();
    });

    it('caja abierta: no muestra el aviso', async () => {
      mswServer.use(
        http.get(`${BASE}/sesion-actual`, () =>
          HttpResponse.json({ sesion: sesionAbierta, diaAnteriorPendiente: false }),
        ),
        http.get(`${BASE}/cobrables`, () => HttpResponse.json([])),
      );
      render(<MiCaja />, { wrapper: createQueryWrapper() });
      expect(await screen.findByText('Caja Mostrador Conkal')).toBeInTheDocument();
      expect(screen.queryByText(/La caja no está abierta/)).toBeNull();
    });
  });
});

describe('86e3tfdvt · voucher en la liquidación de ruta', () => {
  it('marca en rojo los 16 dígitos de la tarjeta igual que el cobro y bloquea la liquidación', async () => {
    mswServer.use(
      http.get(`${BASE}/sesion-actual`, () =>
        HttpResponse.json({ sesion: sesionAbierta, diaAnteriorPendiente: false }),
      ),
      http.get('*/api/v1/catalogos/formas-pago', () =>
        HttpResponse.json([
          { id: '01', claveSat: '01', descripcion: 'Efectivo', activa: true },
          { id: '04', claveSat: '04', descripcion: 'Tarjeta de crédito', activa: true },
        ]),
      ),
      http.get('*/api/v1/facturacion/cobros/cobrables', () =>
        HttpResponse.json([
          {
            comprobanteId: 'cmp-ruta',
            folio: 'RUTA-1',
            tipo: 'Factura',
            receptorNombre: 'Cliente ficticio',
            total: 100,
            moneda: 'MXN',
            fechaTimbrado: '2026-07-11T15:00:00Z',
            montoAcreditado: 0,
            montoRanura: 0,
          },
        ]),
      ),
    );
    render(<MiCaja />, { wrapper: createQueryWrapper() });
    fireEvent.click(await screen.findByRole('button', { name: /Liquidar ruta/i }));
    fireEvent.click(await screen.findByRole('checkbox', { name: 'Incluir RUTA-1' }));
    fireEvent.keyDown(screen.getByRole('combobox', { name: 'Forma de pago de RUTA-1' }), {
      key: 'ArrowDown',
    });
    fireEvent.click(await screen.findByRole('option', { name: 'Tarjeta de crédito' }));

    const voucher = await screen.findByLabelText('Número de autorización del voucher de RUTA-1');
    const registrar = screen.getByRole('button', { name: 'Registrar liquidación' });
    expect(voucher).toHaveAttribute('aria-invalid', 'true');
    expect(registrar).toBeDisabled();

    fireEvent.change(voucher, { target: { value: '4111111111111111' } });
    expect(voucher).toHaveValue('4111111111111111');
    expect(voucher).toHaveAttribute('aria-invalid', 'true');
    expect(registrar).toBeDisabled();

    fireEvent.change(voucher, { target: { value: '123456' } });
    expect(voucher).toHaveAttribute('aria-invalid', 'false');
    await waitFor(() => expect(registrar).toBeEnabled());
  });
});
