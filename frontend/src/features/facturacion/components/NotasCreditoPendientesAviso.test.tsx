import { fireEvent, render, screen, within } from '@testing-library/react';
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
    expect(within(aviso).getByText(/No se pudo timbrar el comprobante/)).toBeInTheDocument();
    expect(within(aviso).getByRole('button', { name: /Reintentar timbrado/ })).toBeEnabled();
  });

  it('el diálogo de descartar dice que la factura queda vigente con su saldo completo', async () => {
    render(<NotasCreditoPendientesAviso notas={[pendiente]} />, { wrapper: createQueryWrapper() });
    fireEvent.click(screen.getByRole('button', { name: /Descartar/ }));
    const dialogo = await screen.findByRole('alertdialog');
    expect(dialogo).toHaveTextContent('La factura queda vigente sin esta nota de crédito');
    expect(dialogo).toHaveTextContent('su saldo por cobrar sigue completo');
    expect(dialogo).not.toHaveTextContent('vuelve a quedar facturable');
  });

  it('no muestra nada si no hay notas pendientes', () => {
    const { container } = render(<NotasCreditoPendientesAviso notas={[]} />, { wrapper: createQueryWrapper() });
    expect(container).toBeEmptyDOMElement();
  });
});
