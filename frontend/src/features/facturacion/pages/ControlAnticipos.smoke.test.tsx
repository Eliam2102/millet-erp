import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ControlAnticipos } from '@/features/facturacion/pages/ControlAnticipos';
import { useAuthStore } from '@/lib/auth/auth-store';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
  useNavigate: () => () => {},
  useSearch: () => ({}),
}));

const abrirSpy = vi.fn();
vi.mock('@/features/facturacion/components/nuevo-anticipo-context', () => ({
  useNuevoAnticipo: () => ({ abrir: abrirSpy, cerrar: () => {}, setDirty: () => {} }),
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
    permisos: ['facturacion.anticipos.leer', 'facturacion.anticipos.emitir'],
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

describe('<ControlAnticipos> — smoke', () => {
  it('renderiza el reporte (fila) y el botón Nuevo anticipo', async () => {
    mswServer.use(
      http.get('*/api/v1/facturacion/anticipos/control', () =>
        HttpResponse.json({
          titulo: 'Control de Anticipos',
          generadoEn: '2026-05-30T10:00:00Z',
          filtrosAplicados: {},
          columnas: [
            { clave: 'folio', etiqueta: 'Folio', tipo: 'texto' },
            { clave: 'cliente', etiqueta: 'Cliente', tipo: 'texto' },
            { clave: 'saldo', etiqueta: 'Saldo', tipo: 'moneda' },
          ],
          filas: [
            {
              anticipoId: 'a-1',
              folio: 'FANT-1',
              cliente: 'Cliente Demo SA',
              obra: null,
              tipoAnticipo: 'ClientesMxp',
              moneda: 'MXN',
              montoCobrado: 1160,
              montoAmortizado: 0,
              saldo: 1160,
              estado: 'Abierto',
              pedidoOrigenRef: null,
              fechaEmision: '2026-05-30T09:00:00Z',
            },
          ],
          totales: { saldo: 1160 },
        }),
      ),
    );
    render(<ControlAnticipos />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByText('Cliente Demo SA')).toBeInTheDocument(),
    );
    expect(screen.getByText('FANT-1')).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: /Nuevo anticipo/i }),
    ).toBeInTheDocument();
  });
});
