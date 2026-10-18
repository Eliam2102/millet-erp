import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { toast } from 'sonner';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { F1AClientesPanel } from '@/modules/datos-maestros/components/F1AClientesPanel';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

// Sin <Toaster/> montado, sonner no pinta nada; se espía el módulo para
// verificar los avisos de éxito y error.
vi.mock('sonner', () => ({
  toast: {
    success: vi.fn(),
    warning: vi.fn(),
    error: vi.fn(),
    info: vi.fn(),
  },
}));

vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    to,
    className,
    ...rest
  }: {
    children: React.ReactNode;
    to: string;
    className?: string;
    [key: string]: unknown;
  }) => (
    <a href={to} className={className} {...rest}>
      {children}
    </a>
  ),
}));

const LIGADO_SIN_CANDIDATOS = {
  id: 'l-1',
  referenciaExterna: '990001',
  rfc: 'EAW261012AB1',
  razonSocialAw: 'E2E AW',
  version: 3,
  candidatos: [],
};

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'a@b.com', nombre: 'Admin' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos: [PermisosCanonicos.DatosMaestrosClientesGestionar],
  } as never);
  mswServer.use(
    http.get('*/api/v1/datos-maestros/clasificaciones-cliente', () =>
      HttpResponse.json([
        { id: 'c-1', nombre: 'Constructora', descripcion: 'Por validar', activa: true, version: 1 },
      ]),
    ),
    http.get('*/api/v1/datos-maestros/clientes/ligados-aw', () =>
      HttpResponse.json([LIGADO_SIN_CANDIDATOS]),
    ),
  );
});

afterEach(() => {
  mswServer.resetHandlers();
  vi.clearAllMocks();
  useAuthStore.setState({
    status: 'idle',
    accessToken: null,
    expiresAt: null,
    user: null,
    empresas: [],
    currentEmpresaId: null,
    permisos: [],
  } as never);
});

describe('F1AClientesPanel — ligados y catálogo', () => {
  it('(M-61) "Revisar ligado" muestra el aviso; "Aprobar ligado revisado" hace POST a /revisar con {id, clienteId, version}, toast y cierra el aviso', async () => {
    const LIGADO_CON_CANDIDATO = {
      ...LIGADO_SIN_CANDIDATOS,
      candidatos: [{ id: 'm-1', clave: 'CLI-0001', razonSocial: 'Cliente Manual' }],
    };
    let body: unknown = null;
    let llamadas = 0;
    mswServer.use(
      http.get('*/api/v1/datos-maestros/clientes/ligados-aw', () =>
        HttpResponse.json([LIGADO_CON_CANDIDATO]),
      ),
      http.post('*/api/v1/datos-maestros/clientes/ligados-aw/:id/revisar', async ({ request }) => {
        llamadas += 1;
        body = await request.json();
        return new HttpResponse(null, { status: 204 });
      }),
    );

    render(<F1AClientesPanel />, { wrapper: createQueryWrapper() });

    fireEvent.click(await screen.findByRole('button', { name: 'Revisar ligado' }));
    const aviso = screen.getByRole('note');
    expect(aviso).toHaveTextContent('Aprobar ligado revisado');
    fireEvent.click(screen.getByRole('button', { name: 'Aprobar ligado revisado' }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Cambio guardado'));
    expect(llamadas).toBe(1);
    expect(body).toEqual({ id: 'l-1', clienteId: 'm-1', version: 3 });
    expect(screen.queryByRole('note')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Mantener separados/ })).not.toBeInTheDocument();
  });

  it('(b) "Guardar clasificación" está deshabilitado con el nombre vacío y se habilita al escribir', async () => {
    render(<F1AClientesPanel />, { wrapper: createQueryWrapper() });

    const guardar = await screen.findByRole('button', { name: 'Guardar clasificación' });
    expect(guardar).toBeDisabled();

    fireEvent.change(screen.getByLabelText('Nueva clasificación'), { target: { value: 'Mayorista' } });
    expect(guardar).toBeEnabled();
  });

  it('(d) doble clic sincrónico en "Guardar clasificación" envía una sola petición POST', async () => {
    let llamadas = 0;
    mswServer.use(
      http.post('*/api/v1/datos-maestros/clasificaciones-cliente', async () => {
        llamadas += 1;
        await new Promise(resolve => setTimeout(resolve, 50));
        return HttpResponse.json({ id: 'c-2' }, { status: 200 });
      }),
    );

    render(<F1AClientesPanel />, { wrapper: createQueryWrapper() });

    const input = await screen.findByLabelText('Nueva clasificación');
    fireEvent.change(input, { target: { value: 'Mayorista' } });
    const guardar = screen.getByRole('button', { name: 'Guardar clasificación' });
    fireEvent.click(guardar);
    fireEvent.click(guardar);

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Cambio guardado'));
    expect(llamadas).toBe(1);
  });

  it('(c) 500 en el POST de clasificación muestra toast.error y conserva el texto del input', async () => {
    mswServer.use(
      http.post('*/api/v1/datos-maestros/clasificaciones-cliente', () =>
        new HttpResponse(null, { status: 500 }),
      ),
    );

    render(<F1AClientesPanel />, { wrapper: createQueryWrapper() });

    const input = await screen.findByLabelText('Nueva clasificación');
    fireEvent.change(input, { target: { value: 'Mayorista' } });
    fireEvent.click(screen.getByRole('button', { name: 'Guardar clasificación' }));

    await waitFor(() => expect(toast.error).toHaveBeenCalled());
    expect(input).toHaveValue('Mayorista');
  });
});
