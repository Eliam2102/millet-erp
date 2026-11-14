import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { CatalogoTerminalesCard } from './CatalogoTerminalesCard';

describe('M-111 · catálogo de terminales por sucursal', () => {
  it('lista las terminales sin sucursal, las precarga al formulario y desactiva una del catálogo', async () => {
    let desactivada: string | null = null;
    mswServer.use(
      http.get('*/api/v1/facturacion/terminal-tarjetas/terminales/sin-catalogar', () =>
        HttpResponse.json([{ afiliacion: '1234567', codTerminal: 'T0001', ventas: 4 }]),
      ),
      http.get('*/api/v1/facturacion/terminal-tarjetas/terminales', () =>
        HttpResponse.json([
          { id: 'x1', afiliacion: '7654321', codTerminal: 'T0002', sucursalId: 's1', descripcion: 'Mostrador de prueba', activa: true },
        ]),
      ),
      http.post('*/api/v1/facturacion/terminal-tarjetas/terminales/:id/desactivar', ({ params }) => {
        desactivada = String(params.id);
        return HttpResponse.json({ id: 'x1', afiliacion: '7654321', codTerminal: 'T0002', sucursalId: 's1', descripcion: null, activa: false });
      }),
    );
    render(<CatalogoTerminalesCard />, { wrapper: createQueryWrapper() });

    fireEvent.click(await screen.findByRole('button', { name: /1234567 \/ T0001 · 4 venta/ }));
    expect(screen.getByLabelText('Afiliación')).toHaveValue('1234567');
    expect(screen.getByLabelText('Terminal')).toHaveValue('T0001');
    expect(screen.getByRole('button', { name: 'Guardar terminal' })).toBeDisabled();

    fireEvent.click(await screen.findByRole('button', { name: 'Desactivar terminal 7654321 T0002' }));
    await waitFor(() => expect(desactivada).toBe('x1'));
  });
});
