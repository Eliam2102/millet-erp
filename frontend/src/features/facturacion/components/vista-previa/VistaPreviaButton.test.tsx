import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import { VistaPreviaButton } from './VistaPreviaButton';
import type { BorradorCfdi } from './modelo';
const descargar = vi.hoisted(() => vi.fn().mockResolvedValue(undefined));
vi.mock('@/components/erp/reportes/exporters/borrador-cfdi-exporter', () => ({ descargarBorradorCfdi: descargar }));
describe('Vista previa', () => {
  it('explica el bloqueo y no envía el formulario ni consulta la red al descargar', async () => {
    const submit = vi.fn(e => e.preventDefault());
    const datos: BorradorCfdi = { tipo: 'Factura', emisor: 'Prueba', receptor: 'Cliente', usoCfdi: 'G03', metodoPago: 'PUE', formaPago: '03', moneda: 'MXN', partidas: [], importes: [{ etiqueta: 'Total', monto: 116 }] };
    const obtenerDatos = vi.fn(() => datos);
    const { rerender } = render(<VistaPreviaButton motivo="Agrega partidas" obtenerDatos={obtenerDatos} />);
    expect(screen.getByRole('button')).toBeDisabled();
    expect(screen.getByRole('button')).toHaveAttribute('title', 'Agrega partidas');
    rerender(<form onSubmit={submit}><VistaPreviaButton obtenerDatos={obtenerDatos} /></form>);
    datos.importes[0].monto = 232;
    fireEvent.click(screen.getByRole('button'));
    await waitFor(() => expect(descargar).toHaveBeenCalledWith(expect.objectContaining({ importes: [{ etiqueta: 'Total', monto: 232 }] })));
    expect(submit).not.toHaveBeenCalled();
    expect(obtenerDatos).toHaveBeenCalledOnce();
  });
});
