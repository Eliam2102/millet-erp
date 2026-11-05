import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { PortalProveedoresPage } from './PortalProveedoresPage';

// Las fechas se prueban en la zona de Mérida: en UTC el corrimiento de día no se notaría.
process.env.TZ = 'America/Merida';

const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });

function montar() {
  return render(
    <QueryClientProvider client={new QueryClient()}>
      <PortalProveedoresPage />
    </QueryClientProvider>,
  );
}

beforeEach(() => { sessionStorage.clear(); window.history.replaceState(null, '', '/'); });
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
        fechaVencimiento: '2026-11-15',
      }, {
        id: 'f2', folio: 'A-11', uuid: 'U-2', fecha: '2026-10-02T00:00:00Z', total: 50, moneda: 'MXN',
        estado: 'Pagada', detenida: false, motivo: null, fechaProgramadaPago: null,
        fechaVencimiento: '2026-11-16', fechaPago: '2026-11-04',
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
  expect(screen.getByText('Sin programar')).toBeTruthy();
  expect(screen.getByText('15/11/2026')).toBeTruthy();
  expect(screen.getByText('01/10/2026')).toBeTruthy(); // la fecha del CFDI, no el día anterior
  expect(screen.getByText('Pagada el 04/11/2026')).toBeTruthy(); // F-CXP-03: la pagada dice cuándo se pagó
  expect(sessionStorage.getItem('millet.portal-proveedores.token')).toBe('tok');
  // resumen, aviso con la acción concreta y correo en la cabecera
  expect(screen.getByRole('region', { name: 'Facturas detenidas' }).textContent).toContain('sube el complemento en la pestaña');
  expect(screen.getByText('Detenidas').parentElement?.parentElement?.textContent).toContain('1');
  expect(screen.getByText('ventas@prov.mx')).toBeTruthy();
  expect(screen.getByRole('button', { name: /Cerrar sesión/ })).toBeTruthy();
  const llamada = fetchMock.mock.calls.find(([u]) => String(u).endsWith('/facturas'));
  expect((llamada?.[1] as RequestInit).headers).toEqual({ Authorization: 'Bearer tok' });
});

it('reenviar código avisa del límite de 3 por hora sin revelar si el correo existe (F-CXP-04)', async () => {
  vi.stubGlobal('fetch', vi.fn(async () => new Response(null, { status: 202 })));
  montar();

  fireEvent.change(screen.getByLabelText('Correo autorizado'), { target: { value: 'ventas@prov.mx' } });
  fireEvent.click(screen.getByRole('button', { name: 'Enviar código' }));
  fireEvent.click(await screen.findByRole('button', { name: 'Reenviar código' }));

  expect((await screen.findByRole('status')).textContent).toMatch(/Si el correo está autorizado.*máximo 3 códigos por hora/);
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

it('sube el XML y distingue «Recibimos tu documento» de «Ya lo teníamos»; muestra el rechazo por RFC', async () => {
  const respuestas = [
    json({ resultado: 'Recibido', uuid: 'U', mensaje: 'Recibimos tu documento. Ligamos tu complemento a tu pago.' }, 201),
    json({ resultado: 'YaLoTeniamos', uuid: 'U', mensaje: 'Ya lo teníamos.' }),
    json({ code: 'PORTAL_RFC_NO_COINCIDE', detail: 'El RFC del emisor del CFDI no coincide con el de tu empresa.' }, 422),
  ];
  sessionStorage.setItem('millet.portal-proveedores.token', 'tok');
  const fetchMock = vi.fn(async (url: string) => (url.endsWith('/facturas') ? json([]) : respuestas.shift()!));
  vi.stubGlobal('fetch', fetchMock);
  montar();

  fireEvent.mouseDown(await screen.findByRole('tab', { name: 'Subir factura o complemento' }));
  const enviar = async () => {
    fireEvent.change(screen.getByLabelText('Archivo XML'), { target: { files: [new File(['<x/>'], 'f.xml', { type: 'text/xml' })] } });
    fireEvent.click(screen.getByRole('button', { name: 'Subir documento' }));
  };
  await enviar();
  expect(await screen.findByText('Recibimos tu documento. Ligamos tu complemento a tu pago.')).toBeTruthy();
  await enviar();
  expect(await screen.findByText('Ya lo teníamos.')).toBeTruthy();
  await enviar();
  expect(await screen.findByText('El RFC del emisor del CFDI no coincide con el de tu empresa. Revisa el RFC del XML y carga un documento de tu empresa.')).toBeTruthy();
  const post = fetchMock.mock.calls.find(([, init]) => (init as RequestInit | undefined)?.method === 'POST');
  expect((post?.[1] as RequestInit).headers).toEqual({ Authorization: 'Bearer tok' });
  expect((post?.[1] as RequestInit).body).toBeInstanceOf(FormData);
});

it('la sección activa vive en ?seccion= y los listados vacíos explican qué hacer', async () => {
  sessionStorage.setItem('millet.portal-proveedores.token', 'tok');
  window.history.replaceState(null, '', '/?seccion=subir');
  vi.stubGlobal('fetch', vi.fn(async () => json([])));
  montar();
  expect(await screen.findByRole('tab', { name: 'Subir factura o complemento', selected: true })).toBeTruthy();
  fireEvent.mouseDown(screen.getByRole('tab', { name: 'Mis facturas' }));
  expect(await screen.findByText('Todavía no tenemos facturas tuyas.')).toBeTruthy();
  expect(window.location.search).toBe('?seccion=facturas');
});
