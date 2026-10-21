import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { NuevaCaja } from '@/features/facturacion/pages/NuevaCaja';
import { useAuthStore } from '@/lib/auth/auth-store';

vi.mock('@tanstack/react-router', () => ({ useNavigate: () => () => {} }));

// El combobox real consulta el catálogo; aquí basta un <select> con dos sucursales.
vi.mock('@/components/erp/selectors/SucursalSelector', () => ({
  SucursalSelector: (props: { value: string | null; onChange: (id: string | null) => void }) => (
    <select
      aria-label="Sucursal"
      value={props.value ?? ''}
      onChange={(e) => props.onChange(e.target.value || null)}
    >
      <option value="">Selecciona sucursal</option>
      <option value="11111111-1111-1111-1111-111111111111">CNK · Conkal</option>
    </select>
  ),
}));

beforeEach(() => {
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
  useAuthStore.setState({ status: 'idle', accessToken: null, permisos: [], user: null });
});

describe('<NuevaCaja> — sucursal obligatoria (86e3t97uy)', () => {
  it('no crea la caja sin sucursal y envía la sucursal elegida', async () => {
    const cuerpos: unknown[] = [];
    mswServer.use(
      http.post('*/api/v1/facturacion/cajas/', async ({ request }) => {
        cuerpos.push(await request.json());
        return HttpResponse.json({ id: 'c-nueva', version: 1 }, { status: 201 });
      }),
    );
    render(<NuevaCaja onClose={() => {}} onDirtyChange={() => {}} />, {
      wrapper: createQueryWrapper(),
    });

    fireEvent.change(screen.getByLabelText(/Nombre/), { target: { value: 'Caja Conkal' } });
    fireEvent.click(screen.getByRole('button', { name: 'Crear caja' }));
    expect(await screen.findByText('Selecciona la sucursal de la caja.')).toBeInTheDocument();
    expect(cuerpos).toHaveLength(0);

    fireEvent.change(screen.getByLabelText('Sucursal'), {
      target: { value: '11111111-1111-1111-1111-111111111111' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Crear caja' }));
    await waitFor(() => expect(cuerpos).toHaveLength(1));
    expect(cuerpos[0]).toMatchObject({
      nombre: 'Caja Conkal',
      sucursalId: '11111111-1111-1111-1111-111111111111',
    });
  });
});
