import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useEffect } from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { NuevoClienteProvider } from '@/modules/datos-maestros/components/SheetNuevoCliente';
import { useNuevoCliente } from '@/modules/datos-maestros/components/nuevo-cliente-context';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

vi.mock('sonner', () => ({
  toast: {
    success: vi.fn(),
    warning: vi.fn(),
    error: vi.fn(),
    info: vi.fn(),
  },
}));

// useNavigate necesita contexto de router; se sustituye por un no-op y se
// conservan el resto de los exports reales (mismo patrón que SheetNuevoProveedor).
vi.mock('@tanstack/react-router', async () => {
  const actual =
    await vi.importActual<typeof import('@tanstack/react-router')>(
      '@tanstack/react-router',
    );
  return { ...actual, useNavigate: () => () => {} };
});

function AbrirAlMontar() {
  const { abrir } = useNuevoCliente();
  useEffect(() => {
    abrir();
  }, [abrir]);
  return null;
}

beforeEach(() => {
  // Los selectores de catálogo disparan su query al montar; lista vacía basta.
  mswServer.use(
    http.get('*/api/v1/catalogos/monedas', () => HttpResponse.json([])),
    http.get('*/api/v1/catalogos/regimenes-fiscales', () =>
      HttpResponse.json([]),
    ),
    http.get('*/api/v1/catalogos/usos-cfdi', () => HttpResponse.json([])),
    http.get('*/api/v1/catalogos/formas-pago', () => HttpResponse.json([])),
  );
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'a@b.com', nombre: 'Admin' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos: [PermisosCanonicos.DatosMaestrosClientesGestionar],
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

function renderSheet() {
  return render(
    <NuevoClienteProvider>
      <AbrirAlMontar />
    </NuevoClienteProvider>,
    { wrapper: createQueryWrapper() },
  );
}

describe('<NuevoClienteProvider> — alta manual de cliente', () => {
  it('sin clasificación no envía el POST y muestra "Elige una clasificación"', async () => {
    let posts = 0;
    mswServer.use(
      http.post('*/api/v1/datos-maestros/clientes', () => {
        posts += 1;
        return HttpResponse.json({ id: 'c-1', clave: 'C-001' }, { status: 201 });
      }),
    );

    renderSheet();

    fireEvent.click(await screen.findByRole('button', { name: /crear cliente/i }));

    // El placeholder del selector usa el mismo texto; se acota al <p role="alert">.
    await waitFor(() =>
      expect(
        screen.getByText('Elige una clasificación', { selector: 'p[role="alert"]' }),
      ).toBeInTheDocument(),
    );
    expect(posts).toBe(0);
  });

  it('muestra los campos de moneda, Tax ID y domicilio extranjero', async () => {
    renderSheet();

    await screen.findByRole('button', { name: /crear cliente/i });

    expect(screen.getByText('Moneda default')).toBeInTheDocument();
    expect(screen.getByText('Núm. reg. id. trib. (Tax ID)')).toBeInTheDocument();
    expect(screen.getByText('Domicilio extranjero — calle')).toBeInTheDocument();
  });
});
