import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper, createTestQueryClient } from '@/test/test-query-client';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { adminKeys } from '../api/keys';
import { toast } from 'sonner';
import { MiEmpresaPage } from './MiEmpresaPage';

vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    to,
    params,
  }: {
    children: React.ReactNode;
    to: string;
    params?: { id: string };
  }) => <a href={to.replace('$id', params?.id ?? '')}>{children}</a>,
}));

const empresa = {
  id: 'e-1',
  rfc: 'MIL010101ABC',
  razonSocial: 'Millet prueba',
  regimenFiscal: '601',
  nombreComercial: null,
  tasaIvaDefault: null,
  codigoPostal: null,
  activa: true,
  version: 2,
  calle: 'Calle de prueba',
  numeroExterior: '10',
  colonia: 'Centro',
  ciudad: 'Mérida',
  municipio: 'Mérida',
  estado: 'Yucatán',
  pais: 'México',
};
let datosEmpresa: Omit<typeof empresa, 'codigoPostal'> & { codigoPostal: string | null } = {
  ...empresa,
};
const pac = {
  proveedorNombre: 'FiscalAPI',
  activo: true,
  apiKeyConfigured: true,
  ultimaTestConexionAt: '2026-10-07T12:00:00Z',
  ultimaTestConexionExitosa: false,
  csdConfigurado: true,
  csdEstado: 'ProximoAVencer',
  csdNotBefore: '2022-10-20T12:00:00Z',
  csdNotAfter: '2026-10-20T12:00:00Z',
};

beforeEach(() => {
  datosEmpresa = { ...empresa };
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test',
    currentEmpresaId: 'e-1',
    permisos: [
      P.AdminEmpresasLeer,
      P.AdminEmpresasEditar,
      P.IntegracionesFiscalLeer,
      P.AdminSeriesGestionar,
      P.AdminEmpresasSucursalesGestionar,
    ],
  });
  mswServer.use(
    http.get('*/api/v1/admin/empresas/e-1', () =>
      HttpResponse.json(
        {
          empresa: datosEmpresa,
          sucursales: [{ id: 's-1', clave: 'CON', nombre: 'Conkal', claveAw: 'AW-CON' }],
          departamentos: [],
        },
        { headers: { ETag: '"2"' } },
      ),
    ),
    http.get('*/api/v1/integraciones/fiscal/configuracion/e-1/1', () => HttpResponse.json(pac)),
  );
});
afterEach(() => useAuthStore.getState().clearSession());

it('muestra las tres secciones, estados persistidos y enlaces, sin alta ni listado de empresas', async () => {
  render(<MiEmpresaPage />, { wrapper: createQueryWrapper() });
  expect(await screen.findByRole('heading', { name: 'Mi empresa' })).toBeVisible();
  expect(screen.getByRole('heading', { name: 'Datos fiscales' })).toBeVisible();
  expect(screen.getByLabelText('Calle')).toHaveValue('Calle de prueba');
  expect(screen.getByLabelText('RFC')).toHaveAttribute('readonly');
  expect(await screen.findByText('Próximo a vencer')).toBeVisible();
  expect(screen.getByText('Fallida')).toBeVisible();
  expect(screen.getByText('AW-CON')).toBeVisible();
  expect(screen.getByRole('link', { name: 'Configurar Conkal' })).toHaveAttribute(
    'href',
    '/admin/sucursales/s-1',
  );
  expect(screen.getByRole('link', { name: 'Integraciones fiscales' })).toHaveAttribute(
    'href',
    '/admin/integraciones/fiscal',
  );
  expect(
    screen.queryByRole('button', { name: /crear|desactivar|nueva empresa/i }),
  ).not.toBeInTheDocument();
});

it('edita CP con If-Match e Idempotency-Key y conserva el domicilio', async () => {
  let body: Record<string, unknown> | undefined;
  mswServer.use(
    http.patch('*/api/v1/admin/empresas/e-1', async ({ request }) => {
      expect(request.headers.get('If-Match')).toBe('"2"');
      expect(request.headers.get('Idempotency-Key')).toBeTruthy();
      body = (await request.json()) as Record<string, unknown>;
      datosEmpresa = { ...datosEmpresa, codigoPostal: '97000', version: 3 };
      return HttpResponse.json({ ...datosEmpresa, codigoPostal: '97000' });
    }),
  );
  render(<MiEmpresaPage />, { wrapper: createQueryWrapper() });
  fireEvent.change(await screen.findByLabelText('Código postal fiscal'), {
    target: { value: '97000' },
  });
  fireEvent.click(screen.getByRole('button', { name: 'Guardar cambios' }));
  await waitFor(() => expect(body?.codigoPostal).toBe('97000'));
  expect(body?.calle).toBe('Calle de prueba');
  expect(body).not.toHaveProperty('rfc');
});

it('sin lectura bloquea la página, sin edición deshabilita los campos', async () => {
  useAuthStore.setState({ permisos: [] });
  const blocked = render(<MiEmpresaPage />, { wrapper: createQueryWrapper() });
  expect(screen.getByRole('alert')).toHaveTextContent('No tienes permiso');
  expect(screen.queryByLabelText('Código postal fiscal')).not.toBeInTheDocument();
  blocked.unmount();
  useAuthStore.setState({ permisos: [P.AdminEmpresasLeer] });
  render(<MiEmpresaPage />, { wrapper: createQueryWrapper() });
  expect(await screen.findByLabelText('Código postal fiscal')).toBeDisabled();
  expect(screen.queryByRole('button', { name: 'Guardar cambios' })).not.toBeInTheDocument();
  expect(
    screen.getByText(/se requiere permiso para consultar Integraciones fiscales/),
  ).toBeVisible();
});

it.each(['Vigente', 'Vencido'])('presenta CSD %s desde el backend', async (estado) => {
  mswServer.use(
    http.get('*/api/v1/integraciones/fiscal/configuracion/e-1/1', () =>
      HttpResponse.json({ ...pac, csdEstado: estado }),
    ),
  );
  render(<MiEmpresaPage />, { wrapper: createQueryWrapper() });
  expect(await screen.findByText(estado)).toBeVisible();
});

it('presenta configuración ausente sin inventar fechas', async () => {
  mswServer.use(
    http.get(
      '*/api/v1/integraciones/fiscal/configuracion/e-1/1',
      () => new HttpResponse(null, { status: 404 }),
    ),
  );
  render(<MiEmpresaPage />, { wrapper: createQueryWrapper() });
  expect(await screen.findAllByText('Sin configurar')).toHaveLength(2);
  expect(screen.getByText('Sin prueba registrada')).toBeVisible();
});

it('error de PAC no aparece como sin configurar', async () => {
  mswServer.use(
    http.get(
      '*/api/v1/integraciones/fiscal/configuracion/e-1/1',
      () => new HttpResponse(null, { status: 500 }),
    ),
  );
  render(<MiEmpresaPage />, { wrapper: createQueryWrapper() });
  expect(await screen.findByRole('button', { name: 'Reintentar' })).toBeVisible();
  expect(screen.queryByText('Sin configurar')).not.toBeInTheDocument();
});

it('pagina las sucursales en grupos de diez', async () => {
  mswServer.use(
    http.get('*/api/v1/admin/empresas/e-1', () =>
      HttpResponse.json({
        empresa,
        departamentos: [],
        sucursales: Array.from({ length: 11 }, (_, i) => ({
          id: `s-${i}`,
          nombre: `Sucursal ${i}`,
          clave: `S${i}`,
          claveAw: `AW-${i}`,
        })),
      }),
    ),
  );
  render(<MiEmpresaPage />, { wrapper: createQueryWrapper() });
  expect(await screen.findByText('1–10 de 11')).toBeVisible();
  expect(screen.queryByText('AW-10')).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Siguiente' }));
  expect(screen.getByText('AW-10')).toBeVisible();
  expect(screen.getByRole('button', { name: 'Siguiente' })).toBeDisabled();
});

it('consulta el catálogo SAT para editar el régimen fiscal', async () => {
  useAuthStore.setState({
    permisos: [P.AdminEmpresasLeer, P.AdminEmpresasEditar, P.CompartidoCatalogosLeer],
  });
  mswServer.use(
    http.get('*/api/v1/catalogos/regimenes-fiscales', () =>
      HttpResponse.json([
        {
          id: 'r-601',
          codigo: '601',
          nombre: 'General de Ley Personas Morales',
          aplicaPersonaFisica: false,
          estatus: 0,
        },
      ]),
    ),
    http.get('*/api/v1/catalogos/impuestos*', () => HttpResponse.json([])),
  );
  render(<MiEmpresaPage />, { wrapper: createQueryWrapper() });
  expect(await screen.findByText('601 · General de Ley Personas Morales')).toBeVisible();
  expect(screen.getByRole('combobox', { name: 'Seleccionar régimen fiscal' })).not.toBeDisabled();
});

it('un conflicto conserva la edición y explica que debe recargar', async () => {
  const aviso = vi.spyOn(toast, 'error');
  mswServer.use(
    http.patch('*/api/v1/admin/empresas/e-1', () =>
      HttpResponse.json(
        { title: 'Conflicto', status: 409, code: 'CONCURRENCY_CONFLICT' },
        { status: 409 },
      ),
    ),
  );
  render(<MiEmpresaPage />, { wrapper: createQueryWrapper() });
  fireEvent.change(await screen.findByLabelText('Código postal fiscal'), {
    target: { value: '97000' },
  });
  fireEvent.click(screen.getByRole('button', { name: 'Guardar cambios' }));
  await waitFor(() =>
    expect(aviso).toHaveBeenCalledWith(
      'La empresa cambió mientras editabas.',
      expect.objectContaining({ description: expect.stringContaining('Recarga') }),
    ),
  );
  expect(screen.getByLabelText('Código postal fiscal')).toHaveValue('97000');
  aviso.mockRestore();
});

it('una recarga en segundo plano conserva valores y versión de la edición iniciada', async () => {
  const client = createTestQueryClient();
  let versionEnviada: string | null = null;
  mswServer.use(
    http.patch('*/api/v1/admin/empresas/e-1', ({ request }) => {
      versionEnviada = request.headers.get('If-Match');
      return HttpResponse.json(datosEmpresa);
    }),
  );
  render(<MiEmpresaPage />, { wrapper: createQueryWrapper(client) });
  fireEvent.change(await screen.findByLabelText('Código postal fiscal'), {
    target: { value: '97000' },
  });
  datosEmpresa = {
    ...datosEmpresa,
    version: 3,
    razonSocial: 'Empresa actualizada por otra persona',
  };
  await client.invalidateQueries({ queryKey: adminKeys.empresa('e-1') });
  await screen.findByText(/Empresa actualizada por otra persona/);
  expect(screen.getByLabelText('Código postal fiscal')).toHaveValue('97000');
  fireEvent.click(screen.getByRole('button', { name: 'Guardar cambios' }));
  await waitFor(() => expect(versionEnviada).toBe('"2"'));
});
