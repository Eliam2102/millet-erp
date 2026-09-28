import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { AuditoriaPage } from '@/modules/administracion/components/AuditoriaPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Smoke de la bandeja P2 de Auditoría (UF-Admin-PR7 §1, F1-ADM-03).
 * Valida renderizado humanizado (quién, qué hizo, registro, módulo, sucursal),
 * filtros por texto y actor, drawer de cambios legible con antesTexto/despuesTexto,
 * manejo de borrado ("Eliminado"), y botón de historial por aggregateRootId.
 */
vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    to,
    ...rest
  }: {
    children: React.ReactNode;
    to: string;
    [key: string]: unknown;
  }) => (
    <a href={to} {...rest}>
      {children}
    </a>
  ),
  useNavigate: () => () => undefined,
}));

const ENTRY_UPDATE = {
  id: 'audit-1',
  timestamp: '2026-05-13T15:30:00Z',
  usuarioId: 'u-aaaabbbb-cccc-dddd-eeee-ffff00000001',
  usuarioNombre: 'Admin Millet',
  actorNombre: 'Admin Millet',
  actorTipo: 'usuario',
  actorEmail: 'admin@millet.mx',
  entidadEtiqueta: 'REQ-0001 · Requisición Perfiles',
  resumen: 'Actualizó Requisicion REQ-0001',
  empresaId: 'e-1',
  modulo: 'Compras',
  entidad: 'Requisicion',
  entidadId: 'rq-1',
  aggregateRootId: 'root-rq-1',
  operacion: 'Actualizar',
  cambios: JSON.stringify({
    diff: {
      estado: {
        antes: 0,
        despues: 1,
        antesTexto: 'Borrador',
        despuesTexto: 'Autorizada',
      },
    },
  }),
  correlationId: 'cor-1',
};

const ENTRY_DELETE = {
  id: 'audit-2',
  timestamp: '2026-05-13T16:00:00Z',
  usuarioId: 'u-aaaabbbb-cccc-dddd-eeee-ffff00000001',
  usuarioNombre: 'Admin Millet',
  actorNombre: 'Admin Millet',
  actorTipo: 'usuario',
  actorEmail: 'admin@millet.mx',
  entidadEtiqueta: 'EMP-0012 · Juana Pérez',
  resumen: 'Eliminó Empleado EMP-0012',
  empresaId: 'e-1',
  modulo: 'Administracion',
  entidad: 'Empleado',
  entidadId: 'emp-1',
  aggregateRootId: 'emp-1',
  operacion: 'Eliminar',
  cambios: JSON.stringify({
    snapshot_pre_borrado: {
      nombre: 'Juana Pérez',
      activo: true,
    },
    snapshotTexto: {
      activo: 'Sí',
    },
  }),
  correlationId: 'cor-2',
};

const EMPRESA_E1 = {
  id: 'e-1',
  rfc: 'MIL010101ABC',
  razonSocial: 'Millet S.A. de C.V.',
  nombreComercial: 'Millet',
  regimenFiscal: '601',
  activa: true,
  version: 1,
};

const USUARIO_U1 = {
  id: 'u-aaaabbbb-cccc-dddd-eeee-ffff00000001',
  email: 'admin@millet.mx',
  entraOid: 'oid-1',
  nombre: 'Admin Millet',
  departamentoId: null,
  activo: true,
  version: 1,
};

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'a@b.com', nombre: 'Admin' },
    empresas: [],
    currentEmpresaId: 'e-1',
    currentSucursalId: null,
    permisos: [
      PermisosCanonicos.AdminAuditoriaLeer,
      PermisosCanonicos.AdminEmpresasLeer,
    ],
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

describe('<AuditoriaPage> — smoke', () => {
  it('renderiza con rango default y dispara la tabla con un row', async () => {
    mswServer.use(
      http.get('*/api/v1/admin/auditoria', () =>
        HttpResponse.json({ items: [ENTRY_UPDATE], total: 1 }),
      ),
      http.get('*/api/v1/admin/empresas', () =>
        HttpResponse.json({ items: [EMPRESA_E1], total: 1 }),
      ),
      http.get('*/api/v1/identidad/usuarios/admin', () =>
        HttpResponse.json({ items: [USUARIO_U1], total: 1 }),
      ),
    );

    render(<AuditoriaPage />, { wrapper: createQueryWrapper() });

    // Header.
    expect(screen.getByText('Bitácora de auditoría')).toBeInTheDocument();
    // Rango default debería estar populado (inputs con value).
    const desde = screen.getByLabelText(/desde/i) as HTMLInputElement;
    const hasta = screen.getByLabelText(/hasta/i) as HTMLInputElement;
    expect(desde.value).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    expect(hasta.value).toMatch(/^\d{4}-\d{2}-\d{2}$/);

    // Tabla con columnas humanizadas — esperamos a que la query resuelva.
    await waitFor(() =>
      expect(screen.getByText('Admin Millet')).toBeInTheDocument(),
    );
    expect(screen.getByText('REQ-0001 · Requisición Perfiles')).toBeInTheDocument();
    expect(screen.getByText('Actualizó Requisicion REQ-0001')).toBeInTheDocument();
    expect(screen.getByText('Compras')).toBeInTheDocument();
  });

  it('al hacer click en "Ver" abre el drawer con la tabla de cambios antes/después', async () => {
    mswServer.use(
      http.get('*/api/v1/admin/auditoria', () =>
        HttpResponse.json({ items: [ENTRY_UPDATE], total: 1 }),
      ),
      http.get('*/api/v1/admin/empresas', () =>
        HttpResponse.json({ items: [EMPRESA_E1], total: 1 }),
      ),
      http.get('*/api/v1/identidad/usuarios/admin', () =>
        HttpResponse.json({ items: [USUARIO_U1], total: 1 }),
      ),
    );

    render(<AuditoriaPage />, { wrapper: createQueryWrapper() });

    await waitFor(() =>
      expect(screen.getByText('REQ-0001 · Requisición Perfiles')).toBeInTheDocument(),
    );

    fireEvent.click(screen.getByRole('button', { name: /ver detalle/i }));

    // Drawer abierto: tabla de campos antes/después (sin JSON crudo ni números enums).
    await waitFor(() => {
      expect(screen.getByTestId('auditoria-cambios-diff')).toBeInTheDocument();
    });

    const tabla = screen.getByTestId('auditoria-cambios-diff');
    expect(tabla.textContent).toMatch(/Borrador/);
    expect(tabla.textContent).toMatch(/Autorizada/);
    // Etiqueta de campo humanizada, no la clave cruda "estado".
    expect(tabla.textContent).toMatch(/Estado/);
    // No debe filtrarse sintaxis JSON al usuario.
    expect(tabla.textContent).not.toMatch(/[{}"]/);

    // Botón para ver historial de este registro disponible
    expect(
      screen.getByRole('button', { name: /ver historial de este registro/i }),
    ).toBeInTheDocument();
  });

  it('en evento de borrado, la columna "Después" muestra "Eliminado"', async () => {
    mswServer.use(
      http.get('*/api/v1/admin/auditoria', () =>
        HttpResponse.json({ items: [ENTRY_DELETE], total: 1 }),
      ),
      http.get('*/api/v1/admin/empresas', () =>
        HttpResponse.json({ items: [EMPRESA_E1], total: 1 }),
      ),
      http.get('*/api/v1/identidad/usuarios/admin', () =>
        HttpResponse.json({ items: [USUARIO_U1], total: 1 }),
      ),
    );

    render(<AuditoriaPage />, { wrapper: createQueryWrapper() });

    await waitFor(() =>
      expect(screen.getByText('EMP-0012 · Juana Pérez')).toBeInTheDocument(),
    );

    fireEvent.click(screen.getByRole('button', { name: /ver detalle/i }));

    await waitFor(() => {
      expect(screen.getByTestId('auditoria-cambios-diff')).toBeInTheDocument();
    });

    const tabla = screen.getByTestId('auditoria-cambios-diff');
    expect(tabla.textContent).toMatch(/Juana Pérez/);
    expect(tabla.textContent).toMatch(/Eliminado/);
  });

  it('el botón "Ver historial de este registro" activa el filtro por aggregateRootId', async () => {
    let aggregateRootIdConsultado: string | null = null;
    mswServer.use(
      http.get('*/api/v1/admin/auditoria', ({ request }) => {
        aggregateRootIdConsultado = new URL(request.url).searchParams.get('aggregateRootId');
        return HttpResponse.json({ items: [ENTRY_UPDATE], total: 1 });
      }),
      http.get('*/api/v1/admin/empresas', () =>
        HttpResponse.json({ items: [EMPRESA_E1], total: 1 }),
      ),
      http.get('*/api/v1/identidad/usuarios/admin', () =>
        HttpResponse.json({ items: [USUARIO_U1], total: 1 }),
      ),
    );

    render(<AuditoriaPage />, { wrapper: createQueryWrapper() });

    await waitFor(() =>
      expect(screen.getByText('REQ-0001 · Requisición Perfiles')).toBeInTheDocument(),
    );

    fireEvent.click(screen.getByRole('button', { name: /ver detalle/i }));

    await waitFor(() => {
      expect(
        screen.getByRole('button', { name: /ver historial de este registro/i }),
      ).toBeInTheDocument();
    });

    fireEvent.click(
      screen.getByRole('button', { name: /ver historial de este registro/i }),
    );

    // Debe mostrar banner de filtro activo y consultar el aggregateRootId
    await waitFor(() => {
      expect(
        screen.getByText(/mostrando solo el historial del registro seleccionado/i),
      ).toBeInTheDocument();
    });
    expect(aggregateRootIdConsultado).toBe('root-rq-1');

    // Quitar filtro
    fireEvent.click(screen.getByRole('button', { name: /quitar filtro de registro/i }));
    await waitFor(() => {
      expect(
        screen.queryByText(/mostrando solo el historial del registro seleccionado/i),
      ).not.toBeInTheDocument();
    });
  });

  it('rango > 90 días desactiva el botón "Aplicar" y muestra mensaje', async () => {
    mswServer.use(
      http.get('*/api/v1/admin/auditoria', () =>
        HttpResponse.json({ items: [], total: 0 }),
      ),
      http.get('*/api/v1/admin/empresas', () =>
        HttpResponse.json({ items: [EMPRESA_E1], total: 1 }),
      ),
      http.get('*/api/v1/identidad/usuarios/admin', () =>
        HttpResponse.json({ items: [USUARIO_U1], total: 1 }),
      ),
    );

    render(<AuditoriaPage />, { wrapper: createQueryWrapper() });

    const desde = screen.getByLabelText(/desde/i) as HTMLInputElement;
    const hasta = screen.getByLabelText(/hasta/i) as HTMLInputElement;
    fireEvent.change(desde, { target: { value: '2025-01-01' } });
    fireEvent.change(hasta, { target: { value: '2025-12-31' } });

    await waitFor(() =>
      expect(screen.getByText('Máximo 90 días.')).toBeInTheDocument(),
    );
    const aplicar = screen.getByRole('button', { name: /aplicar/i });
    expect(aplicar).toBeDisabled();
  });

  it('usa la sucursal activa de la sesión como filtro inicial (01-05)', async () => {
    const SUCURSAL_ACTIVA_ID = 'suc-mty-1';
    useAuthStore.setState({ currentSucursalId: SUCURSAL_ACTIVA_ID });

    let sucursalIdEnviado: string | null = null;
    mswServer.use(
      http.get('*/api/auth/sucursales', () =>
        HttpResponse.json([
          { id: SUCURSAL_ACTIVA_ID, clave: 'MTY', nombre: 'Monterrey' },
          { id: 'suc-cdmx-1', clave: 'CDMX', nombre: 'Ciudad de México' },
        ]),
      ),
      http.get('*/api/v1/admin/auditoria', ({ request }) => {
        sucursalIdEnviado = new URL(request.url).searchParams.get('sucursalId');
        return HttpResponse.json({ items: [], total: 0 });
      }),
      http.get('*/api/v1/admin/empresas', () =>
        HttpResponse.json({ items: [EMPRESA_E1], total: 1 }),
      ),
      http.get('*/api/v1/identidad/usuarios/admin', () =>
        HttpResponse.json({ items: [], total: 0 }),
      ),
    );

    render(<AuditoriaPage />, { wrapper: createQueryWrapper() });

    await waitFor(() => expect(sucursalIdEnviado).toBe(SUCURSAL_ACTIVA_ID));

    const sucursalSelect = screen.getByLabelText('Sucursal') as HTMLSelectElement;
    await waitFor(() => expect(sucursalSelect.value).toBe(SUCURSAL_ACTIVA_ID));
  });
});
