import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { toast } from 'sonner';
import { TerminalTarjetaCard } from './TerminalTarjetaCard';

beforeEach(() => {
  const banco = '*/api/v1/facturacion/terminal-tarjetas/factura-banco';
  mswServer.use(
    http.get(`${banco}/candidatos`, () => HttpResponse.json([])),
    http.get(`${banco}/cuadre`, () => HttpResponse.json(null)),
    http.get(banco, () => HttpResponse.json([])),
  );
});

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

  it('explica por qué no puede cargar y no expone excepciones del servidor', async () => {
    const error = vi.spyOn(toast, 'error');
    mswServer.use(
      http.get('*/api/v1/facturacion/terminal-tarjetas/transacciones', () => HttpResponse.json({ items: [], total: 0 })),
      http.post('*/api/v1/facturacion/terminal-tarjetas/cargas', () => HttpResponse.json(
        { title: 'TERMINAL_ERROR', detail: 'Invalid payload at endpoint', status: 500 }, { status: 500 })),
    );
    render(<TerminalTarjetaCard />, { wrapper: createQueryWrapper() });
    const cargar = screen.getByRole('button', { name: 'Cargar archivo' });
    expect(cargar).toHaveAttribute('title', 'Elige el archivo y el día de las transacciones.');
    fireEvent.change(screen.getByLabelText('Archivo de la terminal'), { target: { files: [new File(['x'], 'prueba.csv')] } });
    fireEvent.change(screen.getByLabelText('Día de las transacciones'), { target: { value: '2026-11-03' } });
    fireEvent.click(cargar);
    await waitFor(() => expect(error).toHaveBeenCalledWith(
      'No se pudo cargar el archivo. Revisa los datos y tu conexión antes de volver a intentarlo.',
    ));
    error.mockRestore();
  });

it('distingue un fallo de consulta de una pestaña sin ventas', async () => {
  mswServer.use(http.get('*/api/v1/facturacion/terminal-tarjetas/transacciones', () => HttpResponse.json(
    { title: 'TERMINAL_ERROR', detail: 'Invalid payload at endpoint', status: 500 }, { status: 500 },
  )));
  render(<TerminalTarjetaCard />, { wrapper: createQueryWrapper() });
  expect(await screen.findByText('No se pudieron cargar las ventas de la terminal')).toBeInTheDocument();
  expect(screen.queryByText('Sin ventas en esta pestaña.')).not.toBeInTheDocument();
  expect(screen.queryByText(/TERMINAL_ERROR|Invalid payload/)).not.toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Reintentar' })).toBeInTheDocument();
});
