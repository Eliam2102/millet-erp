import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { TerminalTarjetaCard } from './TerminalTarjetaCard';

describe('86e3vr1tf · terminal de tarjetas', () => {
  it('sube el archivo con el día y muestra el resultado y las ventas sin emparejar', async () => {
    let tipoContenido: string | null = null;
    mswServer.use(
      http.post('*/api/v1/facturacion/terminal-tarjetas/cargas', async ({ request }) => {
        tipoContenido = request.headers.get('content-type');
        return HttpResponse.json({
          id: 'c1', nombreArchivo: 'terminal-prueba.xls', fechaArchivo: '2026-10-01', cargadoEn: '2026-10-01T20:00:00Z',
          renglones: 3, nuevos: 3, duplicados: 0, emparejados: 1, sinEmparejar: 1, rechazados: 1, liquidaciones: 1,
        });
      }),
      http.get('*/api/v1/facturacion/terminal-tarjetas/transacciones', ({ request }) =>
        HttpResponse.json({
          items: new URL(request.url).searchParams.get('estado') === '2'
            ? [{
                id: 't1', noOperacion: '900000000002', fechaHora: '2026-10-01T12:30:00', importe: 500, moneda: 'MXN',
                ultimos4: '4321', autorizacion: '654321', estatus: 'Aprobada', estado: 2, comision: null,
                ivaComision: null, neto: null, folioLiquidacion: null, cobroMostradorId: null,
                folioComprobante: null, liquidacionId: null,
              }]
            : [],
          offset: 0, limit: 50, total: 1,
        }),
      ),
    );
    render(<TerminalTarjetaCard />, { wrapper: createQueryWrapper() });

    expect(await screen.findByText('900000000002')).toBeInTheDocument();
    const cargar = screen.getByRole('button', { name: 'Cargar archivo' });
    expect(cargar).toBeDisabled();

    const archivo = new File(['<table></table>'], 'terminal-prueba.xls', { type: 'application/vnd.ms-excel' });
    fireEvent.change(screen.getByLabelText('Archivo de la terminal'), { target: { files: [archivo] } });
    fireEvent.change(screen.getByLabelText('Día de las transacciones'), { target: { value: '2026-10-01' } });
    await waitFor(() => expect(cargar).toBeEnabled());
    fireEvent.click(cargar);

    expect(await screen.findByRole('status')).toHaveTextContent(
      '3 renglón(es): 3 nuevos, 0 ya cargados, 1 emparejados, 1 sin emparejar, 1 rechazados',
    );
    expect(tipoContenido).toMatch(/multipart\/form-data/);
  });
});
