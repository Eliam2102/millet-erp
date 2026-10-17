import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ProveedorCuentasBancariasSection } from '@/modules/datos-maestros/components/ProveedorCuentasBancariasSection';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

const base = {
  swift: null,
  moneda: 'MXN',
  titular: 'Proveedor Uno SA',
  predeterminada: false,
  capturadaPorNombre: 'Ana',
  capturadaEn: '2026-10-01T10:00:00Z',
  validadaPorNombre: null,
  validadaEn: null,
  documentoAdjuntoId: null,
  notaValidacion: null,
  motivoEstado: null,
  numeroCompleto: false,
  esClabe: true,
  version: 1,
};

const CUENTAS = [
  { ...base, id: 'c-1', banco: 'BBVA México', numero: '**************1234', estado: 1, predeterminada: true, capturadaPorMi: false },
  { ...base, id: 'c-2', banco: 'Santander', numero: '**************5678', estado: 0, capturadaPorMi: false },
  { ...base, id: 'c-3', banco: 'Banorte', numero: '**************9012', estado: 0, capturadaPorMi: true },
];

const ADJUNTOS = [
  {
    id: 'a-1',
    tipoDocumentoCodigo: 'estado_cuenta_bancaria',
    nombreArchivo: 'edo-cuenta.pdf',
    vigenteHasta: '2027-01-01',
    estado: 1,
  },
];

function setPermisos(permisos: string[]) {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'a@b.com', nombre: 'Admin' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos,
    errorMessage: null,
  });
}

const ADMIN = [
  PermisosCanonicos.DatosMaestrosProveedoresBancariosVer,
  PermisosCanonicos.DatosMaestrosProveedoresCuentasAdministrar,
  PermisosCanonicos.DatosMaestrosProveedoresAdjuntosVer,
];

beforeEach(() => {
  mswServer.use(
    http.get('*/api/v1/datos-maestros/proveedores/p-1/cuentas-bancarias', () =>
      HttpResponse.json(CUENTAS),
    ),
    http.get('*/api/v1/datos-maestros/proveedores/p-1/adjuntos', () =>
      HttpResponse.json(ADJUNTOS),
    ),
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

function renderSection() {
  return render(<ProveedorCuentasBancariasSection proveedorId="p-1" razonSocial="Proveedor Uno SA" />, {
    wrapper: createQueryWrapper(),
  });
}

describe('<ProveedorCuentasBancariasSection>', () => {
  it('no renderiza nada sin bancarios-ver', () => {
    setPermisos([]);
    const { container } = renderSection();
    expect(container).toBeEmptyDOMElement();
  });

  it('lista cuentas con número enmascarado y badges de estado', async () => {
    setPermisos([PermisosCanonicos.DatosMaestrosProveedoresBancariosVer]);
    renderSection();
    await screen.findByText('BBVA México');
    expect(screen.getByText('**************1234')).toBeInTheDocument();
    expect(screen.getByText('Activa')).toBeInTheDocument();
    expect(screen.getAllByText('Por validar')).toHaveLength(2);
    expect(screen.queryByRole('button', { name: 'Validar' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /agregar cuenta/i })).not.toBeInTheDocument();
  });

  it('deshabilita Validar con motivo si la capturó el usuario actual', async () => {
    setPermisos(ADMIN);
    renderSection();
    await screen.findByText('Banorte');
    const botones = screen.getAllByRole('button', { name: 'Validar' });
    expect(botones[0]).toBeEnabled();
    expect(botones[1]).toBeDisabled();
    expect(screen.getByText(/tú la capturaste; pide a otro administrador\./i)).toBeInTheDocument();
  });

  it('deshabilita Validar hasta marcar los 4 cotejos y elegir documento', async () => {
    setPermisos(ADMIN);
    renderSection();
    await screen.findByText('Santander');
    fireEvent.click(screen.getAllByRole('button', { name: 'Validar' })[0]);

    const dialog = await screen.findByRole('dialog');
    const validar = within(dialog).getByRole('button', { name: 'Validar' });
    expect(validar).toBeDisabled();

    await within(dialog).findByRole('option', { name: /edo-cuenta\.pdf/ });
    fireEvent.change(within(dialog).getByLabelText('Estado de cuenta'), {
      target: { value: 'a-1' },
    });
    expect(validar).toBeDisabled();

    const checks = within(dialog).getAllByRole('checkbox');
    expect(checks).toHaveLength(4);
    checks.slice(0, 3).forEach((c) => fireEvent.click(c));
    expect(validar).toBeDisabled();
    fireEvent.click(checks[3]);
    await waitFor(() => expect(validar).toBeEnabled());
  });

  it('Hacer predeterminada exige cuentas-administrar: bancarios-editar solo no lo ve', async () => {
    const activas = [
      { ...base, id: 'c-1', banco: 'BBVA México', numero: '**************1234', estado: 1, predeterminada: true, capturadaPorMi: false },
      { ...base, id: 'c-5', banco: 'HSBC', numero: '**************7777', estado: 1, capturadaPorMi: false },
    ];
    mswServer.use(
      http.get('*/api/v1/datos-maestros/proveedores/p-1/cuentas-bancarias', () => HttpResponse.json(activas)),
    );
    setPermisos([
      PermisosCanonicos.DatosMaestrosProveedoresBancariosVer,
      PermisosCanonicos.DatosMaestrosProveedoresBancariosEditar,
    ]);
    const { unmount } = renderSection();
    await screen.findByText('HSBC');
    expect(screen.queryByRole('button', { name: 'Hacer predeterminada' })).not.toBeInTheDocument();
    unmount();

    setPermisos(ADMIN);
    renderSection();
    await screen.findByText('HSBC');
    expect(screen.getByRole('button', { name: 'Hacer predeterminada' })).toBeInTheDocument();
  });

  it('muestra quién, cuándo y por qué se rechazó o inhabilitó, y el motivo de la migrada', async () => {
    const cuentas = [
      { ...base, id: 'c-6', banco: 'Scotiabank', numero: '**************1111', estado: 3, capturadaPorMi: false,
        motivoEstado: 'CLABE de otro titular', ultimoCambioPorNombre: 'Laura Admin', ultimoCambioEn: '2026-10-05T10:00:00Z' },
      { ...base, id: 'c-7', banco: 'Inbursa', numero: '**************2222', estado: 2, capturadaPorMi: false,
        motivoEstado: 'Cambio de banco', ultimoCambioPorNombre: 'Pedro Admin', ultimoCambioEn: '2026-10-06T10:00:00Z' },
      { ...base, id: 'c-8', banco: 'Banamex', numero: '**************3333', estado: 1, capturadaPorMi: false,
        motivoEstado: 'Migrada del dato bancario único del proveedor.' },
    ];
    mswServer.use(
      http.get('*/api/v1/datos-maestros/proveedores/p-1/cuentas-bancarias', () => HttpResponse.json(cuentas)),
    );
    setPermisos([PermisosCanonicos.DatosMaestrosProveedoresBancariosVer]);
    renderSection();
    await screen.findByText('Scotiabank');
    expect(screen.getByText(/Rechazada por Laura Admin · /)).toBeInTheDocument();
    expect(screen.getByText('CLABE de otro titular')).toBeInTheDocument();
    expect(screen.getByText(/Inhabilitada por Pedro Admin · /)).toBeInTheDocument();
    expect(screen.getByText('Cambio de banco')).toBeInTheDocument();
    expect(screen.getByText('Migrada del dato bancario único del proveedor.')).toBeInTheDocument();
  });

  it('el SWIFT es opcional en una cuenta extranjera y si se captura debe tener 8 u 11 caracteres', async () => {
    setPermisos([
      PermisosCanonicos.DatosMaestrosProveedoresBancariosVer,
      PermisosCanonicos.DatosMaestrosProveedoresBancariosEditar,
    ]);
    renderSection();
    fireEvent.click(await screen.findByRole('button', { name: /agregar cuenta/i }));
    const dialog = await screen.findByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('Banco'), { target: { value: 'Deutsche Bank' } });
    fireEvent.change(within(dialog).getByLabelText('Tipo de cuenta'), { target: { value: 'extranjera' } });
    fireEvent.change(within(dialog).getByLabelText('Número de cuenta'), { target: { value: 'DE89370400440532013000' } });
    const agregar = within(dialog).getByRole('button', { name: 'Agregar cuenta' });
    await waitFor(() => expect(agregar).toBeEnabled());

    fireEvent.change(within(dialog).getByLabelText(/SWIFT/), { target: { value: 'ABC' } });
    await waitFor(() => expect(agregar).toBeDisabled());
    fireEvent.change(within(dialog).getByLabelText(/SWIFT/), { target: { value: 'DEUTDEFF' } });
    await waitFor(() => expect(agregar).toBeEnabled());
  });
});
