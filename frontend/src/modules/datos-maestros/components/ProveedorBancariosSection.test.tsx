import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ProveedorBancariosSection } from '@/modules/datos-maestros/components/ProveedorBancariosSection';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import type { ActualizarDatosBancariosProveedorPayload } from '@/modules/datos-maestros/api/types';

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

  it('con bancarios-editar pero sin catalogos.administrar sí ofrece Editar (endpoint dedicado G1.9)', async () => {
    setPermisos([
      PermisosCanonicos.DatosMaestrosProveedoresBancariosVer,
      PermisosCanonicos.DatosMaestrosProveedoresBancariosEditar,
    ]);
    render(<ProveedorBancariosSection proveedorId="p-1" />, {
      wrapper: createQueryWrapper(),
    });
    await screen.findByText('BBVA México');
    expect(screen.getByRole('button', { name: /editar/i })).toBeInTheDocument();
  });

  it('con bancarios-editar abre el form inline sin pre-llenar la CLABE', async () => {
    setPermisos([
      PermisosCanonicos.DatosMaestrosProveedoresBancariosVer,
      PermisosCanonicos.DatosMaestrosProveedoresBancariosEditar,
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

  it('guarda cambios bancarios vía PATCH /api/v1/datos-maestros/proveedores/{id}/datos-bancarios', async () => {
    let patchLlamado = false;
    let payloadRecibido: ActualizarDatosBancariosProveedorPayload | null = null;

    mswServer.use(
      http.patch(
        '*/api/v1/datos-maestros/proveedores/p-1/datos-bancarios',
        async ({ request }) => {
          patchLlamado = true;
          payloadRecibido = (await request.json()) as ActualizarDatosBancariosProveedorPayload;
          return new HttpResponse(null, { status: 204 });
        },
      ),
    );

    setPermisos([
      PermisosCanonicos.DatosMaestrosProveedoresBancariosVer,
      PermisosCanonicos.DatosMaestrosProveedoresBancariosEditar,
    ]);
    render(<ProveedorBancariosSection proveedorId="p-1" />, {
      wrapper: createQueryWrapper(),
    });

    const editarBtn = await screen.findByRole('button', { name: /editar/i });
    fireEvent.click(editarBtn);

    const bancoInput = screen.getByDisplayValue('BBVA México');
    fireEvent.change(bancoInput, { target: { value: 'Santander' } });

    const guardarBtn = screen.getByRole('button', { name: /guardar cambios/i });
    fireEvent.click(guardarBtn);

    await waitFor(() => expect(patchLlamado).toBe(true));
    expect((payloadRecibido as ActualizarDatosBancariosProveedorPayload | null)?.banco).toBe('Santander');
  });
});
