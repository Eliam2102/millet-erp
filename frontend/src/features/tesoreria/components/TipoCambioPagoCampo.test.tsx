import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { errorTipoCambioPago } from '@/features/tesoreria/lib/tipo-cambio-pago';
import { TipoCambioPagoCampo } from './TipoCambioPagoCampo';

vi.mock('@/features/tesoreria/api/useTesoreria', () => ({
  useTipoCambioPago: (moneda: string | null, fecha: string) => ({
    data: moneda != null && fecha !== '' ? { moneda, fecha, valorEnMxn: 18.42 } : undefined,
    isFetching: false,
    isError: false,
  }),
}));

describe('<TipoCambioPagoCampo> (TES-09)', () => {
  it('cobro sin ingreso elegido: pide elegir el ingreso en vez de decir que el catálogo no tiene TC', () => {
    render(
      <TipoCambioPagoCampo
        id="tc"
        etiqueta="TC del cobro"
        ayudaSinFecha="Elige el ingreso para ver el TC del catálogo."
        moneda="USD"
        fecha=""
        value={null}
        onChange={vi.fn()}
      />,
    );
    expect(screen.getByText('Elige el ingreso para ver el TC del catálogo.')).toBeInTheDocument();
    expect(screen.queryByText(/El catálogo no tiene TC/)).not.toBeInTheDocument();
  });

  it('con fecha muestra el TC del catálogo', () => {
    render(<TipoCambioPagoCampo id="tc" moneda="USD" fecha="2026-11-06" value={null} onChange={vi.fn()} />);
    expect(screen.getByLabelText(/TC del pago/)).toHaveValue(18.42);
    expect(screen.getByText(/Catálogo: 18.42 MXN por USD/)).toBeInTheDocument();
  });

  it('el error usa la etiqueta del campo', () => {
    render(
      <TipoCambioPagoCampo
        id="tc"
        etiqueta="TC del cobro"
        moneda="USD"
        fecha="2026-11-06"
        value="18.1234567"
        onChange={vi.fn()}
      />,
    );
    expect(screen.getByText('El TC del cobro admite hasta 6 decimales')).toBeInTheDocument();
    expect(errorTipoCambioPago('USD', '0', 'TC del cobro')).toBe('El TC del cobro debe ser mayor a cero');
    expect(errorTipoCambioPago('USD', '0')).toBe('El TC del pago debe ser mayor a cero');
  });
});
