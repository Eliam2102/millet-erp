import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import type { PasivoPendienteResponse } from '@/features/tesoreria/api/types';
import { RegistrarPagoSheet } from './RegistrarPagoSheet';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children }: { children: React.ReactNode }) => <a href="#">{children}</a>,
}));
vi.mock('./CuentaBancariaSelector', () => ({
  CuentaBancariaSelector: ({ onChange }: { onChange: (id: string) => void }) => (
    <button type="button" onClick={() => onChange('egreso-1')}>
      elegir egreso
    </button>
  ),
}));
vi.mock('./ConceptoSelector', () => ({ ConceptoSelector: () => null }));

const pasivo: PasivoPendienteResponse = {
  facturaProveedorId: 'fac-1',
  proveedorId: 'prov-1',
  proveedorClave: 'P1',
  proveedorRazonSocial: 'Proveedor Ficticio',
  banco: null,
  clabe: null,
  beneficiario: null,
  ordenCompraId: null,
  montoTotal: 100,
  saldoPendiente: 100,
  moneda: 'MXN',
  tipoCambio: null,
  fechaVencimiento: '2026-12-01',
  uuidCfdi: null,
  folioProveedor: 'F-1',
  metodoPago: null,
  recibidoEn: '2026-10-01T00:00:00Z',
  pagoACuentaAbiertoMovimientoId: null,
};

const cuenta = (o: object) => ({
  id: 'c', banco: 'Banco', numero: '••••1234', esClabe: true, swift: null,
  moneda: 'MXN', titular: null, predeterminada: false, estado: 'Activa', ...o,
});

function cuentasMock(cuentas: object[]) {
  mswServer.use(
    http.get('*/api/v1/tesoreria/proveedores/prov-1/cuentas', () => HttpResponse.json(cuentas)),
  );
}

function abrir() {
  render(
    <RegistrarPagoSheet pasivos={[pasivo]} open onOpenChange={vi.fn()} onSuccess={vi.fn()} />,
    { wrapper: createQueryWrapper() },
  );
}

describe('<RegistrarPagoSheet> cuenta del proveedor', () => {
  it('preselecciona la predeterminada activa y deshabilita las no activas', async () => {
    cuentasMock([
      cuenta({ id: 'a', numero: '••••1111' }),
      cuenta({ id: 'b', numero: '••••2222', predeterminada: true }),
      cuenta({ id: 'c', numero: '••••3333', estado: 'PorValidar' }),
      cuenta({ id: 'd', numero: '••••4444', moneda: 'USD' }),
    ]);
    abrir();
    const radios = await screen.findAllByRole('radio');
    expect(radios).toHaveLength(4);
    expect(radios[1]).toBeChecked();
    expect(radios[2]).toBeDisabled();
    expect(radios[3]).toBeDisabled();
  });

  it('habilita Registrar sin confirmar fondos y envía la cuenta sin fondosVerificados', async () => {
    cuentasMock([cuenta({ id: 'b', predeterminada: true })]);
    let body: Record<string, unknown> | null = null;
    mswServer.use(
      http.post('*/api/v1/tesoreria/pagos', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json({ monto: 100, moneda: 'MXN', aplicaciones: [{}] });
      }),
    );
    abrir();
    await screen.findAllByRole('radio');
    fireEvent.click(screen.getByText('elegir egreso'));
    const boton = screen.getByRole('button', { name: 'Registrar pago' });
    expect(boton).toBeDisabled(); // falta la referencia del banco
    fireEvent.change(screen.getByLabelText(/Referencia bancaria/), { target: { value: ' spei-1 ' } });
    expect(screen.queryByRole('checkbox', { name: /Verifiqué en la banca/ })).not.toBeInTheDocument();
    expect(boton).toBeEnabled();
    fireEvent.click(boton);
    await waitFor(() => expect(body).not.toBeNull());
    expect(body).toMatchObject({ referenciaBancaria: 'spei-1', cuentaProveedorId: 'b', conceptoId: '0000000b-1001-0000-0000-000000000001' });
    expect(body).not.toHaveProperty('fondosVerificados');
  });

  it('sin cuenta activa muestra el callout y deshabilita', async () => {
    cuentasMock([cuenta({ estado: 'Inhabilitada' })]);
    abrir();
    expect(await screen.findByText(/Este proveedor no tiene una cuenta activa en MXN/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Registrar pago' })).toBeDisabled();
  });

  it('pasivo interno: no pide cuenta de proveedor, hace POST sin cuentaProveedorId', async () => {
    let body: Record<string, unknown> | null = null;
    mswServer.use(
      http.post('*/api/v1/tesoreria/pagos', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json({ monto: 100, moneda: 'MXN', aplicaciones: [{}] });
      }),
    );
    render(
      <RegistrarPagoSheet
        pasivos={[{ ...pasivo, tipoBeneficiario: 'Empleado' }]}
        open
        onOpenChange={vi.fn()}
        onSuccess={vi.fn()}
      />,
      { wrapper: createQueryWrapper() },
    );
    fireEvent.click(screen.getByText('elegir egreso'));
    const boton = screen.getByRole('button', { name: 'Registrar pago' });
    expect(boton).toBeDisabled(); // falta la referencia del banco
    fireEvent.change(screen.getByLabelText(/Referencia bancaria/), { target: { value: ' spei-1 ' } });
    expect(screen.queryByRole('checkbox', { name: /Verifiqué en la banca/ })).not.toBeInTheDocument();
    expect(boton).toBeEnabled();
    fireEvent.click(boton);
    await waitFor(() => expect(body).not.toBeNull());
    expect(body).not.toHaveProperty('fondosVerificados');
    expect(body).not.toHaveProperty('cuentaProveedorId');
  });
});

describe('<RegistrarPagoSheet> TC del pago (TES-09)', () => {
  const pasivoUsd: PasivoPendienteResponse = { ...pasivo, moneda: 'USD', tipoCambio: 18 };

  function abrirUsd() {
    cuentasMock([cuenta({ id: 'u', moneda: 'USD', predeterminada: true })]);
    mswServer.use(
      http.get('*/api/v1/tesoreria/tipo-cambio-pago', ({ request }) => {
        const url = new URL(request.url);
        return HttpResponse.json({ moneda: url.searchParams.get('moneda'), fecha: url.searchParams.get('fecha'), valorEnMxn: 18.5 });
      }),
    );
    render(
      <RegistrarPagoSheet pasivos={[pasivoUsd]} open onOpenChange={vi.fn()} onSuccess={vi.fn()} />,
      { wrapper: createQueryWrapper() },
    );
  }

  function capturarPost() {
    const capturado: { body: Record<string, unknown> | null } = { body: null };
    mswServer.use(
      http.post('*/api/v1/tesoreria/pagos', async ({ request }) => {
        capturado.body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json({ monto: 100, moneda: 'USD', aplicaciones: [{}] });
      }),
    );
    return capturado;
  }

  it('en USD muestra el TC del catálogo y, sin cambiarlo, no lo manda (el backend usa ese mismo)', async () => {
    const post = capturarPost();
    abrirUsd();
    const campo = await screen.findByLabelText(/TC del pago/);
    await waitFor(() => expect(campo).toHaveValue(18.5));
    fireEvent.click(screen.getByText('elegir egreso'));
    fireEvent.change(screen.getByLabelText(/Referencia bancaria/), { target: { value: 'spei-usd' } });
    fireEvent.click(screen.getByRole('button', { name: 'Registrar pago' }));
    await waitFor(() => expect(post.body).not.toBeNull());
    expect(post.body).not.toHaveProperty('tipoCambioPago');
  });

  it('en USD manda el TC que aplicó el banco cuando se cambia', async () => {
    const post = capturarPost();
    abrirUsd();
    const campo = await screen.findByLabelText(/TC del pago/);
    await waitFor(() => expect(campo).toHaveValue(18.5));
    fireEvent.change(campo, { target: { value: '18.6123' } });
    fireEvent.click(screen.getByText('elegir egreso'));
    fireEvent.change(screen.getByLabelText(/Referencia bancaria/), { target: { value: 'spei-usd' } });
    fireEvent.click(screen.getByRole('button', { name: 'Registrar pago' }));
    await waitFor(() => expect(post.body).not.toBeNull());
    expect(post.body).toMatchObject({ tipoCambioPago: 18.6123 });
  });

  it('un TC en cero deshabilita el registro', async () => {
    abrirUsd();
    const campo = await screen.findByLabelText(/TC del pago/);
    fireEvent.change(campo, { target: { value: '0' } });
    fireEvent.click(screen.getByText('elegir egreso'));
    fireEvent.change(screen.getByLabelText(/Referencia bancaria/), { target: { value: 'spei-usd' } });
    expect(screen.getByRole('button', { name: 'Registrar pago' })).toBeDisabled();
    expect(screen.getAllByText('El TC del pago debe ser mayor a cero').length).toBeGreaterThan(0);
  });

  it('en MXN no muestra el campo', async () => {
    cuentasMock([cuenta({ id: 'b', predeterminada: true })]);
    abrir();
    await screen.findAllByRole('radio');
    expect(screen.queryByLabelText(/TC del pago/)).not.toBeInTheDocument();
  });
});
