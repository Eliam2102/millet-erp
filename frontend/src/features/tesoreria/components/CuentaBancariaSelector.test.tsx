import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { CuentaBancariaSelector } from './CuentaBancariaSelector';

const mocks = vi.hoisted(() => ({ saldos: vi.fn(), ingresos: vi.fn() }));
vi.mock('@/features/tesoreria/api/useTesoreria', () => ({
  useCuentasBancarias: mocks.saldos,
  useCuentasParaIngresos: mocks.ingresos,
}));

const cuenta = { id: 'cuenta-ficticia', banco: 'Banco ficticio', numeroCuenta: '••••1234', moneda: 'MXN' };

describe('Cuenta bancaria para ingresos de Tesorería operativa', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.saldos.mockReturnValue({ data: [{ ...cuenta, saldo: 987654 }], isLoading: false, isError: false });
    mocks.ingresos.mockReturnValue({ data: [cuenta], isLoading: false, isError: false });
  });

  it('consulta el catálogo para ingresos sin activar la consulta de saldos', () => {
    render(<CuentaBancariaSelector paraIngresos value={null} onChange={vi.fn()} />);
    expect(mocks.saldos).toHaveBeenCalledWith(true, false);
    expect(mocks.ingresos).toHaveBeenCalledWith(true);
    fireEvent.keyDown(screen.getByRole('combobox', { name: 'Cuenta bancaria' }), { key: 'ArrowDown' });
    expect(screen.getByRole('option', { name: /Banco ficticio.*1234.*MXN/ })).toBeInTheDocument();
    expect(screen.queryByText(/987,654/)).not.toBeInTheDocument();
  });

  it('conserva la consulta de saldos para los selectores existentes', () => {
    render(<CuentaBancariaSelector value={null} onChange={vi.fn()} />);
    expect(mocks.saldos).toHaveBeenCalledWith(true, true);
    expect(mocks.ingresos).toHaveBeenCalledWith(false);
  });

  it('muestra el saldo en libros por omisión y lo oculta con ocultarSaldo', () => {
    const abrir = () =>
      fireEvent.keyDown(screen.getByRole('combobox', { name: 'Cuenta bancaria' }), { key: 'ArrowDown' });
    const { unmount } = render(<CuentaBancariaSelector value={null} onChange={vi.fn()} />);
    abrir();
    expect(screen.getByText(/en libros/)).toBeInTheDocument();
    unmount();
    render(<CuentaBancariaSelector value={null} onChange={vi.fn()} ocultarSaldo />);
    abrir();
    expect(screen.queryByText(/en libros/)).not.toBeInTheDocument();
    expect(screen.getByRole('option', { name: /Banco ficticio.*1234.*MXN/ })).toBeInTheDocument();
  });
});
