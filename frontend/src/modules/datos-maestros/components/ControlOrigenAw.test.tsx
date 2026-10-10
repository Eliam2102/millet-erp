import { afterEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { ControlOrigenAw } from './ControlOrigenAw';
import { etiquetaAwOrigen, puedeCambiarAwOrigen, type AwOrigenEstado } from '../api/aw-origen';

const url = '*/api/v1/integraciones/aw/origen';
const estado: AwOrigenEstado = { origen: 'Real', permitido: true, demoConfigurada: true,
  origenClientesReal: 'Sql', origenProductosReal: 'Simulado', cambiadoPor: null, cambiadoEn: null, version: 1 };
const permiso = 'integraciones.aw.administracion.configuracion';
function abrir(e = estado, permisos: string[] = [permiso]) {
  useAuthStore.setState({ status: 'authenticated', accessToken: 't', permisos,
    expiresAt: new Date(Date.now() + 3_600_000) });
  mswServer.use(http.get(url, () => HttpResponse.json(e)));
  return render(<ControlOrigenAw area="clientes" enabled />, { wrapper: createQueryWrapper() });
}
afterEach(() => useAuthStore.setState({ status: 'idle', accessToken: null, permisos: [] }));

describe('Origen A+W', () => {
  it('etiquetas del origen real, simulado y demo', () => {
    expect(etiquetaAwOrigen(estado, 'clientes')).toBe('Origen: A+W en vivo');
    expect(etiquetaAwOrigen(estado, 'productos')).toBe('Origen: simulado');
    expect(etiquetaAwOrigen({ ...estado, origen: 'Demo' }, 'clientes')).toBe('Origen: copia de demo');
    expect(puedeCambiarAwOrigen(estado, [])).toBe(false);
  });
  it.each([ { permisos: [], permitido: true }, { permisos: [permiso], permitido: false } ])(
    'sin permiso o con ambiente apagado muestra etiqueta y oculta el botón ($permitido)', async ({ permisos, permitido }) => {
      abrir({ ...estado, permitido }, permisos);
      expect(await screen.findByText('Origen: A+W en vivo')).toBeInTheDocument();
      expect(screen.queryByRole('tab', { name: 'Copia de demo' })).not.toBeInTheDocument();
    });
  it('solo cambia después de confirmar y envía ETag e Idempotency-Key', async () => {
    let cambios = 0;
    mswServer.use(http.put(url, async ({ request }) => {
      expect(request.headers.get('If-Match')).toBe('"1"');
      expect(request.headers.get('Idempotency-Key')).toBeTruthy();
      expect(await request.json()).toEqual({ origen: 'Demo' });
      cambios++;
      mswServer.use(http.get(url, () => HttpResponse.json({ ...estado, origen: 'Demo', version: 2 })));
      return HttpResponse.json({ ...estado, origen: 'Demo', version: 2 });
    }));
    abrir();
    fireEvent.mouseDown(await screen.findByRole('tab', { name: 'Copia de demo' }), { button: 0, ctrlKey: false });
    expect(screen.getByText('Lo que sincronices desde la copia de demo se guarda en esta base del ERP. Úsalo solo en ambientes de prueba o demo.')).toBeInTheDocument();
    expect(cambios).toBe(0);
    fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }));
    expect(cambios).toBe(0);
    fireEvent.mouseDown(screen.getByRole('tab', { name: 'Copia de demo' }), { button: 0, ctrlKey: false });
    fireEvent.click(screen.getByRole('button', { name: 'Cambiar origen' }));
    await waitFor(() => expect(cambios).toBe(1));
    expect(await screen.findByText('Origen: copia de demo')).toBeInTheDocument();
  });
  it('explica por qué falta configurar la demo', async () => {
    abrir({ ...estado, demoConfigurada: false });
    expect(await screen.findByRole('tab', { name: 'Copia de demo' })).toBeDisabled();
    expect(screen.getByText('La copia de demo de A+W no está configurada en este ambiente.')).toBeInTheDocument();
  });
});
