import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { FormasPagoPage } from '@/modules/catalogos/components/FormasPagoPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Smoke del Grupo 3 (SAT read-only) — usamos FormasPago como exemplar.
 * Valida banner amarillo + tabla + ausencia de botones de mutación.
 */
vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    to,
    ...rest
  }: {
    children: React.ReactNode;
    to: string;
    [key: string]: unknown;
  }) => (
    <a href={to} {...rest}>
      {children}
    </a>
  ),
}));

const ITEMS = [
  { id: 'fp-1', claveSat: '01', descripcion: 'Efectivo', activa: true },
  {
    id: 'fp-2',
    claveSat: '03',
    descripcion: 'Transferencia electrónica',
    activa: true,
  },
];

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'a@b.com', nombre: 'Admin' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos: [PermisosCanonicos.CompartidoCatalogosLeer],
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

describe('<FormasPagoPage> — smoke (SAT read-only)', () => {
  it('muestra banner SAT + tabla y NO botón crear/editar', async () => {
    mswServer.use(
      http.get('*/api/v1/catalogos/formas-pago', () => HttpResponse.json(ITEMS)),
    );

    render(<FormasPagoPage />, { wrapper: createQueryWrapper() });

    await waitFor(() => expect(screen.getByText('Efectivo')).toBeInTheDocument());

    expect(
      screen.getByText(/catálogo sat mantenido vía migración/i),
    ).toBeInTheDocument();
    expect(screen.getByText('Transferencia electrónica')).toBeInTheDocument();

    expect(screen.queryByRole('button', { name: /nuevo/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /nueva/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /editar/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /desactivar/i })).not.toBeInTheDocument();
  });
});
