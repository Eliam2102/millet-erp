import { afterEach, describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ClienteSaldoGrupoSection } from '@/modules/datos-maestros/components/ClienteSaldoGrupoSection';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

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

const URL = '*/api/v1/cuentas-por-cobrar/cartera/saldo-grupo/c-1';

describe('ClienteSaldoGrupoSection', () => {
  afterEach(() => useAuthStore.setState({ permisos: [] }));

  it('muestra Cliente · Moneda · Saldo por miembro cuando el cliente es parte de un grupo', async () => {
    setPermisos([PermisosCanonicos.CuentasPorCobrarCarteraLeer]);
    mswServer.use(
      http.get(URL, () =>
        HttpResponse.json({
          clienteId: 'c-1',
          miembros: [
            { clienteId: 'c-1', clave: '10', razonSocial: 'PRINCIPAL SA', esPrincipal: true, monedas: [{ moneda: 'MXN', saldoNeto: 1000 }] },
            { clienteId: 'c-2', clave: '11', razonSocial: 'SECUNDARIO SA', esPrincipal: false, monedas: [{ moneda: 'USD', saldoNeto: 400 }] },
          ],
        }),
      ),
    );
    render(<ClienteSaldoGrupoSection clienteId="c-1" />, { wrapper: createQueryWrapper() });

    expect(await screen.findByText(/SECUNDARIO SA/)).toBeInTheDocument();
    expect(screen.getByText(/PRINCIPAL SA \(principal\)/)).toBeInTheDocument();
    expect(screen.getByText('USD')).toBeInTheDocument();
    expect(screen.getByText('MXN')).toBeInTheDocument();
  });

  it('no se muestra si el cliente no tiene grupo', async () => {
    setPermisos([PermisosCanonicos.CuentasPorCobrarCarteraLeer]);
    mswServer.use(http.get(URL, () => HttpResponse.json({ clienteId: 'c-1', miembros: [] })));
    const { container } = render(<ClienteSaldoGrupoSection clienteId="c-1" />, { wrapper: createQueryWrapper() });
    await new Promise((r) => setTimeout(r, 50));
    expect(container).toBeEmptyDOMElement();
  });

  it('no consulta sin permiso de cartera', () => {
    setPermisos([]);
    const { container } = render(<ClienteSaldoGrupoSection clienteId="c-1" />, { wrapper: createQueryWrapper() });
    expect(container).toBeEmptyDOMElement();
  });
});
