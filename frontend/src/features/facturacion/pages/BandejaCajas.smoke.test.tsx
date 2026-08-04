import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { BandejaCajas } from '@/features/facturacion/pages/BandejaCajas';
import { useAuthStore } from '@/lib/auth/auth-store';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
  useNavigate: () => () => {},
  useSearch: () => ({}),
}));

const abrirSpy = vi.fn();
vi.mock('@/features/facturacion/components/nueva-caja-context', () => ({
  useNuevaCaja: () => ({ abrir: abrirSpy, cerrar: () => {}, setDirty: () => {} }),
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
    permisos: ['facturacion.caja.administrar'],
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

describe('<BandejaCajas> — smoke', () => {
  it('lista una caja con su alcance y abre el Sheet con Nueva caja', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/cajas', () =>
        HttpResponse.json([
          {
            id: 'c-1',
            nombre: 'Caja Mostrador Conkal',
            descripcion: 'Mostrador principal',
            estatus: 'Activo',
            sucursales: 1,
            canales: 2,
            usuarios: 3,
          },
        ]),
      ),
    );
    render(<BandejaCajas />, { wrapper: createQueryWrapper() });

    await waitFor(() =>
      expect(screen.getByText('Caja Mostrador Conkal')).toBeInTheDocument(),
    );
    expect(screen.getByText(/1 suc\./)).toBeInTheDocument();
    expect(screen.getByText('Activa')).toBeInTheDocument();

    screen.getByRole('button', { name: /Nueva caja/i }).click();
    expect(abrirSpy).toHaveBeenCalledTimes(1);
  });

  it('empty: comodines explicados', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/cajas', () => HttpResponse.json([])),
    );
    render(<BandejaCajas />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText(/Sin cajas/i)).toBeInTheDocument());
  });

  it('estado error con retry', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/cajas', () =>
        HttpResponse.json(
          { title: 'Error interno', status: 500 },
          { status: 500 },
        ),
      ),
    );
    render(<BandejaCajas />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(
        screen.getByText(/No se pudieron cargar las cajas/i),
      ).toBeInTheDocument(),
    );
  });
});
