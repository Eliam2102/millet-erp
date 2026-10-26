import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { DatosClienteFactura } from './DatosClienteFactura';

const mutate = vi.hoisted(() => vi.fn());
vi.mock('../api/useEditarDatosCliente', async () => {
  const { useState } = await import('react');
  return { useEditarDatosCliente: () => {
    const [isSuccess, setSuccess] = useState(false);
    return { mutate: (datos: unknown) => { mutate(datos); setSuccess(true); }, isPending: false, isError: false, isSuccess };
  } };
});
const factura = { id: 'demo', version: 2, estado: 'Borrador', uuid: null, ordenCompraCliente: 'PO DEMO', observaciones: 'Comentario ficticio' };

describe('Datos para el cliente', () => {
  it('permite editar y guardar el borrador con su versión', () => {
    render(<DatosClienteFactura factura={factura} puedeEditar />);
    expect(screen.getByText('Se imprimen en la factura; no forman parte del CFDI')).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Orden de compra del cliente'), { target: { value: 'PO DEMO 2' } });
    fireEvent.click(screen.getByRole('button', { name: 'Guardar datos para el cliente' }));
    expect(mutate).toHaveBeenCalledWith(expect.objectContaining({ id: 'demo', version: 2, datos: expect.objectContaining({ ordenCompraCliente: 'PO DEMO 2' }) }));
  });

  it('conserva la edición y el mensaje al actualizar la versión después de guardar', () => {
    const { rerender } = render(<DatosClienteFactura factura={factura} puedeEditar />);
    fireEvent.change(screen.getByLabelText('Observaciones'), { target: { value: 'Edición DEMO' } });
    fireEvent.click(screen.getByRole('button', { name: 'Guardar datos para el cliente' }));
    rerender(<DatosClienteFactura factura={{ ...factura, version: 3, observaciones: 'Edición DEMO' }} puedeEditar />);
    expect(screen.getByRole('status')).toHaveTextContent('Datos guardados.');
    expect(screen.getByLabelText('Observaciones')).toHaveValue('Edición DEMO');
    fireEvent.click(screen.getByRole('button', { name: 'Guardar datos para el cliente' }));
    expect(mutate).toHaveBeenLastCalledWith(expect.objectContaining({ version: 3 }));
  });

  it.each(['Timbrado' , 'TimbradoEnProceso', 'CancelacionPendiente', 'Cancelado', 'Descartada'])('es de solo lectura en %s', (estado) => {
    render(<DatosClienteFactura factura={{ ...factura, estado }} puedeEditar />);
    expect(screen.getByLabelText('Orden de compra del cliente')).toHaveAttribute('readonly');
    expect(screen.queryByRole('button', { name: 'Guardar datos para el cliente' })).not.toBeInTheDocument();
  });

  it('respeta el permiso de emisión y conserva los límites de captura', () => {
    render(<DatosClienteFactura factura={factura} puedeEditar={false} />);
    expect(screen.getByLabelText('Observaciones')).toHaveAttribute('readonly');
    expect(screen.getByLabelText('Observaciones')).toHaveAttribute('maxlength', '1000');
    expect(screen.getByLabelText('Proforma')).toHaveAttribute('maxlength', '40');
  });
});
