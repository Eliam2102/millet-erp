import { beforeEach, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ProveedoresLayout } from './ProveedoresLayout';
import { NuevoProveedorContext } from './nuevo-proveedor-context';
import { useAuthStore } from '@/lib/auth/auth-store';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children }: { children: React.ReactNode }) => <a>{children}</a>,
}));
vi.mock('./CargaMaestrosDialog', () => ({ CargaMaestrosDialog: () => null }));

beforeEach(() => useAuthStore.setState({ permisos: [], accessToken: 'test-token' }));

it('muestra Badge de advertencia y envía el filtro de expediente al servidor', async () => {
  const consultas: string[] = [];
  mswServer.use(http.get('*/api/v1/datos-maestros/proveedores', ({ request }) => {
    consultas.push(request.url);
    const filtrado = new URL(request.url).searchParams.has('soloExpedienteIncompleto');
    return HttpResponse.json({ items: [
      { id: 'p-1', clave: 'SAP1', razonSocial: 'Sintético incompleto', estatus: 1, expedienteCompleto: false, documentosFaltantes: 2 },
      ...(!filtrado ? [{ id: 'p-2', clave: 'SAP2', razonSocial: 'Sintético completo', estatus: 1, expedienteCompleto: true, documentosFaltantes: 0 }] : []),
    ], total: filtrado ? 1 : 2, offset: 0, limit: 200 });
  }));
  render(<NuevoProveedorContext.Provider value={{ abrir: vi.fn(), cerrar: vi.fn(), setDirty: vi.fn() }}>
    <ProveedoresLayout idActivo={null} />
  </NuevoProveedorContext.Provider>, { wrapper: createQueryWrapper() });
  expect(await screen.findByText('Sintético completo')).toBeInTheDocument();
  expect(screen.getByText('Expediente incompleto')).toHaveClass('bg-warning-bg');
  fireEvent.click(screen.getByRole('checkbox', { name: 'Sólo expediente incompleto' }));
  await waitFor(() => expect(screen.queryByText('Sintético completo')).not.toBeInTheDocument());
  expect(consultas.some(url => url.includes('soloExpedienteIncompleto=true'))).toBe(true);
  fireEvent.click(screen.getByRole('checkbox', { name: 'Sólo expediente incompleto' }));
  expect(await screen.findByText('Sintético completo')).toBeInTheDocument();
});
