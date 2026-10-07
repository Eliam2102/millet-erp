import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ProveedorBancariosSection } from '@/modules/datos-maestros/components/ProveedorBancariosSection';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * F1-ADM-05 — sección "Datos bancarios" del detalle de proveedor:
 * gating por permiso (`bancarios-ver` / `bancarios-editar`) y la regla
 * de no pre-llenar la CLABE completa en el form de edición.
 */

const DATOS_BANCARIOS = {
  id: 'p-1',
  banco: 'BBVA México',
  clabe: '****9719',
  beneficiario: 'Proveedor Uno SA',
  clabeCompleta: false,
};

function setPermisos(permisos: string[]) {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'a@b.com', nombre: 'Admin' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos,
    errorMessage: null,
  });
}

beforeEach(() => {
  mswServer.use(
    http.get('*/api/v1/datos-maestros/proveedores/p-1/datos-bancarios', () =>
      HttpResponse.json(DATOS_BANCARIOS),
    ),
  );
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

describe('<ProveedorBancariosSection>', () => {
  it('no renderiza nada sin el permiso bancarios-ver', () => {
    setPermisos([]);
    const { container } = render(
      <ProveedorBancariosSection proveedorId="p-1" />,
      { wrapper: createQueryWrapper() },
    );
    expect(container).toBeEmptyDOMElement();
  });

  it('muestra los datos (CLABE enmascarada) sin botón Editar sin bancarios-editar', async () => {
    setPermisos([PermisosCanonicos.DatosMaestrosProveedoresBancariosVer]);
    render(<ProveedorBancariosSection proveedorId="p-1" />, {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() =>
      expect(screen.getByText('BBVA México')).toBeInTheDocument(),
    );
    expect(screen.getByText('****9719')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /editar/i })).not.toBeInTheDocument();
  });

  it('con bancarios-editar pero sin catalogos.administrar no ofrece Editar (la API lo rechaza)', async () => {
    setPermisos([
      PermisosCanonicos.DatosMaestrosProveedoresBancariosVer,
      PermisosCanonicos.DatosMaestrosProveedoresBancariosEditar,
    ]);
    render(<ProveedorBancariosSection proveedorId="p-1" />, {
      wrapper: createQueryWrapper(),
    });
    await screen.findByText('BBVA México');
    expect(screen.queryByRole('button', { name: /editar/i })).not.toBeInTheDocument();
  });

  it('con bancarios-editar abre el form inline sin pre-llenar la CLABE', async () => {
    setPermisos([
      PermisosCanonicos.DatosMaestrosProveedoresBancariosVer,
      PermisosCanonicos.DatosMaestrosProveedoresBancariosEditar,
      PermisosCanonicos.CompartidoCatalogosAdministrar,
    ]);
    render(<ProveedorBancariosSection proveedorId="p-1" />, {
      wrapper: createQueryWrapper(),
    });

    const editarBtn = await screen.findByRole('button', { name: /editar/i });
    fireEvent.click(editarBtn);

    // Banco sí se pre-llena (no es sensible).
    expect(screen.getByDisplayValue('BBVA México')).toBeInTheDocument();
    // La CLABE NUNCA se pre-llena, aunque el backend la haya devuelto
    // (enmascarada o no) — vacío = no cambiar.
    expect(screen.queryByDisplayValue('****9719')).not.toBeInTheDocument();
    // El checkbox para limpiarla referencia el valor enmascarado como hint.
    expect(
      screen.getByText(/quitar la clabe registrada \(\*\*\*\*9719\)/i),
    ).toBeInTheDocument();
  });
});
