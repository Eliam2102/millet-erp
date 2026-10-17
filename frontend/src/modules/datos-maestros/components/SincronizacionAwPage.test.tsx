import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { SincronizacionAwPage } from '@/modules/datos-maestros/components/SincronizacionAwPage';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => <a href={to}>{children}</a>,
  useNavigate: () => vi.fn(),
}));

const conflicto = {
  id: 'k-1', tipoEntidad: 'Cliente', entidadId: 'c-2', referenciaExterna: 'AW-62', campo: 'RFC repetido entre clientes de A+W',
  valorAntes: 'AW-61', valorDespues: 'DEMO010101AB1', dueno: 'AW', tipo: 'RfcCompartido', estado: 'Pendiente',
  causa: 'Dos clientes de A+W con la misma clave fiscal DEMO010101AB1: 62 y AW-61. No se fusionan.',
  versionEvento: 'abcdef0123456789', versionMatriz: '0-propuesta', ejecucionId: null,
  ocurridoEnUtc: '2026-10-17T10:00:00Z', resolucion: null, resueltoEnUtc: null, version: 3,
};

function setPermisos(permisos: string[]) {
  useAuthStore.setState({
    status: 'authenticated', accessToken: 'test-token', expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'a@b.com', nombre: 'Admin' }, empresas: [], currentEmpresaId: 'e-1', permisos, errorMessage: null,
  });
}

const conflictoProducto = {
  ...conflicto, id: 'p-1', tipoEntidad: 'ProductoAw', referenciaExterna: '1', campo: 'Descripción', valorAntes: 'PRUEBA ERP',
  valorDespues: 'VIDRIO FLOAT AW', tipo: 'EditadoEnErp', causa: 'El campo es de A+W y se cambió en el ERP.', version: 5,
};

describe('<SincronizacionAwPage>', () => {
  beforeEach(() => setPermisos([PermisosCanonicos.DatosMaestrosClientesSincronizar, PermisosCanonicos.DatosMaestrosProductosAwGestionar]));

  it('conflicto EditadoEnErp de productos: «Aplicar A+W» confirma y envía AplicarAw', async () => {
    let body: unknown = null;
    mswServer.use(
      http.get('*/clientes/sincronizacion/ejecuciones', () => HttpResponse.json({ items: [], offset: 0, limit: 20, total: 0 })),
      http.get('*/clientes/conflictos-aw', () => HttpResponse.json([])),
      http.get('*/productos-aw/conflictos-aw', ({ request }) =>
        HttpResponse.json(new URL(request.url).searchParams.get('estado') === 'Pendiente' ? [conflictoProducto] : [])),
      http.get('*/sincronizacion-aw/matriz', () => HttpResponse.json({ version: '0-propuesta', campos: [] })),
      http.post('*/productos-aw/conflictos-aw/p-1/resolver', async ({ request }) => {
        body = await request.json();
        return new HttpResponse(null, { status: 204 });
      }),
    );
    render(<SincronizacionAwPage />, { wrapper: createQueryWrapper() });

    fireEvent.mouseDown(await screen.findByRole('tab', { name: 'Productos' }), { button: 0 });
    expect(await screen.findByText(/ERP «PRUEBA ERP» · A\+W «VIDRIO FLOAT AW»/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Aplicar A+W' }));
    expect(screen.getByText('Se reemplazará el valor del ERP por el de A+W. Queda en la bitácora.')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Confirmar' }));
    await waitFor(() => expect(body).toEqual({ resolucion: 'AplicarAw', version: 5 }));
  });
  it('muestra el conflicto, resuelve con «Mantener separados» y muestra la versión de la matriz', async () => {
    let body: unknown = null;
    mswServer.use(
      http.get('*/clientes/sincronizacion/ejecuciones', () => HttpResponse.json({ items: [], offset: 0, limit: 20, total: 0 })),
      http.get('*/clientes/conflictos-aw', ({ request }) =>
        HttpResponse.json(new URL(request.url).searchParams.get('estado') === 'Pendiente' ? [conflicto] : [])),
      http.get('*/sincronizacion-aw/matriz', () =>
        HttpResponse.json({ version: '0-propuesta', campos: [{ entidad: 'Cliente', campo: 'Razón social', dueno: 'ERP', siDifiere: 'Se conserva el ERP.' }] })),
      http.post('*/clientes/conflictos-aw/k-1/resolver', async ({ request }) => {
        body = await request.json();
        return new HttpResponse(null, { status: 204 });
      }),
    );
    render(<SincronizacionAwPage />, { wrapper: createQueryWrapper() });

    expect(await screen.findByText(/misma clave fiscal DEMO010101AB1/)).toBeInTheDocument();
    expect(await screen.findByText(/Versión 0-propuesta/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Mantener separados' }));
    expect(screen.getByText('Los dos clientes quedan separados. Esto no fusiona ni liga identidades.')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Confirmar' }));
    await waitFor(() => expect(body).toEqual({ resolucion: 'MantenerSeparados', version: 3 }));
  });
});
