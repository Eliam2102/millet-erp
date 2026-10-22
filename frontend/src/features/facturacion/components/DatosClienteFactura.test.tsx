import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { DatosClienteFactura } from './DatosClienteFactura';

const mutate = vi.hoisted(() => vi.fn());
vi.mock('../api/useEditarDatosCliente', () => ({
  useEditarDatosCliente: () => ({ mutate, isPending: false, isError: false, isSuccess: false }),
}));
const factura = { id: 'demo', version: 2, estado: 'Borrador', uuid: null, ordenCompraCliente: 'PO DEMO', comentarios: 'Comentario ficticio' };

describe('Datos para el cliente', () => {
  it('permite editar y guardar el borrador con su versión', () => {
    render(<DatosClienteFactura factura={factura} puedeEditar />);
    expect(screen.getByText('Se imprimen en la factura; no forman parte del CFDI')).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Orden de compra del cliente'), { target: { value: 'PO DEMO 2' } });
    fireEvent.click(screen.getByRole('button', { name: 'Guardar datos para el cliente' }));
    expect(mutate).toHaveBeenCalledWith(expect.objectContaining({ id: 'demo', version: 2, datos: expect.objectContaining({ ordenCompraCliente: 'PO DEMO 2' }) }));
  });

  it.each(['Timbrado', 'TimbradoEnProceso', 'CancelacionPendiente', 'Cancelado', 'Descartada'])('es de solo lectura en %s', (estado) => {
    render(<DatosClienteFactura factura={{ ...factura, estado }} puedeEditar />);
    expect(screen.getByLabelText('Orden de compra del cliente')).toHaveAttribute('readonly');
    expect(screen.queryByRole('button', { name: 'Guardar datos para el cliente' })).not.toBeInTheDocument();
  });

  it('respeta el permiso de emisión y conserva los límites de captura', () => {
    render(<DatosClienteFactura factura={factura} puedeEditar={false} />);
    expect(screen.getByLabelText('Comentarios')).toHaveAttribute('readonly');
    expect(screen.getByLabelText('Comentarios')).toHaveAttribute('maxlength', '1000');
    expect(screen.getByLabelText('Proforma')).toHaveAttribute('maxlength', '40');
  });
});
