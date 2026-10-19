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

  it('extranjero: oculta el RFC y manda origen y retención en el mismo POST, sin segunda llamada', async () => {
    let body: Record<string, unknown> | null = null;
    let llamadasClasificacion = 0;
    mswServer.use(
      http.post('*/api/v1/catalogos/proveedores', async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(
          { id: 'p-ext', clave: 'EXT-01', estatus: 2, posibleDuplicadoDeId: null },
          { status: 201 },
        );
      }),
      http.put('*/api/v1/datos-maestros/proveedores/:id/clasificacion-fiscal', () => {
        llamadasClasificacion += 1;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    render(
      <NuevoProveedorProvider>
        <AbrirAlMontar />
      </NuevoProveedorProvider>,
      { wrapper: createQueryWrapper() },
    );

    await screen.findByPlaceholderText('P-001');
    fireEvent.change(screen.getByPlaceholderText('P-001'), { target: { value: 'EXT-01' } });
    fireEvent.change(screen.getByPlaceholderText('Acme S.A. de C.V.'), { target: { value: 'Glass Corp LLC' } });
    expect(screen.getByPlaceholderText('ACM010101ABC')).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Origen'), { target: { value: '1' } });
    await waitFor(() => expect(screen.queryByPlaceholderText('ACM010101ABC')).not.toBeInTheDocument());
    expect(screen.getByText(/XEXX010101000/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /crear proveedor/i }));

    await waitFor(() => expect(body).not.toBeNull());
    expect(body).toMatchObject({ clave: 'EXT-01', rfc: null, origen: 1, regimenFiscalCodigo: null });
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Proveedor EXT-01 creado'));
    expect(llamadasClasificacion).toBe(0);
  });

  it('nacional: sin RFC no envía el alta y marca el campo', async () => {
    let posts = 0;
    mswServer.use(
      http.post('*/api/v1/catalogos/proveedores', () => {
        posts += 1;
        return HttpResponse.json({ id: 'x', clave: 'x', estatus: 1, posibleDuplicadoDeId: null }, { status: 201 });
      }),
    );
    render(
      <NuevoProveedorProvider>
        <AbrirAlMontar />
      </NuevoProveedorProvider>,
      { wrapper: createQueryWrapper() },
    );
    await screen.findByPlaceholderText('P-001');
    fireEvent.change(screen.getByPlaceholderText('P-001'), { target: { value: 'NAC-01' } });
    fireEvent.change(screen.getByPlaceholderText('Acme S.A. de C.V.'), { target: { value: 'Nacional SA' } });
    fireEvent.change(screen.getByLabelText('Origen'), { target: { value: '0' } });
    fireEvent.click(screen.getByRole('button', { name: /crear proveedor/i }));
    expect(await screen.findByText('El RFC debe tener 12 o 13 caracteres')).toBeInTheDocument();
    expect(posts).toBe(0);
  });
});
