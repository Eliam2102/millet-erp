import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { NuevoPagoACuentaSheet } from './NuevoPagoACuentaSheet';

vi.mock('@/features/tesoreria/api/useTesoreria', () => ({
  useCuentasBancarias: () => ({
    data: [{ id: 'cuenta-ficticia', banco: 'Banco ficticio', numeroCuenta: '••••1234', moneda: 'MXN', saldo: 987654 }],
    isLoading: false,
    isError: false,
  }),
  useCuentasParaIngresos: () => ({ data: [], isLoading: false, isError: false }),
  useRegistrarPagoACuenta: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));
vi.mock('@/components/erp/selectors/ProveedorSelector', () => ({ ProveedorSelector: () => null }));
vi.mock('./ConceptoSelector', () => ({ ConceptoSelector: () => null }));

describe('<NuevoPagoACuentaSheet> cuenta de egreso', () => {
  it('no muestra el saldo en libros al abrir el selector', () => {
    render(<NuevoPagoACuentaSheet open onOpenChange={vi.fn()} />);
    const combo = screen.getAllByRole('combobox').find((c) => /cuenta/i.test(c.getAttribute('aria-label') ?? ''));
    fireEvent.keyDown(combo!, { key: 'ArrowDown' });
    expect(screen.getByRole('option', { name: /Banco ficticio.*1234/ })).toBeInTheDocument();
    expect(screen.queryByText(/en libros/)).not.toBeInTheDocument();
  });
});
