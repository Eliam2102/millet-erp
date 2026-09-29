import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useEffect } from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { toast } from 'sonner';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import {
  NuevoProveedorProvider,
} from '@/modules/datos-maestros/components/SheetNuevoProveedor';
import { useNuevoProveedor } from '@/modules/datos-maestros/components/nuevo-proveedor-context';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

// Sin <Toaster/> montado en el árbol de test, sonner no renderiza nada al
// DOM — se espía el módulo para verificar el aviso de "En revisión".
vi.mock('sonner', () => ({
  toast: {
    success: vi.fn(),
    warning: vi.fn(),
    error: vi.fn(),
    info: vi.fn(),
  },
}));

/**
 * F1-ADM-05 — alta de proveedor (Sheet P4): el 409
 * `PROVEEDOR_RFC_DUPLICADO` se muestra en el campo RFC, y una
 * respuesta 201 con `estatus = EnRevision` (RFC genérico + misma razón
 * social que otro proveedor) dispara un aviso adicional al toast de
 * éxito.
 */

// useNavigate necesita contexto de router; lo sustituimos por un no-op y
// conservamos el resto de los exports reales del paquete (mismo patrón que
// CapturarFacturaSheet.test.tsx).
vi.mock('@tanstack/react-router', async () => {
  const actual =
    await vi.importActual<typeof import('@tanstack/react-router')>(
      '@tanstack/react-router',
    );
  return { ...actual, useNavigate: () => () => {} };
});

function AbrirAlMontar() {
  const { abrir } = useNuevoProveedor();
  useEffect(() => {
    abrir();
  }, [abrir]);
  return null;
}

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'a@b.com', nombre: 'Admin' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos: [PermisosCanonicos.CompartidoCatalogosAdministrar],
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

function llenarYEnviar() {
  fireEvent.change(screen.getByPlaceholderText('P-001'), {
    target: { value: 'P-010' },
  });
  fireEvent.change(screen.getByPlaceholderText('Acme S.A. de C.V.'), {
    target: { value: 'Otro Proveedor SA' },
  });
  fireEvent.change(screen.getByPlaceholderText('ACM010101ABC'), {
    target: { value: 'OTR010101AAA' },
  });
  fireEvent.click(screen.getByRole('button', { name: /crear proveedor/i }));
}

describe('<SheetNuevoProveedor>', () => {
  it('409 PROVEEDOR_RFC_DUPLICADO se muestra en el campo RFC', async () => {
    mswServer.use(
      http.post('*/api/v1/catalogos/proveedores', () =>
        HttpResponse.json(
          {
            type: 'about:blank',
            title: 'Conflicto',
            status: 409,
            code: 'PROVEEDOR_RFC_DUPLICADO',
            detail:
              'Ya existe el proveedor P-002 · Otro Proveedor SA con RFC OTR010101AAA.',
          },
          {
            status: 409,
            headers: { 'Content-Type': 'application/problem+json' },
          },
        ),
      ),
    );

    render(
      <NuevoProveedorProvider>
        <AbrirAlMontar />
      </NuevoProveedorProvider>,
      { wrapper: createQueryWrapper() },
    );

    await screen.findByPlaceholderText('P-001');
    llenarYEnviar();

    await waitFor(() =>
      expect(
        screen.getByText(
          'Ya existe el proveedor P-002 · Otro Proveedor SA con RFC OTR010101AAA.',
        ),
      ).toBeInTheDocument(),
    );
  });

  it('201 con estatus EnRevision avisa posible duplicado', async () => {
    mswServer.use(
      http.post('*/api/v1/catalogos/proveedores', () =>
        HttpResponse.json(
          {
            id: 'p-nuevo',
            clave: 'P-010',
            estatus: 2, // EstatusCatalogo.EnRevision
            posibleDuplicadoDeId: 'p-002',
          },
          { status: 201 },
        ),
      ),
    );

    render(
      <NuevoProveedorProvider>
        <AbrirAlMontar />
      </NuevoProveedorProvider>,
      { wrapper: createQueryWrapper() },
    );

    await screen.findByPlaceholderText('P-001');
    llenarYEnviar();

    await waitFor(() =>
      expect(toast.warning).toHaveBeenCalledWith(
        expect.stringMatching(/P-010 quedó en revisión: posible duplicado/i),
        expect.anything(),
      ),
    );
  });
});
