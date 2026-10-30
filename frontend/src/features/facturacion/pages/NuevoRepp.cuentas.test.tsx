import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { useAuthStore } from '@/lib/auth/auth-store';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { NuevoRepp } from './NuevoRepp';

/**
 * 86e3tfdve — cuentas del pago en «Nuevo REPP»: se habilitan según la forma
 * de pago (c_FormaPago) y se validan con el mismo formato que el backend.
 * Cuentas ficticias.
 */
vi.mock('@/features/facturacion/api/useFacturas', () => ({
  useEmisorDefaults: () => ({
    data: { rfcEmisor: 'AAA010101AAA', razonSocialEmisor: 'Empresa', regimenFiscalEmisor: '601' },
  }),
}));
vi.mock('@/components/erp', () => ({ SucursalSelector: () => <span>Sucursal</span> }));

function sesion(permisos: string[]) {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u', email: 'u@test.local', nombre: 'Prueba' },
    currentEmpresaId: 'e',
    empresas: [],
    permisos,
    errorMessage: null,
  });
}

afterEach(() =>
  useAuthStore.setState({ status: 'idle', accessToken: null, permisos: [], currentEmpresaId: null }),
);

function montar() {
  // Catálogo c_FormaPago del selector (subconjunto ficticio del seed).
  mswServer.use(
    http.get('*/api/v1/catalogos/formas-pago', () =>
      HttpResponse.json([
        { id: 'fp-01', claveSat: '01', descripcion: 'Efectivo', activa: true },
        { id: 'fp-03', claveSat: '03', descripcion: 'Transferencia electrónica de fondos', activa: true },
        { id: 'fp-06', claveSat: '06', descripcion: 'Dinero electrónico', activa: true },
      ]),
    ),
  );
  render(<NuevoRepp onClose={() => {}} />, { wrapper: createQueryWrapper() });
}

/** Elige una forma de pago en el selector del catálogo, como lo haría el usuario. */
async function elegirFormaPago(descripcion: string) {
  fireEvent.click(screen.getByRole('combobox', { name: 'Seleccionar forma de pago' }));
  fireEvent.click(await screen.findByText(descripcion));
}

describe('<NuevoRepp> — cuentas del pago', () => {
  it('con transferencia (03) ambas cuentas están habilitadas', () => {
    sesion(['facturacion.repp.emitir']);
    montar();
    expect(screen.getByLabelText('Cuenta ordenante')).toBeEnabled();
    expect(screen.getByLabelText('Cuenta beneficiaria')).toBeEnabled();
  });

  it('con efectivo (01) deshabilita ambas cuentas y explica el motivo', async () => {
    sesion(['facturacion.repp.emitir']);
    montar();
    await elegirFormaPago('Efectivo');
    await waitFor(() => expect(screen.getByLabelText('Cuenta ordenante')).toBeDisabled());
    expect(screen.getByLabelText('Cuenta beneficiaria')).toBeDisabled();
    expect(screen.getAllByText(/La forma de pago 01 no admite/).length).toBeGreaterThan(0);
  });

  it('elegir transferencia (03) en el selector vuelve a habilitar las cuentas', async () => {
    sesion(['facturacion.repp.emitir']);
    montar();
    await elegirFormaPago('Efectivo');
    await waitFor(() => expect(screen.getByLabelText('Cuenta ordenante')).toBeDisabled());
    expect(screen.getByLabelText('Cuenta beneficiaria')).toBeDisabled();

    await elegirFormaPago('Transferencia electrónica de fondos');
    await waitFor(() => expect(screen.getByLabelText('Cuenta ordenante')).toBeEnabled());
    expect(screen.getByLabelText('Cuenta beneficiaria')).toBeEnabled();
    expect(
      screen.getByRole('combobox', { name: 'Seleccionar forma de pago' }),
    ).toHaveTextContent('03 · Transferencia electrónica de fondos');
  });

  it('con dinero electrónico (06) solo admite la ordenante', async () => {
    sesion(['facturacion.repp.emitir']);
    montar();
    await elegirFormaPago('Dinero electrónico');
    await waitFor(() => expect(screen.getByLabelText('Cuenta beneficiaria')).toBeDisabled());
    expect(screen.getByLabelText('Cuenta ordenante')).toBeEnabled();
  });

  it('muestra el formato esperado cuando la ordenante no cumple', async () => {
    sesion(['facturacion.repp.emitir']);
    montar();
    fireEvent.change(screen.getByLabelText('Cuenta ordenante'), {
      target: { value: '000000000012' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Emitir y timbrar' }));
    expect(await screen.findByText(/se esperan 10, 16 o 18 dígitos/)).toBeInTheDocument();
  });

  it('con permiso de cuentas ofrece las cuentas de Millet enmascaradas', async () => {
    sesion(['facturacion.repp.emitir', 'tesoreria.cuentas.ver']);
    mswServer.use(
      http.get('*/api/v1/tesoreria/cuentas', () =>
        HttpResponse.json([
          {
            id: 'cta-1',
            banco: 'Banco ficticio',
            numeroCuenta: '******0010',
            moneda: 'MXN',
            saldo: 0,
            activa: true,
          },
        ]),
      ),
    );
    montar();
    expect(
      await screen.findByRole('combobox', { name: 'Cuenta beneficiaria' }),
    ).toBeInTheDocument();
  });
});
