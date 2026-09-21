import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { AuditoriaPage } from '@/modules/administracion/components/AuditoriaPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Smoke de la bandeja P2 de Auditoría (UF-Admin-PR7 §1). El componente
 * se monta con default rango de últimos 7 días y dispara la query
 * automáticamente; mockeamos el endpoint con una fila de update usando
 * el shape real que emite <c>AuditSaveChangesInterceptor</c> —
 * <c>{ diff: { campo: { antes, despues } } }</c> — para validar que el
 * drawer renderiza la tabla de cambios.
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
  usuarioNombre: null, // PLATFORM-TODO(<AuditUsuarioEnrich>)
  empresaId: 'e-1',
  modulo: 'Compras',
  entidad: 'Requisicion',
  entidadId: 'rq-1',
  operacion: 'Actualizar',
  cambios: JSON.stringify({
    diff: { estado: { antes: 'Borrador', despues: 'Autorizada' } },
  }),
  correlationId: 'cor-1',
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

    // Tabla con la fila — esperamos a que la query resuelva.
    await waitFor(() =>
      expect(screen.getByText('Requisicion')).toBeInTheDocument(),
    );
    expect(screen.getByText('Compras')).toBeInTheDocument();
    expect(screen.getByText('Actualizar')).toBeInTheDocument();
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
      expect(screen.getByText('Requisicion')).toBeInTheDocument(),
    );

    fireEvent.click(screen.getByRole('button', { name: /ver detalle/i }));

    // Drawer abierto: tabla de campos antes/después (sin JSON crudo).
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
    // Cambiamos desde primero y después hasta — el orden no importa
    // pero ambos deben caer en un rango > 90 días.
    fireEvent.change(desde, { target: { value: '2025-01-01' } });
    fireEvent.change(hasta, { target: { value: '2025-12-31' } });

    await waitFor(() =>
      // Mensaje de validación inline (no el subtítulo del header).
      expect(screen.getByText('Máximo 90 días.')).toBeInTheDocument(),
    );
    const aplicar = screen.getByRole('button', { name: /aplicar/i });
    expect(aplicar).toBeDisabled();
  });
});
