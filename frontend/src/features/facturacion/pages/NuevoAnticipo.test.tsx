import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { NuevoAnticipo } from './NuevoAnticipo';

vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: vi.fn(), warning: vi.fn() } }));

// Selectores de catálogo stubbeados: la prueba valida de dónde sale la
// sucursal (86e3tfd80), no los combobox de catálogos.
vi.mock('@/components/erp', () => ({
  SucursalSelector: ({
    value,
    onChange,
    disabled,
  }: {
    value: string | null;
    onChange: (id: string | null) => void;
    disabled?: boolean;
  }) => (
    <input
      aria-label="Sucursal (stub)"
      value={value ?? ''}
      disabled={disabled}
      onChange={(e) => onChange(e.target.value || null)}
    />
  ),
  UsoCfdiSelector: () => <input aria-label="Uso CFDI (stub)" />,
  FormaPagoSelector: () => <input aria-label="Forma de pago (stub)" />,
}));
vi.mock('@/components/erp/selectors/MonedaSelector', () => ({
  MonedaSelector: () => <input aria-label="Moneda (stub)" />,
}));
vi.mock('@/features/facturacion/components/selectors/ClienteSelector', () => ({
  ClienteSelector: ({ onChange }: { onChange: (item: unknown) => void }) => (
    <button
      type="button"
      onClick={() =>
        onChange({
          id: '44444444-4444-4444-8444-444444444444',
          razonSocial: 'CLIENTE DEMO',
          rfc: 'CDE010101AB1',
          regimenFiscal: '601',
          codigoPostalFiscal: '76120',
        })
      }
    >
      Elegir cliente
    </button>
  ),
}));
vi.mock('../components/vista-previa/VistaPreviaButton', () => ({ VistaPreviaButton: () => null }));
vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => vi.fn(),
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => <a href={to}>{children}</a>,
}));

const SUCURSAL_DEFAULT = '33333333-3333-4333-8333-333333333333';
const SUCURSAL_CAJA = '55555555-5555-4555-8555-555555555555';

const EMISOR = {
  rfcEmisor: 'MIL010101ABC',
  razonSocialEmisor: 'Millet SA de CV',
  regimenFiscalEmisor: '601',
  sucursalIdDefault: SUCURSAL_DEFAULT,
  tasaIvaDefault: null,
  codigoPostalEmisor: '76120',
};

function sesion(estado: string, diaAnteriorPendiente = false) {
  return {
    sesion: {
      id: 's-1',
      cajaId: 'c-1',
      cajaNombre: 'Caja 1 Mostrador',
      sucursalId: SUCURSAL_CAJA,
      estado,
      version: 1,
    },
    diaAnteriorPendiente,
  };
}

function setPermisos(permisos: string[]) {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos,
    errorMessage: null,
  });
}

let llamadasSesion = 0;
let respuestaSesion: Record<string, unknown> = sesion('Abierta');

beforeEach(() => {
  llamadasSesion = 0;
  respuestaSesion = sesion('Abierta');
  setPermisos([PermisosCanonicos.FacturacionAnticiposEmitir, PermisosCanonicos.FacturacionCajaOperar]);
  mswServer.use(
    http.get('*/api/v1/facturacion/emisor-defaults', () => HttpResponse.json(EMISOR)),
    http.get('*/api/v1/facturacion/cajas/sesion-actual', () => {
      llamadasSesion += 1;
      return HttpResponse.json(respuestaSesion);
    }),
  );
});

function renderizar() {
  return render(<NuevoAnticipo onClose={vi.fn()} />, { wrapper: createQueryWrapper() });
}

describe('NuevoAnticipo · sucursal de la caja abierta (86e3tfd80)', () => {
  it('con caja abierta precarga la sucursal de la caja y la bloquea', async () => {
    renderizar();
    const sucursal = await screen.findByLabelText('Sucursal (stub)');
    expect(sucursal).toHaveValue(SUCURSAL_CAJA);
    expect(sucursal).toBeDisabled();
    expect(screen.getByText(/Tomada de tu caja abierta: Caja 1 Mostrador/)).toBeInTheDocument();
  });

  it.each([
    ['sin sesión', { sesion: null, diaAnteriorPendiente: false }],
    ['caja en arqueo', sesion('EnArqueo')],
    ['día anterior pendiente', sesion('Abierta', true)],
  ])('%s: usa la sucursal por defecto, editable y con aviso', async (_caso, respuesta) => {
    respuestaSesion = respuesta;
    renderizar();
    const sucursal = await screen.findByLabelText('Sucursal (stub)');
    expect(sucursal).toHaveValue(SUCURSAL_DEFAULT);
    expect(sucursal).toBeEnabled();
    expect(screen.getByText(/No tienes caja abierta; elige la sucursal/)).toBeInTheDocument();
  });

  it('sin permiso de caja no consulta la sesión y deja elegir la sucursal', async () => {
    setPermisos([PermisosCanonicos.FacturacionAnticiposEmitir]);
    renderizar();
    const sucursal = await screen.findByLabelText('Sucursal (stub)');
    expect(sucursal).toHaveValue(SUCURSAL_DEFAULT);
    expect(sucursal).toBeEnabled();
    expect(llamadasSesion).toBe(0);
  });

  it('si falla la consulta de la sesión deja elegir la sucursal', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/cajas/sesion-actual', () =>
        HttpResponse.json({ title: 'error' }, { status: 500 }),
      ),
    );
    renderizar();
    const sucursal = await screen.findByLabelText('Sucursal (stub)');
    expect(sucursal).toHaveValue(SUCURSAL_DEFAULT);
    expect(sucursal).toBeEnabled();
  });

  it('muestra uso de CFDI, forma de pago y moneda como desplegables', async () => {
    renderizar();
    await screen.findByLabelText('Sucursal (stub)');
    expect(screen.getByLabelText('Uso CFDI (stub)')).toBeInTheDocument();
    expect(screen.getByLabelText('Forma de pago (stub)')).toBeInTheDocument();
    expect(screen.getByLabelText('Moneda (stub)')).toBeInTheDocument();
  });

  it('emite el anticipo con la sucursal de la caja', async () => {
    let cuerpo: Record<string, unknown> | null = null;
    const emitir = async ({ request }: { request: Request }) => {
      cuerpo = (await request.json()) as Record<string, unknown>;
      return HttpResponse.json({ id: 'a-1', folio: 'FANT 1', estado: 'Timbrada', uuid: 'u', saldo: 116 });
    };
    mswServer.use(
      http.post('*/api/v1/facturacion/anticipos', emitir),
      http.post('*/api/v1/facturacion/anticipos/', emitir),
    );
    const { container } = renderizar();
    await screen.findByLabelText('Sucursal (stub)');
    fireEvent.click(screen.getByRole('button', { name: 'Elegir cliente' }));
    fireEvent.change(container.querySelector('input[name="total"]')!, {
      target: { value: '1160' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Emitir y timbrar' }));
    await waitFor(() => expect(cuerpo).not.toBeNull());
    expect(cuerpo!.sucursalId).toBe(SUCURSAL_CAJA);
    // 86e3tfd7j: se manda el total capturado; el backend desglosa.
    expect(cuerpo!.total).toBe(1160);
    expect(cuerpo!.montoBase).toBe(0);
  });

  it('86e3tfd6n: manda el número de pedido de A+W cobrado antes de que el pedido llegue', async () => {
    let cuerpo: Record<string, unknown> | null = null;
    const emitir = async ({ request }: { request: Request }) => {
      cuerpo = (await request.json()) as Record<string, unknown>;
      return HttpResponse.json({ id: 'a-1', folio: 'FANT 1', estado: 'Timbrada', uuid: 'u', saldo: 116 });
    };
    mswServer.use(
      http.post('*/api/v1/facturacion/anticipos', emitir),
      http.post('*/api/v1/facturacion/anticipos/', emitir),
    );
    const { container } = renderizar();
    await screen.findByLabelText('Sucursal (stub)');
    fireEvent.click(screen.getByRole('button', { name: 'Elegir cliente' }));
    fireEvent.change(container.querySelector('input[name="total"]')!, {
      target: { value: '1160' },
    });
    fireEvent.change(screen.getByLabelText(/Número de pedido A\+W/), {
      target: { value: ' 10425068 ' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Emitir y timbrar' }));
    await waitFor(() => expect(cuerpo).not.toBeNull());
    expect(cuerpo!.pedidoOrigenRef).toBe('10425068');
  });
});

describe('NuevoAnticipo · captura por total (86e3tfd7j)', () => {
  it('muestra subtotal e IVA calculados del total capturado', async () => {
    const { container } = renderizar();
    await screen.findByLabelText('Sucursal (stub)');
    expect(screen.getByText(/Total \(con IVA\)/)).toBeInTheDocument();
    fireEvent.change(container.querySelector('input[name="total"]')!, {
      target: { value: '1160' },
    });
    await waitFor(() => expect(screen.getAllByText(/1,000\.00/).length).toBeGreaterThan(0));
    expect(screen.getAllByText(/160\.00/).length).toBeGreaterThan(0);
    expect(screen.getAllByText(/1,160\.00/).length).toBeGreaterThan(0);
  });
});
