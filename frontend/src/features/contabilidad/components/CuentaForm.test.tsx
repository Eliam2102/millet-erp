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

  it('cuenta padre: un solo combobox busca títulos activos y guarda el padre elegido', async () => {
    const titulo = { id: 't1', codigo: 'FIX-100', nombre: 'FIX Bancos', padreId: null, nivel: 1, naturaleza: 'Deudora', tipo: 'Titulo',
      estatus: 'Activo', activa: true, cuentaControl: 'Ninguna', codigoAgrupador: null, grupoReporte: null, pendienteValidacion: false, version: 1 };
    const consultas: URLSearchParams[] = [];
    let body: unknown = null;
    mswServer.use(
      http.get('*/api/v1/contabilidad/cuentas', ({ request }) => {
        consultas.push(new URL(request.url).searchParams);
        return HttpResponse.json({ items: [titulo], total: 1, offset: 0, limit: 50 });
      }),
      http.post('*/api/v1/contabilidad/cuentas', async ({ request }) => {
        body = await request.json();
        return HttpResponse.json({ ...titulo, id: 'n1', codigo: 'FIX-001' }, { status: 201, headers: { ETag: '"1"' } });
      }),
      http.get('*/api/v1/contabilidad/cuentas/siguiente-codigo', () => HttpResponse.json({ codigo: 'FIX-100.01', motivo: null })),
    );
    render(<CuentaForm onGuardada={vi.fn()} onCancelar={vi.fn()} />, { wrapper: createQueryWrapper() });
    llenar();

    // Un solo control etiquetado «Cuenta padre» (antes había caja de búsqueda + <select> separados).
    const trigger = screen.getByLabelText('Cuenta padre');
    expect(trigger).toHaveAttribute('role', 'combobox');
    expect(trigger).toHaveTextContent('Sin padre');
    fireEvent.click(trigger);
    fireEvent.change(await screen.findByLabelText('Buscar cuenta padre'), { target: { value: 'Bancos' } });
    // Tras el debounce, la búsqueda va al servidor: solo títulos activos con el término escrito.
    await waitFor(() =>
      expect(consultas.some((p) => p.get('tipo') === 'Titulo' && p.get('estatus') === 'Activo' && p.get('q') === 'Bancos')).toBe(true),
    );
    fireEvent.click(await screen.findByText('FIX Bancos'));
    expect(screen.getByLabelText('Cuenta padre')).toHaveTextContent('FIX-100 — FIX Bancos');

    fireEvent.click(screen.getByRole('button', { name: 'Crear cuenta' }));
    await waitFor(() => expect(body).toMatchObject({ padreId: 't1' }));
    // El código que el usuario ya había escrito NO se pisa con la sugerencia.
    expect(body).toMatchObject({ codigo: 'FIX-001' });
  });

  it('opción 2: al elegir el padre se sugiere el siguiente código (editable) y se envía tal cual', async () => {
    const titulo = { id: 't1', codigo: 'FIX-100.10.00.00', nombre: 'FIX Bancos', padreId: null, nivel: 1, naturaleza: 'Deudora', tipo: 'Titulo',
      estatus: 'Activo', activa: true, cuentaControl: 'Ninguna', codigoAgrupador: null, grupoReporte: null, pendienteValidacion: false, version: 1 };
    let pedido: string | null = null;
    let body: unknown = null;
    mswServer.use(
      http.get('*/api/v1/contabilidad/cuentas', () => HttpResponse.json({ items: [titulo], total: 1, offset: 0, limit: 50 })),
      http.get('*/api/v1/contabilidad/cuentas/siguiente-codigo', ({ request }) => {
        pedido = new URL(request.url).searchParams.get('padreId');
        return HttpResponse.json({ codigo: 'FIX-100.10.03.00', motivo: null });
      }),
      http.post('*/api/v1/contabilidad/cuentas', async ({ request }) => {
        body = await request.json();
        return HttpResponse.json({ ...titulo, id: 'n1' }, { status: 201, headers: { ETag: '"1"' } });
      }),
    );
    render(<CuentaForm onGuardada={vi.fn()} onCancelar={vi.fn()} />, { wrapper: createQueryWrapper() });
    expect(screen.getByText(/Elige primero la cuenta padre/)).toBeInTheDocument();

    fireEvent.click(screen.getByLabelText('Cuenta padre'));
    fireEvent.click(await screen.findByText('FIX Bancos'));

    await waitFor(() => expect(screen.getByLabelText(/^Código\*?$/)).toHaveValue('FIX-100.10.03.00'));
    expect(pedido).toBe('t1');
    expect(screen.getByText('Sugerido según la cuenta padre; puedes cambiarlo.')).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText(/Nombre/), { target: { value: 'FIX Banco Centro' } });
    fireEvent.click(screen.getByRole('button', { name: 'Crear cuenta' }));
    await waitFor(() => expect(body).toMatchObject({ codigo: 'FIX-100.10.03.00', padreId: 't1', nombre: 'FIX Banco Centro' }));
  });

  it('opción 2: si no se puede sugerir, explica el motivo y el código queda para escribirlo', async () => {
    const titulo = { id: 't1', codigo: 'FIX-4', nombre: 'FIX Raiz libre', padreId: null, nivel: 1, naturaleza: null, tipo: 'Titulo',
      estatus: 'Activo', activa: true, cuentaControl: 'Ninguna', codigoAgrupador: null, grupoReporte: null, pendienteValidacion: true, version: 1 };
    mswServer.use(
      http.get('*/api/v1/contabilidad/cuentas', () => HttpResponse.json({ items: [titulo], total: 1, offset: 0, limit: 50 })),
      http.get('*/api/v1/contabilidad/cuentas/siguiente-codigo', () =>
        HttpResponse.json({ codigo: null, motivo: 'FIX-4 aún no tiene cuentas hijas para deducir el formato; escriba el código.' })),
    );
    render(<CuentaForm onGuardada={vi.fn()} onCancelar={vi.fn()} />, { wrapper: createQueryWrapper() });
    fireEvent.click(screen.getByLabelText('Cuenta padre'));
    fireEvent.click(await screen.findByText('FIX Raiz libre'));
    expect(await screen.findByText(/aún no tiene cuentas hijas/)).toBeInTheDocument();
    expect(screen.getByLabelText(/^Código\*?$/)).toHaveValue('');
  });

  it('422 CONTAB_CUENTA_CODIGO_FUERA_DE_RAMA se marca en el campo Código y conserva los datos', async () => {
    mswServer.use(http.post('*/api/v1/contabilidad/cuentas', () =>
      problem(422, { code: 'CONTAB_CUENTA_CODIGO_FUERA_DE_RAMA', detail: 'El código FIX-001 no corresponde a la cuenta padre FIX-100.' })));
    render(<CuentaForm onGuardada={vi.fn()} onCancelar={vi.fn()} />, { wrapper: createQueryWrapper() });
    llenar();
    fireEvent.click(screen.getByRole('button', { name: 'Crear cuenta' }));
    expect((await screen.findAllByText(/no corresponde a la cuenta padre/)).length).toBeGreaterThan(0);
    expect(screen.getByLabelText(/^Código\*?$/)).toHaveValue('FIX-001');
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
