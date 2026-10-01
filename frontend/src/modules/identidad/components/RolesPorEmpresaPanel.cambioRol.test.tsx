import '@testing-library/jest-dom/vitest';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { RolesPorEmpresaPanel } from '@/modules/identidad/components/RolesPorEmpresaPanel';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Aviso de pérdida de permisos personalizados al cambiar/revocar el rol
 * (ADR-0053): las excepciones del usuario en esa empresa se borran.
 */
const ASIGNACION = {
  id: 'uer-1',
  empresaId: 'e-1',
  empresaRfc: 'BLO902640MHI',
  rolId: 'r-1',
  rolCodigo: 'operador',
  fechaAsignacion: '2026-05-01T12:00:00Z',
};

function mockEfectivos(concedidos: number, denegados: number) {
  const estado = { llamadas: 0 };
  mswServer.use(
    http.get('*/api/v1/identidad/usuarios/u-objetivo/empresas/e-1/permisos', () => {
      estado.llamadas += 1;
      return HttpResponse.json({
        usuarioId: 'u-objetivo',
        empresaId: 'e-1',
        rolId: 'r-1',
        rolCodigo: 'operador',
        rolEsSuperAdmin: false,
        permisos: [],
        concedidos,
        denegados,
      });
    }),
  );
  return estado;
}

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    user: { id: 'u-admin', email: 'a@b.com', nombre: 'Admin' },
    currentEmpresaId: 'e-1',
    permisos: [PermisosCanonicos.IdentidadAsignacionesAdministrar, PermisosCanonicos.IdentidadUsuariosLeer],
  });
});

afterEach(() => {
  useAuthStore.setState({ status: 'idle', accessToken: null, user: null, currentEmpresaId: null, permisos: [] });
});

describe('<RolesPorEmpresaPanel> — aviso de permisos personalizados', () => {
  it('al revocar avisa cuántos permisos personalizados se perderán', async () => {
    mockEfectivos(2, 1);
    render(<RolesPorEmpresaPanel usuarioId="u-objetivo" asignaciones={[ASIGNACION]} />, {
      wrapper: createQueryWrapper(),
    });

    fireEvent.click(screen.getByRole('button', { name: /revocar rol operador/i }));

    expect(await screen.findByText(/se perderán 3 permiso\(s\) personalizado\(s\)/i)).toBeInTheDocument();
  });

  it('sin excepciones no muestra el aviso', async () => {
    const estado = mockEfectivos(0, 0);
    render(<RolesPorEmpresaPanel usuarioId="u-objetivo" asignaciones={[ASIGNACION]} />, {
      wrapper: createQueryWrapper(),
    });

    fireEvent.click(screen.getByRole('button', { name: /revocar rol operador/i }));

    await screen.findByText(/confirmas revocar el rol/i);
    await waitFor(() => expect(estado.llamadas).toBeGreaterThan(0));
    expect(screen.queryByText(/se perderán/i)).not.toBeInTheDocument();
  });
});
