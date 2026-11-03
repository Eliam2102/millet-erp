import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { EmpleadoInlineForm } from '@/modules/administracion/components/EmpleadoInlineForm';
import { EmpleadoAccesoPanel } from '@/modules/administracion/components/EmpleadoAccesoPanel';
import { useAuthStore } from '@/lib/auth/auth-store';

/**
 * 86e3x6cpw · fix PR #270: la lista de dominios corporativos sólo protege la
 * creación de cuentas nuevas. Una cuenta que ya existe (directorio o ERP) se
 * liga aunque su dominio no esté en la lista, con aviso informativo.
 * Datos ficticios.
 */
const ESPERA = { timeout: 5000 };
const AVISO = 'Cuenta existente fuera de los dominios corporativos; se ligará tal cual.';

function validacion(cuentaExiste: boolean) {
  return {
    correo: 'persona.invitada@externo.example',
    dominioPermitido: false,
    cuentaEntra: cuentaExiste
      ? { objectId: 'oid-9', nombreMostrado: 'Persona Invitada', habilitada: true }
      : null,
    usuarioErp: null,
    empleadoVinculado: null,
    puedeVincularCuentaExistente: cuentaExiste,
    puedeCrearCuentaNueva: false,
    motivoBloqueo: cuentaExiste ? null : 'ENTRA_DOMINIO_NO_PERMITIDO',
  };
}

function usarValidacion(cuentaExiste: boolean) {
  mswServer.use(
    http.get('*/api/v1/identidad/directorio-entra/validar-correo*', () =>
      HttpResponse.json(validacion(cuentaExiste)),
    ),
  );
}

beforeEach(() => {
  sessionStorage.clear();
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'admin@millet.mx', nombre: 'Admin' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos: [
      'identidad.usuarios.crear',
      'identidad.asignaciones.administrar',
      'compartido.catalogos.leer',
    ],
    errorMessage: null,
  });
  mswServer.use(
    http.get('*/api/v1/admin/empleados/siguiente-clave', () =>
      HttpResponse.json({ siguienteClave: 'EMP-017' }),
    ),
    http.get('*/api/v1/identidad/roles*', () =>
      HttpResponse.json({ items: [{ id: 'rol-1', nombre: 'Vendedor', activo: true }] }),
    ),
    http.get('*/api/v1/catalogos/*', () =>
      HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 }),
    ),
    http.get('*/api/v1/admin/empresas/sucursales/*', () => HttpResponse.json({ items: [] })),
  );
});

afterEach(() => {
  sessionStorage.clear();
  useAuthStore.setState({ permisos: [], accessToken: null, status: 'idle' });
});

describe('<EmpleadoInlineForm> · dominio en el paso Acceso (86e3x6cpw)', () => {
  function abrirPasoAcceso(acceso: 1 | 2) {
    sessionStorage.setItem(
      'millet_empleado_wizard_draft_v1',
      JSON.stringify({
        values: {
          clave: 'EMP-017',
          nombre: 'Persona Invitada',
          sucursalId: 's-1',
          departamentoId: 'd-1',
          puestoId: 'p-1',
          email: 'persona.invitada@millet.mx',
        },
        paso: 2,
        acceso,
        rolId: 'rol-1',
        emailPrefix: 'persona.invitada',
        emailDomain: 'millet.mx',
        emailContacto: 'contacto@externo.example',
      }),
    );
    render(<EmpleadoInlineForm onCancel={vi.fn()} />, { wrapper: createQueryWrapper() });
  }

  it('cuenta existente fuera de la lista: aviso informativo y deja continuar', async () => {
    usarValidacion(true);
    abrirPasoAcceso(1);
    expect(await screen.findByText(AVISO, {}, ESPERA)).toBeInTheDocument();
    expect(screen.queryByText(/Dominio no permitido/)).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /siguiente/i }));
    expect(await screen.findByText(/revisa antes de crear/i, {}, ESPERA)).toBeInTheDocument();
  });

  it('cuenta nueva con dominio fuera de la lista: sigue bloqueando', async () => {
    usarValidacion(false);
    abrirPasoAcceso(2);
    expect(
      await screen.findByText(/Dominio no permitido para cuenta corporativa/, {}, ESPERA),
    ).toBeInTheDocument();
    expect(screen.queryByText(AVISO)).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /siguiente/i }));
    await waitFor(() => expect(screen.queryByText(/revisa antes de crear/i)).toBeNull());
  });
});

describe('<EmpleadoAccesoPanel> · dominio al dar acceso (86e3x6cpw)', () => {
  function abrirPanel() {
    render(
      <EmpleadoAccesoPanel
        empleadoId="emp-1"
        usuarioId={null}
        email="persona.invitada@millet.mx"
        empleadoActivo={true}
        sucursalId="s-1"
        departamentoId="d-1"
        puestoId="p-1"
      />,
      { wrapper: createQueryWrapper() },
    );
  }

  it('«Ya tiene cuenta Microsoft» con cuenta existente fuera de la lista: aviso, sin error', async () => {
    usarValidacion(true);
    abrirPanel();
    expect(await screen.findByText(AVISO, {}, ESPERA)).toBeInTheDocument();
    expect(screen.getByText('Persona Invitada')).toBeInTheDocument();
    expect(screen.queryByText(/Dominio no permitido/)).not.toBeInTheDocument();
  });

  it('«Cuenta Microsoft nueva» con dominio fuera de la lista: muestra el error de dominio', async () => {
    usarValidacion(false);
    abrirPanel();
    fireEvent.change(screen.getByLabelText(/modo de acceso/i), { target: { value: '2' } });
    expect(
      await screen.findByText(/Dominio no permitido para cuenta corporativa/, {}, ESPERA),
    ).toBeInTheDocument();
    expect(screen.queryByText(AVISO)).not.toBeInTheDocument();
  });
});
