import { afterEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ExpedienteTiposPage } from '@/modules/datos-maestros/components/ExpedienteTiposPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';

const URL_TIPOS = '*/api/v1/datos-maestros/proveedores/expediente/tipos';
const TIPO = {
  id: 't-1', codigo: 'contrato', nombre: 'Contrato', orden: 1,
  obligatorioPersonaMoral: true, obligatorioPersonaFisica: false,
  vigenciaMeses: null, soloPersonaMoral: false, aplicaOrigen: 1, activo: true, version: 7,
};

function setPermisos(permisos: string[]) {
  useAuthStore.setState({
    status: 'authenticated', accessToken: 't', expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u', email: 'a@b.com', nombre: 'A' }, empresas: [], currentEmpresaId: 'e', permisos, errorMessage: null,
  });
}
afterEach(() =>
  useAuthStore.setState({ status: 'idle', accessToken: null, expiresAt: null, user: null, empresas: [], currentEmpresaId: null, permisos: [], errorMessage: null }),
);
const pintar = () => render(<ExpedienteTiposPage />, { wrapper: createQueryWrapper() });

describe('<ExpedienteTiposPage>', () => {
  it('sin permiso de configurar es solo lectura', async () => {
    mswServer.use(http.get(URL_TIPOS, () => HttpResponse.json([TIPO])));
    setPermisos([P.DatosMaestrosProveedoresGestionar]);
    pintar();
    expect(await screen.findByText('Solo nacional')).toBeInTheDocument();
    // Columnas de la matriz: obligatorio por tipo de persona y a quién aplica.
    expect(screen.getByRole('columnheader', { name: 'Obligatorio moral' })).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Obligatorio física' })).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Aplica a' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /nuevo documento/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /editar contrato/i })).not.toBeInTheDocument();
  });

  it('con permiso edita y envía PUT con X-Expected-Version', async () => {
    mswServer.use(http.get(URL_TIPOS, () => HttpResponse.json([TIPO])));
    let version: string | null = null;
    let body: Record<string, unknown> | null = null;
    mswServer.use(
      http.put(`${URL_TIPOS}/t-1`, async ({ request }) => {
        version = request.headers.get('X-Expected-Version');
        body = (await request.json()) as Record<string, unknown>;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    setPermisos([P.DatosMaestrosProveedoresGestionar, P.DatosMaestrosProveedoresExpedienteConfigurar]);
    pintar();
    fireEvent.click(await screen.findByRole('button', { name: /editar contrato/i }));
    fireEvent.change(screen.getByLabelText('Nombre'), { target: { value: 'Contrato marco' } });
    fireEvent.click(screen.getByRole('button', { name: 'Guardar' }));
    await waitFor(() => expect(version).toBe('7'));
    expect(body).toMatchObject({
      codigo: 'contrato', nombre: 'Contrato marco', obligatorioPersonaMoral: true, obligatorioPersonaFisica: false, aplicaOrigen: 1,
    });
  });

  it('permite marcar el documento solo para extranjeros y obligatorio a físicas', async () => {
    mswServer.use(http.get(URL_TIPOS, () => HttpResponse.json([TIPO])));
    let body: Record<string, unknown> | null = null;
    mswServer.use(
      http.put(`${URL_TIPOS}/t-1`, async ({ request }) => {
        body = (await request.json()) as Record<string, unknown>;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    setPermisos([P.DatosMaestrosProveedoresGestionar, P.DatosMaestrosProveedoresExpedienteConfigurar]);
    pintar();
    fireEvent.click(await screen.findByRole('button', { name: /editar contrato/i }));
    fireEvent.change(screen.getByLabelText('Aplica a'), { target: { value: '2' } });
    fireEvent.click(screen.getByLabelText('Obligatorio para persona física'));
    fireEvent.click(screen.getByRole('button', { name: 'Guardar' }));
    await waitFor(() => expect(body).not.toBeNull());
    expect(body).toMatchObject({ aplicaOrigen: 2, obligatorioPersonaFisica: true, obligatorioPersonaMoral: true });
  });
});
