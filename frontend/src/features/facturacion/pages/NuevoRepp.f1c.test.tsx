import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { createQueryWrapper } from '@/test/test-query-client';
import { NuevoRepp } from './NuevoRepp';
const emitir = vi.hoisted(() => vi.fn());
vi.mock('@/features/facturacion/api/useRepp', () => ({ useEmitirRepp: () => ({ mutate: emitir, isPending: false }) }));
vi.mock('@/features/facturacion/api/useFacturas', () => ({ useEmisorDefaults: () => ({ data: { rfcEmisor: 'AAA010101AAA', razonSocialEmisor: 'Empresa de pruebas', regimenFiscalEmisor: '601' } }) }));
vi.mock('@/components/erp', () => ({ SucursalSelector: () => <span>Sucursal</span> }));
vi.mock('@/features/facturacion/components/FacturaPpdPicker', () => ({
  FacturaPpdPicker: ({ onChange }: { onChange: (item: unknown) => void }) => <>
    <button type="button" onClick={() => onChange({ facturaVentaId: 'f1', receptorRfc: 'XAXX010101000', receptorNombre: 'Cliente', folio: 'FA-1', saldo: 8700, total: 8700, acreditadoNc: 0, pagadoRepp: 0, moneda: 'MXN', numParcialidadSiguiente: 1 })}>Primera parcialidad</button>
    <button type="button" onClick={() => onChange({ facturaVentaId: 'f1', receptorRfc: 'XAXX010101000', receptorNombre: 'Cliente', folio: 'FA-1', saldo: 3700, total: 8700, acreditadoNc: 0, pagadoRepp: 5000, moneda: 'MXN', numParcialidadSiguiente: 2 })}>Segunda parcialidad</button>
  </>,
}));
describe('19-oct #64', () => {
  it('muestra saldo anterior y restante para 8700 menos 5000 y después 3700', () => {
    render(<NuevoRepp onClose={() => {}} />, { wrapper: createQueryWrapper() });
    expect(screen.getByRole('button', { name: 'Vista previa' })).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Primera parcialidad' }));
    const monto = screen.getAllByRole('spinbutton').at(-1)!;
    fireEvent.change(monto, { target: { value: '5000' } });
    expect(screen.getByText(/Saldo por cobrar 8700.00 MXN/)).toHaveTextContent('Saldo restante 3700.00 MXN');
    fireEvent.click(screen.getByRole('button', { name: 'Segunda parcialidad' }));
    expect(screen.getByText(/Saldo por cobrar 3700.00 MXN/)).toHaveTextContent('Saldo restante 0.00 MXN');
    expect(emitir).not.toHaveBeenCalled();
  });
});
