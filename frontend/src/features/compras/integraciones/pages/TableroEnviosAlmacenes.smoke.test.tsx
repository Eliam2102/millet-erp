import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { TableroEnviosAlmacenes } from '@/features/compras/integraciones/pages/TableroEnviosAlmacenes';
import { estaAtorado } from '@/features/compras/integraciones/api/atorado';
import { useAuthStore } from '@/lib/auth/auth-store';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => <a href={to}>{children}</a>,
}));

const base = {
  ordenCompraId: 'oc-1', referencia: 'OC-MID2026-000050', intentos: 0, proximoIntentoEn: null, ultimoError: null,
  compraIdAlmacenes: null, enviadoEn: null, acuseEn: null, createdAt: '2026-11-05T15:00:00Z',
};
const recibida = {
  ...base, id: 'e-1', secuencia: 1, mensajeId: 'ERP-OC-MID2026-000050-R1', tipo: 'COMPRA_RECIBIDA', estado: 'PROCESADO',
  compraIdAlmacenes: '1',
  datos: { folio: 'OC-MID2026-000050', lineas: [{ lineaErp: '1', articuloAw: 41036, piezas: 50, lote: 'VF6-2611-A' }] },
};
const modificada = {
  ...base, id: 'e-2', secuencia: 2, mensajeId: 'ERP-OC-MID2026-000050-M1', tipo: 'COMPRA_MODIFICADA', estado: 'ERROR',
  ultimoError: 'Transporte simulado: falla',
  datos: { folio: 'OC-MID2026-000050', lineas: [{ lineaErp: '1', articuloAw: 41036, ancho: 3302, alto: 6100, piezas: 48, lote: 'VF6-2611-A', pedimento: '26 47 3456 6001234' }] },
};

beforeEach(() => {
  mswServer.use(http.get('*/api/v1/compras/integraciones/alm-compra/envios', () => HttpResponse.json([modificada, recibida])));
  useAuthStore.setState({
    status: 'authenticated', accessToken: 't', expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u', email: 't@e.com', nombre: 'T' }, empresas: [], currentEmpresaId: 'e',
    permisos: ['compras.ordenes.leer', 'compras.ordenes.logistica'], errorMessage: null,
  });
});
afterEach(() => useAuthStore.setState({ status: 'idle', accessToken: null, expiresAt: null, user: null, empresas: [], currentEmpresaId: null, permisos: [], errorMessage: null }));

describe('<TableroEnviosAlmacenes>', () => {
  it('muestra hojas, lote y pedimento (sin recepción ni rack) y reintenta solo lo que falló', async () => {
    const reintentos: string[] = [];
    mswServer.use(http.post('*/api/v1/compras/integraciones/alm-compra/envios/:id/reintentar', ({ params, request }) => {
      reintentos.push(`${params.id}:${request.headers.get('Idempotency-Key') ? 'idem' : 'sin-key'}`);
      return new HttpResponse(null, { status: 204 });
    }));
    render(<TableroEnviosAlmacenes />, { wrapper: createQueryWrapper() });
    await screen.findByText('ERP-OC-MID2026-000050-M1');
    expect(screen.getAllByRole('button', { name: 'Reintentar' })).toHaveLength(1);

    fireEvent.click(screen.getAllByRole('button', { name: 'Qué recibe A+W' })[0]);
    const fila = await screen.findByText('3302 × 6100');
    expect(fila.closest('tr')).toHaveTextContent('VF6-2611-A');
    expect(fila.closest('tr')).toHaveTextContent('26 47 3456 6001234');
    expect(screen.queryByText('Rack')).toBeNull();
    expect(screen.queryByTestId('fila-recepcion')).toBeNull();

    fireEvent.click(screen.getByRole('button', { name: 'Reintentar' }));
    await waitFor(() => expect(reintentos).toEqual(['e-2:idem']));
  });

  it('marca atorado el mensaje enviado que lleva 5 minutos sin acuse y solo ese', () => {
    const ahora = Date.parse('2026-11-09T12:00:00Z');
    const hace = (min: number) => new Date(ahora - min * 60_000).toISOString();
    expect(estaAtorado({ estado: 'ENVIADO', enviadoEn: hace(4), acuseEn: null }, ahora)).toBe(false);
    expect(estaAtorado({ estado: 'ENVIADO', enviadoEn: hace(5), acuseEn: null }, ahora)).toBe(true);
    expect(estaAtorado({ estado: 'ENVIADO', enviadoEn: hace(30), acuseEn: hace(1) }, ahora)).toBe(false);
    expect(estaAtorado({ estado: 'PROCESADO', enviadoEn: hace(30), acuseEn: null }, ahora)).toBe(false);
    expect(estaAtorado({ estado: 'PENDIENTE', enviadoEn: null, acuseEn: null }, ahora)).toBe(false);
  });

  it('muestra la insignia Atorado en la fila sin acuse tras 5 minutos', async () => {
    const viejo = { ...modificada, id: 'e-3', mensajeId: 'ERP-X-R1', estado: 'ENVIADO', ultimoError: null, enviadoEn: new Date(Date.now() - 6 * 60_000).toISOString() };
    mswServer.use(http.get('*/api/v1/compras/integraciones/alm-compra/envios', () => HttpResponse.json([viejo, recibida])));
    render(<TableroEnviosAlmacenes />, { wrapper: createQueryWrapper() });
    await screen.findByText('ERP-X-R1');
    expect(screen.getAllByText('Atorado')).toHaveLength(1);
  });
});
