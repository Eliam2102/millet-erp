import { beforeEach, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { InventariosPage } from './InventariosPage';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => <a href={to}>{children}</a>,
  useNavigate: () => () => {},
  useSearch: () => ({}),
}));
vi.mock('@/features/almacen/components/NuevoConteoSheet', () => ({ NuevoConteoSheet: () => null }));

beforeEach(() => {
  useAuthStore.setState({ status: 'authenticated', accessToken: 'test-token', currentEmpresaId: 'e-test', permisos: [] });
  mswServer.use(
    http.get('*/api/v1/almacen/sub-almacenes', () => HttpResponse.json({ items: [], total: 0 })),
    http.get('*/api/v1/almacen/conteos', () => HttpResponse.json({ items: [], offset: 0, limit: 10, total: 0 })),
  );
});

it('K1: muestra cero en los estados ausentes de los conteos cargados', async () => {
  mswServer.use(http.get('*/api/v1/almacen/conteos/conteos-por-estado', () => HttpResponse.json([])));
  const { container } = render(<InventariosPage />, { wrapper: createQueryWrapper() });
  await waitFor(() => {
    const valores = container.querySelectorAll('span.text-3xl');
    expect(valores.length).toBeGreaterThan(0);
    valores.forEach((valor) => expect(valor).toHaveTextContent(/^0$/));
  });
});

it.each(['carga', 'error'])('K1: conserva «—» durante %s de los conteos', async (estado) => {
  mswServer.use(http.get('*/api/v1/almacen/conteos/conteos-por-estado', () => estado === 'error'
    ? new HttpResponse(null, { status: 500 }) : new Promise(() => {})));
  const { container } = render(<InventariosPage />, { wrapper: createQueryWrapper() });
  await screen.findByRole('heading', { name: 'Inventario físico' });
  const valores = container.querySelectorAll('span.text-3xl');
  expect(valores.length).toBeGreaterThan(0);
  valores.forEach((valor) => expect(valor).toHaveTextContent('—'));
});
