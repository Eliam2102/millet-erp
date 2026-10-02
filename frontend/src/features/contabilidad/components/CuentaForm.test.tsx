import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { toast } from 'sonner';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { conPermisos, limpiarAuth } from '@/features/centros-costo/pages/__fixtures__/configuracion-test-harness';
import { CuentaForm } from './CuentaForm';

vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

const vacia = () => http.get('*/api/v1/contabilidad/cuentas', () => HttpResponse.json({ items: [], total: 0, offset: 0, limit: 50 }));
const problem = (status: number, body: Record<string, unknown>) =>
  HttpResponse.json({ title: 'x', status, ...body }, { status, headers: { 'Content-Type': 'application/problem+json' } });

function llenar() {
  fireEvent.change(screen.getByLabelText(/^Código\*?$/), { target: { value: 'FIX-001' } });
  fireEvent.change(screen.getByLabelText(/Nombre/), { target: { value: 'FIX Caja' } });
}

beforeEach(() => {
  conPermisos(['contabilidad.catalogo.administrar']);
  vi.mocked(toast.success).mockClear();
  mswServer.use(vacia());
});
afterEach(limpiarAuth);

describe('<CuentaForm> alta', () => {
  it('regresión «destroy is not a function»: un onDirtyChange que devuelve valor no rompe el efecto', () => {
    const { unmount } = render(<CuentaForm onGuardada={vi.fn()} onCancelar={vi.fn()} onDirtyChange={() => true as unknown as void} />, { wrapper: createQueryWrapper() });
    llenar();
    expect(() => unmount()).not.toThrow();
  });

  it('validación de cliente: campos requeridos vacíos no envían y muestran el error', async () => {
    const post = vi.fn();
    mswServer.use(http.post('*/api/v1/contabilidad/cuentas', () => { post(); return HttpResponse.json({}, { status: 201 }); }));
    render(<CuentaForm onGuardada={vi.fn()} onCancelar={vi.fn()} />, { wrapper: createQueryWrapper() });
    fireEvent.click(screen.getByRole('button', { name: 'Crear cuenta' }));
    expect(await screen.findByText('El código es requerido.')).toBeInTheDocument();
    expect(post).not.toHaveBeenCalled();
  });

  it('422: el formulario CONSERVA los datos, marca el campo y NO muestra toast de éxito', async () => {
    mswServer.use(http.post('*/api/v1/contabilidad/cuentas', () =>
      problem(422, { code: 'CONTAB_CUENTA_CODIGO_INVALIDO', detail: 'Código inválido: contiene caracteres no permitidos.' })));
    const onGuardada = vi.fn();
    render(<CuentaForm onGuardada={onGuardada} onCancelar={vi.fn()} />, { wrapper: createQueryWrapper() });
    llenar();
    fireEvent.change(screen.getByLabelText(/Grupo de reporte/), { target: { value: 'FIX-GRUPO' } });
    fireEvent.click(screen.getByRole('button', { name: 'Crear cuenta' }));

    expect((await screen.findAllByText(/caracteres no permitidos/)).length).toBeGreaterThan(0);
    expect(screen.getByLabelText(/^Código\*?$/)).toHaveValue('FIX-001');
    expect(screen.getByLabelText(/Nombre/)).toHaveValue('FIX Caja');
    expect(screen.getByLabelText(/Grupo de reporte/)).toHaveValue('FIX-GRUPO');
    expect(onGuardada).not.toHaveBeenCalled();
    expect(toast.success).not.toHaveBeenCalled();
  });

  it('guardando: botón bloqueado; guardado: toast solo tras 201 y Idempotency-Key, naturaleza/tipo pendientes como null', async () => {
    let liberar!: () => void;
    const gate = new Promise<void>((r) => (liberar = r));
    let capturado: { key: string | null; body: unknown } | null = null;
    mswServer.use(http.post('*/api/v1/contabilidad/cuentas', async ({ request }) => {
      capturado = { key: request.headers.get('Idempotency-Key'), body: await request.json() };
      await gate;
      return HttpResponse.json({ id: 'n', codigo: 'FIX-001', version: 1 }, { status: 201 });
    }));
    const onGuardada = vi.fn();
    render(<CuentaForm onGuardada={onGuardada} onCancelar={vi.fn()} />, { wrapper: createQueryWrapper() });
    llenar();
    fireEvent.click(screen.getByRole('button', { name: 'Crear cuenta' }));

    const guardando = await screen.findByRole('button', { name: 'Guardando…' });
    expect(guardando).toBeDisabled();
    expect(toast.success).not.toHaveBeenCalled(); // nada optimista

    liberar();
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Cuenta creada'));
    expect(onGuardada).toHaveBeenCalled();
    expect(capturado!.key).toBeTruthy();
    expect(capturado!.body).toMatchObject({ codigo: 'FIX-001', nombre: 'FIX Caja', naturaleza: null, tipo: null, padreId: null });
  });

  it('fallo recuperable (500): mensaje de reintento y datos conservados', async () => {
    mswServer.use(http.post('*/api/v1/contabilidad/cuentas', () => problem(500, { title: 'Boom' })));
    render(<CuentaForm onGuardada={vi.fn()} onCancelar={vi.fn()} />, { wrapper: createQueryWrapper() });
    llenar();
    fireEvent.click(screen.getByRole('button', { name: 'Crear cuenta' }));
    expect(await screen.findByText(/Tus datos se conservan: reintenta/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Nombre/)).toHaveValue('FIX Caja');
    expect(screen.getByRole('button', { name: 'Crear cuenta' })).toBeEnabled();
  });
});
