import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { PortalProveedoresPage } from './PortalProveedoresPage';

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });

function montar() {
  return render(
    <QueryClientProvider client={new QueryClient()}>
      <PortalProveedoresPage />
    </QueryClientProvider>,
  );
}

beforeEach(() => sessionStorage.clear());
afterEach(() => vi.unstubAllGlobals());

it('sin sesión del ERP muestra el acceso por correo y no redirige a /login', () => {
  montar();
  expect(screen.getByLabelText('Correo autorizado')).toBeTruthy();
  expect(screen.getByRole('button', { name: 'Enviar código' })).toBeTruthy();
});

it('pide código, entra con el código y muestra la factura detenida por falta de complemento', async () => {
  const fetchMock = vi.fn(async (url: string) => {
    if (url.endsWith('/codigo')) return new Response(null, { status: 202 });
    if (url.endsWith('/sesion')) return json({ token: 'tok', expiraEn: '2026-10-30T20:00:00Z' });
    if (url.endsWith('/facturas')) {
      return json([{
        id: 'f1', folio: 'A-10', uuid: 'U-1', fecha: '2026-10-01T00:00:00Z', total: 116, moneda: 'MXN',
        estado: 'Detenida: falta el complemento de pago', detenida: true,
        motivo: 'Falta el complemento de pago de la factura A-10', fechaProgramadaPago: null,
      }]);
    }
    return json([], 404);
  });
  vi.stubGlobal('fetch', fetchMock);
  montar();

  fireEvent.change(screen.getByLabelText('Correo autorizado'), { target: { value: 'ventas@prov.mx' } });
  fireEvent.click(screen.getByRole('button', { name: 'Enviar código' }));
  fireEvent.change(await screen.findByLabelText('Código de acceso'), { target: { value: '123456' } });
  fireEvent.click(screen.getByRole('button', { name: 'Entrar' }));

  expect(await screen.findByText('Falta el complemento de pago de la factura A-10')).toBeTruthy();
  expect(sessionStorage.getItem('millet.portal-proveedores.token')).toBe('tok');
  const llamada = fetchMock.mock.calls.find(([u]) => String(u).endsWith('/facturas'));
  expect((llamada?.[1] as RequestInit).headers).toEqual({ Authorization: 'Bearer tok' });
});

it('un código inválido muestra el error y no abre sesión', async () => {
  vi.stubGlobal('fetch', vi.fn(async (url: string) =>
    url.endsWith('/codigo') ? new Response(null, { status: 202 }) : json({}, 401)));
  montar();

  fireEvent.change(screen.getByLabelText('Correo autorizado'), { target: { value: 'ventas@prov.mx' } });
  fireEvent.click(screen.getByRole('button', { name: 'Enviar código' }));
  fireEvent.change(await screen.findByLabelText('Código de acceso'), { target: { value: '000000' } });
  fireEvent.click(screen.getByRole('button', { name: 'Entrar' }));

  await waitFor(() => expect(screen.getByRole('alert').textContent).toContain('no es válido'));
  expect(sessionStorage.getItem('millet.portal-proveedores.token')).toBeNull();
});
