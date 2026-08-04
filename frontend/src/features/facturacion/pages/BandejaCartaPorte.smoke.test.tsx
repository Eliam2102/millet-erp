import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { BandejaCartaPorte } from '@/features/facturacion/pages/BandejaCartaPorte';
import { useAuthStore } from '@/lib/auth/auth-store';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
  useNavigate: () => () => {},
  useSearch: () => ({}),
}));

const abrirSpy = vi.fn();
vi.mock('@/features/facturacion/components/nueva-carta-porte-context', () => ({
  useNuevaCartaPorte: () => ({
    abrir: abrirSpy,
    cerrar: () => {},
    setDirty: () => {},
  }),
}));

beforeEach(() => {
  abrirSpy.mockClear();
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: ['facturacion.carta-porte.leer', 'facturacion.carta-porte.emitir'],
    errorMessage: null,
  });
});

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

describe('<BandejaCartaPorte> — smoke', () => {
  it('lista una Carta Porte y muestra Nueva Carta Porte', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/carta-porte', () =>
        HttpResponse.json([
          {
            id: 'c-1',
            folio: 'CP-1',
            estado: 'Timbrado',
            uuid: 'U-1',
            tipo: 'T',
            tramo: 'Cancún → Mérida',
            fechaSalida: '2026-05-30T08:00:00Z',
          },
        ]),
      ),
    );
    render(<BandejaCartaPorte />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText('Cancún → Mérida')).toBeInTheDocument(),
    );
    expect(screen.getByText('CP-1')).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /Nueva Carta Porte/i }),
    ).toBeInTheDocument();
  });

  it('empty', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/carta-porte', () => HttpResponse.json([])),
    );
    render(<BandejaCartaPorte />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/Sin Cartas Porte/i)).toBeInTheDocument(),
    );
  });
});
