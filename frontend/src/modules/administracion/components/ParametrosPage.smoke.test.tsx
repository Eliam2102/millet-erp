import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ParametrosPage } from '@/modules/administracion/components/ParametrosPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Smoke de la bandeja P1 de Parámetros (UF-Admin-PR7 §2). Mockeamos
 * los 4 tipos (Texto/Numero/Booleano/Json) para validar que cada uno
 * renderiza el editor correcto y que cambiar un valor habilita
 * "Guardar".
 */
vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    to,
    ...rest
  }: {
    children: React.ReactNode;
    to: string;
    [key: string]: unknown;
  }) => (
    <a href={to} {...rest}>
      {children}
    </a>
  ),
  useNavigate: () => () => undefined,
}));

const PARAMETROS = [
  {
    id: 'p-1',
    clave: 'sistema.nombre',
    valor: 'Millet ERP',
    tipo: 0, // Texto
    modulo: null,
    descripcion: 'Nombre visible del sistema.',
    version: 1,
  },
  {
    id: 'p-2',
    clave: 'sistema.timeout_segundos',
    valor: '30',
    tipo: 1, // Numero
    modulo: null,
    descripcion: 'Timeout default para operaciones HTTP.',
    version: 1,
  },
  {
    id: 'p-3',
    clave: 'sistema.feature_x_activo',
    valor: 'false',
    tipo: 2, // Booleano
    modulo: null,
    descripcion: 'Feature flag de la X.',
    version: 1,
  },
  {
    id: 'p-4',
    clave: 'sistema.config_json',
    valor: '{"a":1}',
    tipo: 3, // Json
    modulo: 'compras',
    descripcion: 'Configuración estructurada.',
    version: 1,
  },
];

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-1', email: 'a@b.com', nombre: 'Admin' },
    empresas: [],
    currentEmpresaId: 'e-1',
    permisos: [
      PermisosCanonicos.AdminParametrosLeer,
      PermisosCanonicos.AdminParametrosEditar,
    ],
    errorMessage: null,
  });
});

afterEach(() => {
  useAuthStore.setState({
    status: 'idle',
    accessToken: null,
    expiresAt: null,
    user: null,
    empresas: [],
    currentEmpresaId: null,
    permisos: [],
    errorMessage: null,
  });
});

describe('<ParametrosPage> — smoke', () => {
  it('renderiza los 4 parámetros con el editor correcto por tipo', async () => {
    mswServer.use(
      http.get('*/api/v1/admin/parametros', () =>
        HttpResponse.json({ items: PARAMETROS }),
      ),
    );

    render(<ParametrosPage />, { wrapper: createQueryWrapper() });

    await waitFor(() =>
      expect(screen.getByText('sistema.nombre')).toBeInTheDocument(),
    );

    // Texto → input type=text con valor.
    const texto = document.getElementById(
      'parametro-input-sistema.nombre',
    ) as HTMLInputElement;
    expect(texto).not.toBeNull();
    expect(texto.value).toBe('Millet ERP');
    expect(texto.type).toBe('text');

    // Numero → input type=number.
    const numero = document.getElementById(
      'parametro-input-sistema.timeout_segundos',
    ) as HTMLInputElement;
    expect(numero.type).toBe('number');
    expect(numero.value).toBe('30');

    // Booleano → checkbox nativo.
    const bool = document.getElementById(
      'parametro-input-sistema.feature_x_activo',
    ) as HTMLInputElement;
    expect(bool.type).toBe('checkbox');
    expect(bool.checked).toBe(false);

    // Json → textarea.
    const json = document.getElementById(
      'parametro-input-sistema.config_json',
    ) as HTMLTextAreaElement;
    expect(json.tagName).toBe('TEXTAREA');
    expect(json.value).toBe('{"a":1}');
  });

  it('cambiar valor habilita el botón Guardar y dispara PATCH', async () => {
    let patchCalled = false;
    mswServer.use(
      http.get('*/api/v1/admin/parametros', () =>
        HttpResponse.json({ items: [PARAMETROS[0]] }),
      ),
      http.patch('*/api/v1/admin/parametros/sistema.nombre', () => {
        patchCalled = true;
        return HttpResponse.json({
          ...PARAMETROS[0],
          valor: 'Millet ERP v2',
          version: 2,
        });
      }),
    );

    render(<ParametrosPage />, { wrapper: createQueryWrapper() });

    await waitFor(() =>
      expect(screen.getByText('sistema.nombre')).toBeInTheDocument(),
    );

    const input = document.getElementById(
      'parametro-input-sistema.nombre',
    ) as HTMLInputElement;

    // Antes del cambio, Guardar está deshabilitado.
    let guardar = screen.getByRole('button', { name: /^guardar$/i });
    expect(guardar).toBeDisabled();

    fireEvent.change(input, { target: { value: 'Millet ERP v2' } });

    guardar = screen.getByRole('button', { name: /^guardar$/i });
    expect(guardar).not.toBeDisabled();

    fireEvent.click(guardar);

    await waitFor(() => expect(patchCalled).toBe(true));
  });

  it('valor JSON inválido bloquea el guardado con error inline', async () => {
    mswServer.use(
      http.get('*/api/v1/admin/parametros', () =>
        HttpResponse.json({ items: [PARAMETROS[3]] }),
      ),
    );

    render(<ParametrosPage />, { wrapper: createQueryWrapper() });

    await waitFor(() =>
      expect(screen.getByText('sistema.config_json')).toBeInTheDocument(),
    );

    const json = document.getElementById(
      'parametro-input-sistema.config_json',
    ) as HTMLTextAreaElement;
    fireEvent.change(json, { target: { value: '{ broken json' } });

    const guardar = screen.getByRole('button', { name: /^guardar$/i });
    fireEvent.click(guardar);

    await waitFor(() =>
      expect(screen.getByText(/json inválido/i)).toBeInTheDocument(),
    );
  });
});
