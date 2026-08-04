import { afterEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { DetalleCartaPorte } from '@/features/facturacion/pages/DetalleCartaPorte';
import { useAuthStore } from '@/lib/auth/auth-store';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
  useParams: () => ({ id: 'c-1' }),
  useSearch: () => ({}),
  useNavigate: () => () => {},
}));

const detalleTimbrado = {
  id: 'c-1',
  folio: 'CP-1',
  estado: 'Timbrado',
  uuid: 'U-1',
  tipo: 'T',
  origen: 'Cancún',
  destino: 'Mérida',
  distanciaKm: 300,
  fechaSalida: '2026-05-30T08:00:00Z',
  fechaLlegadaEstimada: '2026-05-30T14:00:00Z',
  cartaPortePreviaId: null,
  total: 0,
  vehiculo: { id: 'v-1', placa: 'ABC123', configVehicular: 'C2', anioModelo: 2020 },
  operador: { id: 'o-1', rfc: 'XXXX010101XX1', nombre: 'Operador Demo', numLicencia: 'L-9' },
  mercancias: [
    {
      descripcion: 'Vidrio templado',
      bienesTransp: '43211503',
      claveUnidad: 'KGM',
      cantidad: 10,
      pesoEnKg: 500,
      materialPeligroso: false,
    },
  ],
};

afterEach(() => {
  useAuthStore.setState({
    status: 'idle',
    accessToken: null,
    expiresAt: null,
    user: null,
    empresas: [],
    currentEmpresaId: null,
    permisos: [],
    errorMessage: null,
  });
});

describe('<DetalleCartaPorte> — smoke', () => {
  it('"Crear siguiente tramo" visible con permiso emitir en tramo timbrado', async () => {
    useAuthStore.setState({
      status: 'authenticated',
      accessToken: 't',
      expiresAt: new Date(Date.now() + 3600_000),
      user: { id: 'u', email: 't@e.com', nombre: 'T' },
      empresas: [],
      currentEmpresaId: 'e',
      permisos: ['facturacion.carta-porte.leer', 'facturacion.carta-porte.emitir'],
      errorMessage: null,
    });
    mswServer.use(
      http.get('*/api/v1/facturacion/carta-porte/c-1', () =>
        HttpResponse.json(detalleTimbrado),
      ),
    );
    render(<DetalleCartaPorte />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('CP-1')).toBeInTheDocument());
    expect(
      screen.getByRole('button', { name: /Crear siguiente tramo/i }),
    ).toBeInTheDocument();
  });

  it('renderiza tramo, vehículo, operador y mercancías', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/carta-porte/c-1', () =>
        HttpResponse.json({
          id: 'c-1',
          folio: 'CP-1',
          estado: 'Timbrado',
          uuid: 'U-1',
          tipo: 'T',
          origen: 'Cancún',
          destino: 'Mérida',
          distanciaKm: 300,
          fechaSalida: '2026-05-30T08:00:00Z',
          fechaLlegadaEstimada: '2026-05-30T14:00:00Z',
          cartaPortePreviaId: null,
          total: 0,
          vehiculo: { id: 'v-1', placa: 'ABC123', configVehicular: 'C2', anioModelo: 2020 },
          operador: { id: 'o-1', rfc: 'XXXX010101XX1', nombre: 'Operador Demo', numLicencia: 'L-9' },
          mercancias: [
            {
              descripcion: 'Vidrio templado',
              bienesTransp: '43211503',
              claveUnidad: 'KGM',
              cantidad: 10,
              pesoEnKg: 500,
              materialPeligroso: false,
            },
          ],
        }),
      ),
    );
    render(<DetalleCartaPorte />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('CP-1')).toBeInTheDocument());
    expect(screen.getByText(/ABC123/)).toBeInTheDocument();
    expect(screen.getByText('Vidrio templado')).toBeInTheDocument();
    expect(screen.getByText(/Mercancías \(1\)/i)).toBeInTheDocument();
  });

  it('error', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/carta-porte/c-1', () =>
        HttpResponse.json(
          { type: 'about:blank', title: 'No encontrada', status: 404 },
          { status: 404, headers: { 'Content-Type': 'application/problem+json' } },
        ),
      ),
    );
    render(<DetalleCartaPorte />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(
        screen.getByText(/No se pudo cargar la Carta Porte/i),
      ).toBeInTheDocument(),
    );
  });
});
