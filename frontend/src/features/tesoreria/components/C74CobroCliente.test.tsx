import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

// F1-CXC-03 · C7.4 · CA7.9: el ingreso registrado como «Cobro de cliente» (cliente
// opcional) es el depósito sin identificar que CxC verá en su bandeja.

const mocks = vi.hoisted(() => ({ mutate: vi.fn() }));
vi.mock('../api/useTesoreria', () => ({
  useRegistrarMovimientoIngreso: () => ({ mutate: mocks.mutate, isPending: false }),
}));
vi.mock('@/lib/api', () => ({ esApiError: () => false, useBodyScopedIdempotencyKey: () => () => 'c74-key' }));
vi.mock('@/features/compras/ordenes/api/useSucursalesPermitidas', () => ({
  useSucursalesPermitidas: () => ({
    isLoading: false,
    data: [{ id: '22222222-2222-4222-8222-222222222222', clave: 'MID', nombre: 'Mérida' }],
  }),
}));
vi.mock('./CuentaBancariaSelector', () => ({
  CuentaBancariaSelector: ({ onChange }: { onChange: (v: string) => void }) => (
    <button type="button" onClick={() => onChange('11111111-1111-4111-8111-111111111111')}>
      elegir cuenta
    </button>
  ),
}));
vi.mock('./ConceptoSelector', () => ({ ConceptoSelector: () => <span>concepto</span> }));
vi.mock('./ClienteTesoreriaSelector', () => ({
  ClienteTesoreriaSelector: () => <span>selector de cliente</span>,
}));

import { useAuthStore } from '@/lib/auth/auth-store';
import { RegistrarIngresoSheet } from './RegistrarIngresoSheet';

describe('Alta de ingreso · cobro de cliente (C7.4)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useAuthStore.setState({ currentSucursalId: '22222222-2222-4222-8222-222222222222' });
  });

  it('marca el ingreso con beneficiario Cliente sin exigir el cliente', () => {
    render(<RegistrarIngresoSheet open onOpenChange={() => {}} />);
    fireEvent.click(screen.getByRole('button', { name: 'elegir cuenta' }));
    fireEvent.change(screen.getByLabelText('Importe *'), { target: { value: '50000' } });

    expect(screen.queryByText('selector de cliente')).not.toBeInTheDocument();
    fireEvent.click(screen.getByLabelText('Cobro de cliente (cliente opcional)'));
    expect(screen.getByText('selector de cliente')).toBeInTheDocument();
    expect(screen.getByText(/queda «sin identificar»/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Registrar ingreso' }));

    expect(mocks.mutate).toHaveBeenCalledTimes(1);
    const { command } = mocks.mutate.mock.calls[0][0];
    expect(command.beneficiarioTipo).toBe(2);
    expect(command.beneficiarioRef).toBeUndefined();
    expect(command.monto).toBe(50_000);
  });

  it('el selector de sucursal muestra el nombre de la sucursal activa, no su GUID', () => {
    render(<RegistrarIngresoSheet open onOpenChange={() => {}} />);
    const sucursal = screen.getByRole('combobox', { name: 'Sucursal del ingreso' });
    expect(sucursal).toHaveTextContent('MID · Mérida');
    expect(sucursal).not.toHaveTextContent('22222222');
  });

  it('sin la opción el ingreso no lleva beneficiario', () => {
    render(<RegistrarIngresoSheet open onOpenChange={() => {}} />);
    fireEvent.click(screen.getByRole('button', { name: 'elegir cuenta' }));
    fireEvent.change(screen.getByLabelText('Importe *'), { target: { value: '100' } });
    fireEvent.click(screen.getByRole('button', { name: 'Registrar ingreso' }));
    expect(mocks.mutate.mock.calls[0][0].command.beneficiarioTipo).toBeUndefined();
  });

  it('86e3tfdve: captura la cuenta ordenante normalizada y bloquea un formato fuera del SAT', () => {
    render(<RegistrarIngresoSheet open onOpenChange={() => {}} />);
    fireEvent.click(screen.getByRole('button', { name: 'elegir cuenta' }));
    fireEvent.change(screen.getByLabelText('Importe *'), { target: { value: '100' } });
    expect(screen.queryByLabelText('Cuenta ordenante del cliente')).not.toBeInTheDocument();
    fireEvent.click(screen.getByLabelText('Cobro de cliente (cliente opcional)'));

    fireEvent.change(screen.getByLabelText('Cuenta ordenante del cliente'), { target: { value: '000000012' } });
    expect(screen.getByText(/debe tener 10, 11, 15, 16 o 18 dígitos/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Registrar ingreso' })).toBeDisabled();

    fireEvent.change(screen.getByLabelText('Cuenta ordenante del cliente'), {
      target: { value: '0000-0000 0000-0016' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Registrar ingreso' }));
    expect(mocks.mutate.mock.calls[0][0].command.cuentaOrdenante).toBe('0000000000000016');
  });
});

