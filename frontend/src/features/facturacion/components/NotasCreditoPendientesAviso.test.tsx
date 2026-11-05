import { render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { createQueryWrapper } from '@/test/test-query-client';
import { NotasCreditoPendientesAviso } from './NotasCreditoPendientesAviso';

vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => true }));

const pendiente = {
  id: 'nc-1',
  folio: 'NC-FIX-1',
  motivo: 'Ranura',
  total: 580,
  estado: 'TimbradoFallido',
  timbradoErrorCodigo: 'PAC-99',
  timbradoErrorMensaje: 'PAC caído (FIX)',
  version: 1,
};

describe('U-1 · aviso «nota de crédito pendiente» en la factura', () => {
  it('muestra la NC pendiente con su motivo y el botón de reintento', () => {
    render(<NotasCreditoPendientesAviso notas={[pendiente]} />, { wrapper: createQueryWrapper() });
    const aviso = screen.getByRole('region', { name: 'Nota de crédito pendiente' });
    expect(within(aviso).getByRole('note')).toHaveTextContent('Nota de crédito pendiente.');
    expect(within(aviso).getByText('NC-FIX-1')).toBeInTheDocument();
    expect(within(aviso).getByText(/PAC caído \(FIX\)/)).toBeInTheDocument();
    expect(within(aviso).getByRole('button', { name: /Reintentar timbrado/ })).toBeEnabled();
  });

  it('no muestra nada si no hay notas pendientes', () => {
    const { container } = render(<NotasCreditoPendientesAviso notas={[]} />, { wrapper: createQueryWrapper() });
    expect(container).toBeEmptyDOMElement();
  });
});
