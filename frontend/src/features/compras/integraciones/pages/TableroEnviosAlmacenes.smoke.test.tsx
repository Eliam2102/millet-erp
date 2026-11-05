import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { TableroEnviosAlmacenes } from '@/features/compras/integraciones/pages/TableroEnviosAlmacenes';
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
const entrada = {
  ...base, id: 'e-2', secuencia: 2, mensajeId: 'ERP-OC-MID2026-000050-M1', tipo: 'COMPRA_MODIFICADA', estado: 'ERROR',
  ultimoError: 'Transporte simulado: falla',
  datos: {
    folio: 'OC-MID2026-000050', lineas: [],
    recepciones: [{ folio: 'EC-000012', fecha: '2026-11-05',
      importacion: { pedimento: '26 47 3456 6001234', bl: 'MAEU123456', contenedor: 'MSCU1234567', aduana: '470', clavePedimento: 'A1' },
      lineas: [{ lineaErp: '1', piezas: 48, lote: 'VF6-2611-A', rack: 'R-A01', fraccion: '70051001' }] }],
  },
};

beforeEach(() => {
  mswServer.use(http.get('*/api/v1/compras/integraciones/alm-compra/envios', () => HttpResponse.json([entrada, recibida])));
  useAuthStore.setState({
    status: 'authenticated', accessToken: 't', expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u', email: 't@e.com', nombre: 'T' }, empresas: [], currentEmpresaId: 'e',
    permisos: ['compras.ordenes.leer', 'compras.ordenes.logistica'], errorMessage: null,
  });
});
afterEach(() => useAuthStore.setState({ status: 'idle', accessToken: null, expiresAt: null, user: null, empresas: [], currentEmpresaId: null, permisos: [], errorMessage: null }));

describe('<TableroEnviosAlmacenes>', () => {
  it('muestra hojas, lote, rack e importación y reintenta solo lo que falló', async () => {
    const reintentos: string[] = [];
    mswServer.use(http.post('*/api/v1/compras/integraciones/alm-compra/envios/:id/reintentar', ({ params, request }) => {
      reintentos.push(`${params.id}:${request.headers.get('Idempotency-Key') ? 'idem' : 'sin-key'}`);
      return new HttpResponse(null, { status: 204 });
    }));
    render(<TableroEnviosAlmacenes />, { wrapper: createQueryWrapper() });
    await screen.findByText('ERP-OC-MID2026-000050-M1');
    expect(screen.getAllByRole('button', { name: 'Reintentar' })).toHaveLength(1);

    fireEvent.click(screen.getAllByRole('button', { name: 'Qué recibe A+W' })[0]);
    const fila = await screen.findByTestId('fila-recepcion');
    expect(fila).toHaveTextContent('48');
    expect(fila).toHaveTextContent('VF6-2611-A');
    expect(fila).toHaveTextContent('R-A01');
    expect(screen.getByTestId('importacion')).toHaveTextContent('BL MAEU123456');
    expect(screen.getByTestId('importacion')).toHaveTextContent('fracción 70051001');

    fireEvent.click(screen.getByRole('button', { name: 'Reintentar' }));
    await waitFor(() => expect(reintentos).toEqual(['e-2:idem']));
  });
});
