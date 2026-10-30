import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { EmpleadoAccesoPanel } from '@/modules/administracion/components/EmpleadoAccesoPanel';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';

/**
 * 86e3x6cpw · ficha del empleado, pestaña Acceso: activar y desactivar el
 * acceso (separado de la baja laboral) y estado del grupo de Entra (contrato 4).
 */
function estado(extra: Record<string, unknown> = {}) {
  return {
    empleadoId: 'emp-1',
    usuarioId: 'u-9',
    email: 'ficticia@millet.mx',
    estadoAcceso: 0,
    motivoErrorProvision: null,
    accesoEnviadoEn: null,
    primerAccesoEn: '2026-10-01T10:00:00Z',
    emailContacto: null,
    usuarioActivo: true,
    ...extra,
  };
}

let llamadas: string[] = [];

function setUsuario(id: string, permisos: string[]) {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id, email: 'admin@millet.mx', nombre: 'Admin' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos,
    errorMessage: null,
  });
}

function renderPanel(usuarioId = 'u-9') {
  return render(
    <EmpleadoAccesoPanel
      empleadoId="emp-1"
      usuarioId={usuarioId}
      email="ficticia@millet.mx"
      empleadoActivo={true}
    />,
    { wrapper: createQueryWrapper() },
  );
}

beforeEach(() => {
  llamadas = [];
  mswServer.use(
    http.post('*/api/v1/identidad/usuarios/:id/desactivar', ({ params }) => {
      llamadas.push(`desactivar:${params.id}`);
      return HttpResponse.json({ id: params.id, activo: false });
    }),
    http.post('*/api/v1/identidad/usuarios/:id/reactivar', ({ params }) => {
      llamadas.push(`reactivar:${params.id}`);
      return HttpResponse.json({ id: params.id, activo: true });
    }),
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

describe('<EmpleadoAccesoPanel> · activar y desactivar acceso (86e3x6cpw)', () => {
  it('«Desactivar acceso» pide confirmación y llama al endpoint de la cuenta', async () => {
    setUsuario('u-1', [P.IdentidadUsuariosDesactivar, P.IdentidadUsuariosEditar]);
    mswServer.use(
      http.get('*/api/v1/admin/colaboradores/emp-1/acceso', () => HttpResponse.json(estado())),
    );
    renderPanel();

    fireEvent.click(await screen.findByRole('button', { name: 'Desactivar acceso' }));
    expect(llamadas).toHaveLength(0);
    const dialogo = await screen.findByRole('alertdialog');
    expect(dialogo).toHaveTextContent('Sigue como empleado');
    fireEvent.click(screen.getAllByRole('button', { name: 'Desactivar acceso' }).at(-1)!);

    await waitFor(() => expect(llamadas).toEqual(['desactivar:u-9']));
  });

  it('no permite desactivar la propia cuenta', async () => {
    setUsuario('u-9', [P.IdentidadUsuariosDesactivar]);
    mswServer.use(
      http.get('*/api/v1/admin/colaboradores/emp-1/acceso', () => HttpResponse.json(estado())),
    );
    renderPanel();

    expect(await screen.findByRole('button', { name: 'Desactivar acceso' })).toBeDisabled();
  });

  it('cuenta bloqueada: «Activar acceso» con confirmación', async () => {
    setUsuario('u-1', [P.IdentidadUsuariosDesactivar, P.IdentidadUsuariosEditar]);
    mswServer.use(
      http.get('*/api/v1/admin/colaboradores/emp-1/acceso', () =>
        HttpResponse.json(estado({ usuarioActivo: false })),
      ),
    );
    renderPanel();

    expect(await screen.findByText(/Usuario bloqueado/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Desactivar acceso' })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Activar acceso' }));
    await screen.findByRole('alertdialog');
    fireEvent.click(screen.getAllByRole('button', { name: 'Activar acceso' }).at(-1)!);

    await waitFor(() => expect(llamadas).toEqual(['reactivar:u-9']));
  });

  it('sin permiso de desactivar no muestra el botón', async () => {
    setUsuario('u-1', [P.IdentidadUsuariosEditar]);
    mswServer.use(
      http.get('*/api/v1/admin/colaboradores/emp-1/acceso', () => HttpResponse.json(estado())),
    );
    renderPanel();

    await screen.findByText(/Acceso activo/);
    expect(screen.queryByRole('button', { name: 'Desactivar acceso' })).not.toBeInTheDocument();
  });

  it.each([
    ['Sincronizado', 'En el grupo de acceso de Microsoft'],
    ['Pendiente', 'Pendiente de sincronizar con el grupo de acceso de Microsoft'],
    ['NoConfigurado', 'Grupo de acceso de Microsoft sin configurar'],
  ])('muestra el grupo de acceso %s', async (grupoAcceso, texto) => {
    setUsuario('u-1', []);
    mswServer.use(
      http.get('*/api/v1/admin/colaboradores/emp-1/acceso', () =>
        HttpResponse.json(estado({ grupoAcceso })),
      ),
    );
    renderPanel();
    expect(await screen.findByText(texto)).toBeInTheDocument();
  });

  it('sin grupoAcceso en la respuesta no muestra nada del grupo', async () => {
    setUsuario('u-1', []);
    mswServer.use(
      http.get('*/api/v1/admin/colaboradores/emp-1/acceso', () => HttpResponse.json(estado())),
    );
    renderPanel();
    await screen.findByText(/Acceso activo/);
    expect(screen.queryByText(/grupo de acceso/i)).not.toBeInTheDocument();
  });
});
