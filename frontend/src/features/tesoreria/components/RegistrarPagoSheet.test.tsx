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

describe('<RegistrarPagoSheet> cuenta del proveedor y fondos', () => {
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

  it('deshabilita Registrar sin casilla de fondos y envía cuenta y fondos', async () => {
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
    expect(boton).toBeDisabled();
    expect(screen.getByText('Confirma que verificaste fondos en la banca')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('checkbox', { name: /Verifiqué en la banca/ }));
    expect(boton).toBeEnabled();
    fireEvent.click(boton);
    await waitFor(() => expect(body).not.toBeNull());
    expect(body).toMatchObject({ cuentaProveedorId: 'b', fondosVerificados: true });
  });

  it('sin cuenta activa muestra el callout y deshabilita', async () => {
    cuentasMock([cuenta({ estado: 'Inhabilitada' })]);
    abrir();
    expect(await screen.findByText(/Este proveedor no tiene una cuenta activa en MXN/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Registrar pago' })).toBeDisabled();
  });
});
