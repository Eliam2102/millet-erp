import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import type { SolicitudAnulacion } from '../../api/polizas-types';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to, params, ...rest }: { children: React.ReactNode; to: string; params?: { id: string }; [k: string]: unknown }) => (
    <a href={params ? to.replace('$id', params.id) : to} {...(rest as object)}>{children}</a>
  ),
}));

import { SolicitudesAnulacionPanel } from './SolicitudesAnulacionPanel';
import { MENSAJE_MISMA_PERSONA, esPropia } from '../../lib/anulacion';

const API = '*/api/v1/contabilidad/polizas';

function solicitud(cambios: Partial<SolicitudAnulacion> = {}): SolicitudAnulacion {
  return {
    id: 's1', polizaId: 'p1', polizaFolio: 'TT-000123', polizaConcepto: 'FIX Traspaso', polizaFecha: '2026-11-05', polizaTotal: 1234,
    motivo: 'FIX Captura duplicada', periodoAnio: null, periodoNumero: null, estado: 'Pendiente', solicitadaPorId: 'u-a',
    solicitadaPor: 'FIX Analista', solicitadaEn: '2026-11-09T15:00:00Z', decididaPorId: null, decididaPor: null, decididaEn: null,
    motivoRechazo: null, polizaAnulacionId: null, version: 1, ...cambios,
  };
}

afterEach(() => useAuthStore.setState({ user: null }));

describe('M-212 · solicitudes de anulación', () => {
  it('quien pidió la anulación no la decide', () => {
    expect(esPropia({ solicitadaPorId: 'u-a' }, 'u-a')).toBe(true);
    expect(esPropia({ solicitadaPorId: 'u-a' }, 'u-b')).toBe(false);
    expect(esPropia({ solicitadaPorId: null }, null)).toBe(false);
  });

  it('sin pendientes no muestra nada', async () => {
    mswServer.use(http.get(`${API}/solicitudes-anulacion`, () => HttpResponse.json([])));
    const { container } = render(<SolicitudesAnulacionPanel />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(container).toBeEmptyDOMElement());
  });

  it('la solicitud propia deshabilita Aprobar y Rechazar con el motivo', async () => {
    mswServer.use(http.get(`${API}/solicitudes-anulacion`, () => HttpResponse.json([solicitud()])));
    useAuthStore.setState({ user: { id: 'u-a', email: 'fix@example.com', nombre: 'FIX Analista' } });
    render(<SolicitudesAnulacionPanel />, { wrapper: createQueryWrapper() });

    const panel = await screen.findByRole('region', { name: 'Solicitudes de anulación' });
    const aprobar = within(panel).getByRole('button', { name: 'Aprobar' });
    expect(aprobar).toBeDisabled();
    expect(aprobar).toHaveAttribute('title', MENSAJE_MISMA_PERSONA);
    expect(within(panel).getByRole('button', { name: 'Rechazar' })).toBeDisabled();
  });

  it('otra persona aprueba y ve la póliza de anulación; o rechaza con motivo', async () => {
    let rechazo: unknown = null;
    mswServer.use(
      http.get(`${API}/solicitudes-anulacion`, () => HttpResponse.json([solicitud()])),
      http.post(`${API}/solicitudes-anulacion/s1/aprobar`, () => HttpResponse.json({ id: 'p2', folio: 'TT-000124' }, { status: 201 })),
      http.post(`${API}/solicitudes-anulacion/s1/rechazar`, async ({ request }) => {
        rechazo = await request.json();
        return HttpResponse.json(solicitud({ estado: 'Rechazada' }));
      }),
    );
    useAuthStore.setState({ user: { id: 'u-b', email: 'fix@example.com', nombre: 'FIX Supervisor' } });
    render(<SolicitudesAnulacionPanel />, { wrapper: createQueryWrapper() });

    const panel = await screen.findByRole('region', { name: 'Solicitudes de anulación' });
    fireEvent.click(within(panel).getByRole('button', { name: 'Aprobar' }));
    expect(await within(panel).findByRole('status')).toHaveTextContent('TT-000123 quedó anulada por TT-000124');

    fireEvent.click(within(panel).getByRole('button', { name: 'Rechazar' }));
    const dialogo = await screen.findByRole('dialog');
    const confirmar = within(dialogo).getByRole('button', { name: 'Rechazar anulación' });
    expect(confirmar).toBeDisabled();
    fireEvent.change(within(dialogo).getByLabelText(/^Motivo del rechazo/), { target: { value: 'FIX la póliza es correcta' } });
    fireEvent.click(confirmar);
    await waitFor(() => expect(rechazo).toEqual({ motivo: 'FIX la póliza es correcta' }));
  });
});
