import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { FacturaPicker } from './FacturaPicker';
import { EstadoPasivo } from '../api/types';
vi.mock('../api/useFacturas', () => ({ useFacturas: () => ({ data: { items: [
  { id: 'abierta', estado: EstadoPasivo.Autorizada, folioProveedor: 'ABIERTA', saldoPendiente: 50, total: 100, moneda: 'MXN', fechaDocumento: '2026-10-10' },
  { id: 'pagada', estado: EstadoPasivo.Pagada, folioProveedor: 'PAGADA', saldoPendiente: 0, total: 100, moneda: 'MXN', fechaDocumento: '2026-10-10' },
  { id: 'cancelada', estado: EstadoPasivo.Cancelada, folioProveedor: 'CANCELADA', saldoPendiente: 100, total: 100, moneda: 'MXN', fechaDocumento: '2026-10-10' },
] } }) }));
describe('Factura origen de nota de cargo', () => {
  it('ofrece únicamente documentos con saldo que no estén pagados ni cancelados', () => {
    render(<FacturaPicker value={null} onChange={() => {}} soloConSaldo />);
    fireEvent.click(screen.getByRole('combobox'));
    expect(screen.getByText('ABIERTA')).toBeInTheDocument();
    expect(screen.queryByText('PAGADA')).not.toBeInTheDocument();
    expect(screen.queryByText('CANCELADA')).not.toBeInTheDocument();
  });
});
