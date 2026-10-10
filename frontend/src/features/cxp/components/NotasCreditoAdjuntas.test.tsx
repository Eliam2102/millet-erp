import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { NotasCreditoAdjuntas } from './NotasCreditoAdjuntas';

vi.mock('@/features/cxp/api/useCfdis', () => ({
  useCfdiParseado: () => ({ data: { subtotal: 30, descuentos: 0, moneda: 'MXN' }, isPending: false }),
}));
vi.mock('./CfdiPorProcesarPicker', () => ({
  CfdiPorProcesarPicker: ({ disabled }: { disabled: boolean }) => <button disabled={disabled}>Adjuntar NC</button>,
}));

describe('P3: adjuntar NC', () => {
  it('asigna base a la línea de OC seleccionada y permite retirar la NC', () => {
    const change = vi.fn();
    render(<NotasCreditoAdjuntas value={[{ cfdiRecibidoId: 'nc-1', lineas: [] }]}
      onChange={change} lineas={[{ lineaOcId: 'oc-linea-1', descripcion: 'Material de prueba' }]} facturaLigada />);
    fireEvent.change(screen.getByLabelText('Línea 1 · Material de prueba'), { target: { value: '30' } });
    expect(change).toHaveBeenCalledWith([{ cfdiRecibidoId: 'nc-1', lineas: [{ lineaOcId: 'oc-linea-1', base: 30 }] }]);
    fireEvent.click(screen.getByRole('button', { name: 'Quitar NC 1' }));
    expect(change).toHaveBeenLastCalledWith([]);
  });
  it('explica el bloqueo hasta ligar CFDI y líneas', () => {
    render(<NotasCreditoAdjuntas value={[]} onChange={vi.fn()} lineas={[]} facturaLigada={false} />);
    expect(screen.getByRole('button', { name: 'Adjuntar NC' })).toBeDisabled();
    expect(screen.getByText(/Liga el CFDI de la factura/)).toBeInTheDocument();
  });
});
