import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { SolicitudesCancelacionPanel } from './SolicitudesCancelacionPanel';

vi.mock('@/lib/auth/useHasPermission', () => ({ useHasPermission: () => true }));

const URL = '*/api/v1/cuentas-por-pagar/cfdis/cfdi-1/solicitudes-cancelacion';
const base = {
  id: 'sol-1', cfdiRecibidoId: 'cfdi-1', area: 1, fechaRecepcion: '2026-11-03T18:00:00Z',
  plazoSatVence: '2099-11-06T18:00:00Z', responsableId: 'u1', responsableNombre: 'Marta Ibarra',
  notas: null, fechaDecision: null, decididoPor: null, decididoPorNombre: null,
  comentarioDecision: null, evidenciaNombre: null,
};

describe('SolicitudesCancelacionPanel', () => {
  it('lista responsable, área y plazo SAT, y exige comentario para decidir', async () => {
    let decidirLlamado = false;
    mswServer.use(
      http.get(URL, () => HttpResponse.json([{ ...base, estado: 1 }])),
      http.post(`${URL}/sol-1/decidir`, () => {
        decidirLlamado = true;
        return HttpResponse.json({ ...base, estado: 3 });
      }),
    );
    render(<SolicitudesCancelacionPanel cfdiId="cfdi-1" />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('Marta Ibarra')).toBeInTheDocument();
    expect(screen.getByText('Plazo del SAT vence')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Rechazar' }));
    expect(screen.getByText('El comentario es obligatorio.')).toBeInTheDocument();
    expect(decidirLlamado).toBe(false);

    fireEvent.change(screen.getByLabelText('Comentario de la decisión'), { target: { value: 'Ya está pagada' } });
    fireEvent.click(screen.getByRole('button', { name: 'Rechazar' }));
    await waitFor(() => expect(decidirLlamado).toBe(true));
  });

  it('muestra quién decidió, cuándo y la evidencia de una solicitud decidida', async () => {
    mswServer.use(
      http.get(URL, () =>
        HttpResponse.json([{
          ...base, estado: 3, fechaDecision: '2026-11-04T16:00:00Z', decididoPorNombre: 'Raúl Beltrán',
          comentarioDecision: 'La factura ya se pagó', evidenciaNombre: 'correo-proveedor.pdf',
        }]),
      ),
    );
    render(<SolicitudesCancelacionPanel cfdiId="cfdi-1" />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('Raúl Beltrán')).toBeInTheDocument();
    expect(screen.getByText('Rechazada')).toBeInTheDocument();
    expect(screen.getByText('correo-proveedor.pdf')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Aceptar' })).toBeNull();
  });
});
