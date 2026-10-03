import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { toast } from 'sonner';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { conPermisos, limpiarAuth } from '@/features/centros-costo/pages/__fixtures__/configuracion-test-harness';

vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => <a href={to}>{children}</a>,
}));

import { CuentaDetallePage } from './CuentaDetallePage';

const LEER = 'contabilidad.catalogo.leer';
const ADMIN = 'contabilidad.catalogo.administrar';

const cuenta = (o: Record<string, unknown> = {}) => ({
  id: 'c1', codigo: 'FIX-110', nombre: 'FIX Caja', padreId: 'c0', nivel: 2, naturaleza: 'Deudora', tipo: 'Afectable',
  estatus: 'Activo', activa: true, cuentaControl: 'Ninguna', codigoAgrupador: null, grupoReporte: null,
  pendienteValidacion: false, version: 3, usada: false, origenes: [{ fuente: 'FIX-SAP', codigoOrigen: 'S-110' }], ...o,
});
const padre = { ...cuenta({ id: 'c0', codigo: 'FIX-100', nombre: 'FIX Activo', padreId: null, nivel: 1, tipo: 'Titulo' }) };

let puts: { ifMatch: string | null; key: string | null; body: unknown }[];

function instalar(c = cuenta()) {
  puts = [];
  mswServer.use(
    http.get('*/api/v1/contabilidad/cuentas/c1', () => HttpResponse.json(c, { headers: { ETag: `"${c.version}"` } })),
    http.get('*/api/v1/contabilidad/cuentas/c0', () => HttpResponse.json(padre, { headers: { ETag: '"1"' } })),
    http.get('*/api/v1/contabilidad/cuentas', () => HttpResponse.json({ items: [], total: 0, offset: 0, limit: 200 })),
  );
}
const problem = (status: number, body: Record<string, unknown>) =>
  HttpResponse.json({ title: 'x', status, ...body }, { status, headers: { 'Content-Type': 'application/problem+json' } });

function put(responder: () => Response) {
  mswServer.use(http.put('*/api/v1/contabilidad/cuentas/c1', async ({ request }) => {
    puts.push({ ifMatch: request.headers.get('If-Match'), key: request.headers.get('Idempotency-Key'), body: await request.json() });
    return responder();
  }));
}

async function abrirEdicion() {
  render(<CuentaDetallePage id="c1" />, { wrapper: createQueryWrapper() });
  fireEvent.click(await screen.findByRole('button', { name: /Editar/ }));
  await screen.findByRole('form', { name: 'Editar cuenta' });
}

beforeEach(() => {
  conPermisos([LEER, ADMIN]);
  vi.mocked(toast.success).mockClear();
});
afterEach(limpiarAuth);

describe('<CuentaDetallePage>', () => {
  it('cargando: skeleton; luego datos, ancestros y orígenes', async () => {
    instalar();
    render(<CuentaDetallePage id="c1" />, { wrapper: createQueryWrapper() });
    expect(screen.getByTestId('detalle-cargando')).toBeInTheDocument();
    expect(await screen.findByText('Sin cuentas hijas.')).toBeInTheDocument();
    expect(await screen.findByRole('link', { name: /FIX-100/ })).toBeInTheDocument(); // ancestro
    expect(screen.getByText('S-110')).toBeInTheDocument(); // origen
  });

  it('naturaleza y tipo nulos se muestran como "Pendiente de validación" en el detalle', async () => {
    instalar(cuenta({ naturaleza: null, tipo: null, pendienteValidacion: true }));
    render(<CuentaDetallePage id="c1" />, { wrapper: createQueryWrapper() });
    await screen.findByLabelText('Datos de la cuenta');
    expect(screen.getAllByText('Pendiente de validación').length).toBeGreaterThanOrEqual(2);
    expect(screen.getByText('Naturaleza: Pendiente')).toBeInTheDocument();
  });

  it('fallo recuperable: error al cargar ofrece Reintentar', async () => {
    mswServer.use(http.get('*/api/v1/contabilidad/cuentas/c1', () => problem(500, { title: 'Boom' })));
    render(<CuentaDetallePage id="c1" />, { wrapper: createQueryWrapper() });
    expect(await screen.findByRole('button', { name: 'Reintentar' })).toBeInTheDocument();
  });

  it('sin permiso de administrar: no se renderiza Editar ni Desactivar', async () => {
    conPermisos([LEER]);
    instalar();
    render(<CuentaDetallePage id="c1" />, { wrapper: createQueryWrapper() });
    await screen.findByLabelText('Datos de la cuenta');
    expect(screen.queryByRole('button', { name: /Editar/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Desactivar' })).not.toBeInTheDocument();
  });

  it('cuenta usada: candado con la explicación y padre/naturaleza/tipo bloqueados en la edición', async () => {
    instalar(cuenta({ usada: true }));
    await abrirEdicion();
    expect(screen.getByTestId('aviso-usada')).toHaveTextContent(/ya tiene movimientos/);
    expect(screen.getByLabelText('Naturaleza')).toBeDisabled();
    // P19: el tipo es informativo (lo calcula el sistema): solo lectura siempre.
    expect(screen.getByLabelText('Tipo')).toHaveAttribute('readonly');
    expect(screen.getByLabelText('Cuenta padre')).toBeDisabled();
    expect(screen.getByLabelText(/Nombre/)).toBeEnabled(); // el nombre sí se puede editar
  });

  it('guardado: PUT con If-Match del ETag e Idempotency-Key; toast solo tras 200', async () => {
    instalar();
    put(() => HttpResponse.json(cuenta({ nombre: 'FIX Caja 2', version: 4 }), { headers: { ETag: '"4"' } }));
    await abrirEdicion();
    fireEvent.change(screen.getByLabelText(/Nombre/), { target: { value: 'FIX Caja 2' } });
    fireEvent.click(screen.getByRole('button', { name: 'Guardar cambios' }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Cuenta actualizada'));
    expect(puts[0].ifMatch).toBe('"3"');
    expect(puts[0].key).toBeTruthy();
    // El tipo no se envía (lo calcula el sistema).
    expect(puts[0].body).toMatchObject({ nombre: 'FIX Caja 2', naturaleza: 'Deudora', tipo: null });
  });

  it('422 CAMBIO_BLOQUEADO_POR_USO: muestra el motivo, conserva el borrador y no hay toast de éxito', async () => {
    instalar();
    put(() => problem(422, { code: 'CONTAB_CUENTA_CAMBIO_BLOQUEADO_POR_USO', detail: 'La cuenta FIX-110 ya tiene movimientos; cambiar el tipo alteraría saldos.' }));
    await abrirEdicion();
    fireEvent.change(screen.getByLabelText(/Nombre/), { target: { value: 'FIX Borrador' } });
    fireEvent.change(screen.getByLabelText('Naturaleza'), { target: { value: 'Acreedora' } });
    fireEvent.click(screen.getByRole('button', { name: 'Guardar cambios' }));
    expect((await screen.findAllByText(/alteraría saldos/)).length).toBeGreaterThan(0);
    expect(screen.getByLabelText(/Nombre/)).toHaveValue('FIX Borrador');
    expect(toast.success).not.toHaveBeenCalled();
  });

  it('conflicto 409: diálogo de recarga, el borrador se conserva y recargar vuelve a consultar sin sobrescribir', async () => {
    instalar();
    put(() => problem(409, { code: 'CONCURRENCY_CONFLICT', title: 'Conflicto de concurrencia' }));
    await abrirEdicion();
    fireEvent.change(screen.getByLabelText(/Nombre/), { target: { value: 'FIX Mi borrador' } });
    fireEvent.click(screen.getByRole('button', { name: 'Guardar cambios' }));

    const refrescar = await screen.findByRole('button', { name: 'Refrescar y revisar' });
    expect(puts).toHaveLength(1);

    // Tras recargar, otra versión en el servidor: el borrador del formulario NO se pierde ni se envía solo.
    mswServer.use(http.get('*/api/v1/contabilidad/cuentas/c1', () =>
      HttpResponse.json(cuenta({ nombre: 'FIX Cambio ajeno', version: 5 }), { headers: { ETag: '"5"' } })));
    fireEvent.click(refrescar);
    await waitFor(() => expect(screen.getAllByText('FIX Cambio ajeno').length).toBeGreaterThan(0));
    expect(screen.getByLabelText(/Nombre/)).toHaveValue('FIX Mi borrador');
    expect(puts).toHaveLength(1);
    expect(toast.success).not.toHaveBeenCalled();
  });

  it('428 sin If-Match: mismo diálogo de recarga (se matchea por status)', async () => {
    instalar();
    put(() => problem(428, { title: 'If-Match requerido' }));
    await abrirEdicion();
    fireEvent.click(screen.getByRole('button', { name: 'Guardar cambios' }));
    expect(await screen.findByRole('button', { name: 'Refrescar y revisar' })).toBeInTheDocument();
  });

  it('baja lógica: confirma con mensaje de efectos y toast solo tras 2xx', async () => {
    instalar();
    let ifMatch: string | null = null;
    mswServer.use(http.post('*/api/v1/contabilidad/cuentas/c1/desactivar', ({ request }) => {
      ifMatch = request.headers.get('If-Match');
      return HttpResponse.json(cuenta({ activa: false, estatus: 'Inactivo', version: 4 }));
    }));
    render(<CuentaDetallePage id="c1" />, { wrapper: createQueryWrapper() });
    fireEvent.click(await screen.findByRole('button', { name: 'Desactivar' }));
    expect(await screen.findByText(/Nada se borra/)).toBeInTheDocument();
    expect(toast.success).not.toHaveBeenCalled();
    fireEvent.click(screen.getAllByRole('button', { name: 'Desactivar' }).at(-1)!);
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Cuenta desactivada'));
    expect(ifMatch).toBe('"3"');
  });

  it('baja rechazada (422 con hijas activas): mensaje en el diálogo, sin toast de éxito', async () => {
    instalar();
    mswServer.use(http.post('*/api/v1/contabilidad/cuentas/c1/desactivar', () =>
      problem(422, { code: 'CONTAB_CUENTA_BAJA_CON_HIJAS_ACTIVAS', detail: 'Tiene hijas activas.' })));
    render(<CuentaDetallePage id="c1" />, { wrapper: createQueryWrapper() });
    fireEvent.click(await screen.findByRole('button', { name: 'Desactivar' }));
    fireEvent.click(screen.getAllByRole('button', { name: 'Desactivar' }).at(-1)!);
    expect(await screen.findByText('Tiene hijas activas.')).toBeInTheDocument();
    expect(toast.success).not.toHaveBeenCalled();
  });
});
