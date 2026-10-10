import type { ReactNode } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { AplicarDocumentoFacturaSheet } from './AplicarDocumentoFacturaSheet';
import { EstadoPasivo, type FacturaDetalle } from '@/features/cxp/api/types';
const mocks = vi.hoisted(() => ({ anticipo: vi.fn(), nc: vi.fn(), moneda: 'MXN' }));
vi.mock('@/lib/api', () => ({ useFormIdempotencyKey: () => 'FIX-P4-key', esApiError: () => false }));
vi.mock('@/features/cxp/api/useNotasYAnticipos', () => ({
  useAnticipos: () => ({ data: { items: [{ id: 'anti', serie:'FANT',folioProveedor:'FIX',moneda:mocks.moneda,saldoAmortizable:10000,version:3 }] } }),
  useNotasCredito: () => ({ data: { items: [{ id: 'nc', folioProveedor:'FIX-NC',moneda:'MXN',saldoPorAplicar:10000,version:4,cargoReconocidoPendiente:0 }] } }),
  useAplicarAnticipoAFactura: () => ({ mutate:mocks.anticipo,isPending:false }),
  useAplicarNcAFactura: () => ({ mutate:mocks.nc,isPending:false }),
}));
vi.mock('@/components/ui/select', () => ({
  Select: ({ children,onValueChange }: { children: ReactNode;onValueChange:(v:string)=>void }) => <select aria-label="Documento abierto" onChange={e=>onValueChange(e.target.value)}><option value=""/>{children}</select>,
  SelectTrigger: () => null, SelectValue: () => null,
  SelectContent: ({children}:{children:ReactNode}) => <>{children}</>,
  SelectItem: ({children,value}:{children:ReactNode;value:string}) => <option value={value}>{children}</option>,
}));
const factura = { id:'factura',proveedorId:'proveedor',version:2,saldoPendiente:50000,moneda:'MXN',estado:EstadoPasivo.Autorizada } as FacturaDetalle;
describe('acciones de aplicaciones P4', () => {
  it.each(['anticipo','nc'] as const)('aplica %s desde la pantalla con versiones e idempotencia', tipo => {
    mocks.moneda='MXN';render(<AplicarDocumentoFacturaSheet factura={factura} tipo={tipo} onClose={vi.fn()} />);
    fireEvent.change(screen.getByRole('combobox'),{target:{value:tipo==='anticipo'?'anti':'nc'}});
    fireEvent.change(screen.getByLabelText('Importe a aplicar'),{target:{value:'10000'}});
    expect(screen.getByText(/40,000.00/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button',{name:tipo==='anticipo'?'Aplicar anticipo':'Aplicar NC'}));
    expect(tipo==='anticipo'?mocks.anticipo:mocks.nc).toHaveBeenCalledWith(expect.objectContaining({facturaId:'factura',facturaVersionEsperada:2,idempotencyKey:'FIX-P4-key',command:expect.objectContaining({monto:10000})}),expect.any(Object));
  });
  it('muestra el rechazo por moneda y deshabilita la aplicación',()=> {
    mocks.moneda='USD';render(<AplicarDocumentoFacturaSheet factura={factura} tipo="anticipo" onClose={vi.fn()} />);
    fireEvent.change(screen.getByRole('combobox'),{target:{value:'anti'}});
    fireEvent.change(screen.getByLabelText('Importe a aplicar'),{target:{value:'10000'}});
    expect(screen.getByRole('alert')).toHaveTextContent('misma moneda');expect(screen.getByRole('button',{name:'Aplicar anticipo'})).toBeDisabled();
  });
});
