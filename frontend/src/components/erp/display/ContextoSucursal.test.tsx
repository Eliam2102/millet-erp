import { render, screen } from '@testing-library/react';
import { afterEach, it, expect } from 'vitest';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { ContextoSucursal } from './ContextoSucursal';
const initial = useAuthStore.getState();
afterEach(() => useAuthStore.setState(initial, true));
it('usa la selección del topbar y advierte cuando son todas', async () => {
  useAuthStore.setState({ currentSucursalId: null, currentEmpresaId: null });
  const view = render(<ContextoSucursal />, { wrapper: createQueryWrapper() });
  expect(screen.getByText('Todas las sucursales')).toHaveClass('bg-warning-bg');
  view.unmount();
  useAuthStore.setState({ currentSucursalId: 's-1', currentEmpresaId: 'e-1' });
  mswServer.use(
    http.get('*/api/auth/sucursales', () => HttpResponse.json([{ id: 's-1', nombre: 'Centro' }])),
  );
  render(<ContextoSucursal />, { wrapper: createQueryWrapper() });
  expect(await screen.findByText('Sucursal: Centro')).toBeInTheDocument();
});
