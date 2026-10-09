import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { conPermisos, limpiarAuth } from '@/features/centros-costo/pages/__fixtures__/configuracion-test-harness';
import { useAuthStore } from '@/lib/auth/auth-store';
import { SolicitudesCatalogo } from './SolicitudesCatalogo';

vi.mock('sonner', () => ({ toast: { success: vi.fn() } }));
afterEach(limpiarAuth);
const permiso = 'contabilidad.catalogo.autorizar';
function instalar(autor = 'otra-persona') {
  mswServer.use(http.get('*/api/v1/contabilidad/solicitudes', () => HttpResponse.json({ total: 1, items: [{
    id: '019a0000-0000-7000-8000-000000000001', operacion: 'Cambio', estado: 'Pendiente', preparadaPorId: autor,
    preparadaPor: 'FIX Contador', preparadaEn: '2026-10-09T09:00:00Z', version: 1,
    cambios: [{ antes: { noAfectableManual: false }, despues: { id: 'c1', codigo: 'FIX-1', nombre: 'FIX Banco', noAfectableManual: true } }],
  }] })));
}
it('muestra las diferencias y permite resolver con motivo, versión e idempotencia', async () => {
  conPermisos([permiso]);
  instalar();
  let body: unknown;
  let version: string | null = null;
  mswServer.use(http.post('*/api/v1/contabilidad/solicitudes/:id/resolver', async ({ request }) => {
    body = await request.json(); version = request.headers.get('If-Match');
    expect(request.headers.get('Idempotency-Key')).toBeTruthy();
    return HttpResponse.json({});
  }));
  render(<SolicitudesCatalogo />, { wrapper: createQueryWrapper() });
  fireEvent.click(await screen.findByText(/Ver antes y después/));
  expect(screen.getByText('No afectable por asiento manual')).toBeInTheDocument();
  expect(screen.getByText('Sí')).toBeInTheDocument();
  expect(screen.getByRole('button', { name: 'Rechazar' })).toBeDisabled();
  fireEvent.change(screen.getByLabelText('Motivo del rechazo'), { target: { value: 'FIX corregir' } });
  fireEvent.click(screen.getByRole('button', { name: 'Rechazar' }));
  await waitFor(() => expect(body).toEqual({ autorizar: false, motivo: 'FIX corregir' }));
  expect(version).toBe('"1"');
});

describe('segregación en la bandeja', () => {
  it('el DAF distinto envía la autorización con la versión de la solicitud', async () => {
    conPermisos([permiso]); instalar();
    let autorizacion: unknown;
    mswServer.use(http.post('*/api/v1/contabilidad/solicitudes/:id/resolver', async ({ request }) => {
      expect(request.headers.get('If-Match')).toBe('"1"');
      autorizacion = await request.json();
      return HttpResponse.json({});
    }));
    render(<SolicitudesCatalogo />, { wrapper: createQueryWrapper() });
    const autorizar = await screen.findByRole('button', { name: 'Autorizar' });
    expect(autorizar).toBeEnabled();
    fireEvent.click(autorizar);
    await waitFor(() => expect(autorizacion).toEqual({ autorizar: true, motivo: '' }));
  });

  it('sin permiso explica el bloqueo y no ofrece resolver', async () => {
    conPermisos([]); instalar();
    render(<SolicitudesCatalogo />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText(/Se requiere permiso/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Autorizar' })).not.toBeInTheDocument();
  });
  it('el autor no puede autorizar aunque tenga permiso', async () => {
    conPermisos([permiso]); instalar(useAuthStore.getState().user!.id);
    render(<SolicitudesCatalogo />, { wrapper: createQueryWrapper() });
    expect(await screen.findByRole('button', { name: 'Autorizar' })).toBeDisabled();
    expect(screen.getByText(/incluso si eres superadministrador/)).toBeInTheDocument();
  });
});
