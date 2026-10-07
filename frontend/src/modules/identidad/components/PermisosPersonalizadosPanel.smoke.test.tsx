import '@testing-library/jest-dom/vitest';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { PermisosPersonalizadosPanel } from '@/modules/identidad/components/PermisosPersonalizadosPanel';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Smoke del panel "Permisos personalizados" (ADR-0053): interruptores por
 * permiso y por grupo, guardado (PUT batch), restablecer (DELETE con
 * confirmación), errores 403/409/422 como toasts y protección solo del
 * super-admin.
 */

const USUARIO = 'u-objetivo';
const EMPRESA = 'e-1';
const URL_EFECTIVOS = `*/api/v1/identidad/usuarios/${USUARIO}/empresas/${EMPRESA}/permisos`;
const URL_OVERRIDE = `*/api/v1/identidad/usuarios/${USUARIO}/empresas/${EMPRESA}/permisos-override`;

const CATALOGO = {
  items: [],
  grupos: [
    {
      modulo: 'identidad',
      items: [
        { id: 'p-leer', codigo: 'identidad.roles.leer', modulo: 'identidad', recurso: 'roles', accion: 'leer', descripcion: 'Lee roles.' },
        { id: 'p-crear', codigo: 'identidad.roles.crear', modulo: 'identidad', recurso: 'roles', accion: 'crear', descripcion: 'Crea roles.' },
        { id: 'p-editar', codigo: 'identidad.roles.editar', modulo: 'identidad', recurso: 'roles', accion: 'editar', descripcion: 'Edita roles.' },
        { id: 'p-ajeno', codigo: 'compras.ordenes.leer', modulo: 'identidad', recurso: 'ordenes', accion: 'leer', descripcion: 'Lee OC.' },
      ],
    },
  ],
};

function efectivos(permisos: unknown[], concedidos = 0, denegados = 0, extra: Record<string, unknown> = {}) {
  return {
    usuarioId: USUARIO,
    empresaId: EMPRESA,
    rolId: 'r-1',
    rolCodigo: 'operador',
    rolEsSuperAdmin: false,
    permisos,
    concedidos,
    denegados,
    ...extra,
  };
}

function mockBase(respuestaEfectivos: Record<string, unknown>) {
  mswServer.use(
    http.get('*/api/v1/identidad/permisos', () => HttpResponse.json(CATALOGO)),
    http.get('*/api/v1/identidad/roles/r-1', () =>
      HttpResponse.json({
        rol: { id: 'r-1', codigo: 'operador', nombre: 'Operador', descripcion: null, esDelSistema: false, activo: true, version: 1 },
        permisoIds: ['p-leer', 'p-crear'],
        gruposEntraId: [],
      }),
    ),
    http.get(URL_EFECTIVOS, () => HttpResponse.json(respuestaEfectivos)),
  );
}

async function abrirModulo() {
  const trigger = await screen.findByRole('button', { name: /permisos personalizados de identidad/i });
  fireEvent.click(trigger);
}

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-admin', email: 'a@b.com', nombre: 'Admin' },
    empresas: [],
    currentEmpresaId: EMPRESA,
    permisos: [
      PermisosCanonicos.IdentidadUsuariosLeer,
      PermisosCanonicos.IdentidadUsuariosGestionarPermisos,
      'identidad.roles.leer',
      'identidad.roles.crear',
      'identidad.roles.editar',
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


const SW = (codigo: string) => `Permiso ${codigo}`;
const GRUPO = 'Todos los permisos de identidad';

const LEER = { permisoId: 'p-leer', codigo: 'identidad.roles.leer', origen: 'Rol', efectivo: true, motivo: null };
const CREAR_ROL = { permisoId: 'p-crear', codigo: 'identidad.roles.crear', origen: 'Rol', efectivo: true, motivo: null };
const CREAR_DENEGADO = { ...CREAR_ROL, origen: 'Denegado', efectivo: false };
const AJENO_CONCEDIDO = { permisoId: 'p-ajeno', codigo: 'compras.ordenes.leer', origen: 'Concedido', efectivo: true, motivo: null };

function renderPanel() {
  render(<PermisosPersonalizadosPanel usuarioId={USUARIO} empresaId={EMPRESA} />, {
    wrapper: createQueryWrapper(),
  });
}

function guardadoCapturado() {
  const captura: { body: unknown; key: string | null } = { body: null, key: null };
  mswServer.use(
    http.put(URL_OVERRIDE, async ({ request }) => {
      captura.body = await request.json();
      captura.key = request.headers.get('Idempotency-Key');
      return HttpResponse.json({ usuarioId: USUARIO, empresaId: EMPRESA, concedidos: 0, denegados: 0 });
    }),
  );
  return captura;
}

describe('<PermisosPersonalizadosPanel> — smoke', () => {
  it('no se renderiza sin identidad.usuarios.gestionar-permisos', () => {
    useAuthStore.setState({ permisos: [PermisosCanonicos.IdentidadUsuariosLeer] });
    mockBase(efectivos([]));

    const { container } = render(
      <PermisosPersonalizadosPanel usuarioId={USUARIO} empresaId={EMPRESA} />,
      { wrapper: createQueryWrapper() },
    );

    expect(container).toBeEmptyDOMElement();
  });

  it('muestra el estado efectivo en interruptores y el resumen respecto al rol', async () => {
    mockBase(efectivos([LEER, CREAR_DENEGADO, AJENO_CONCEDIDO], 1, 1));
    renderPanel();
    await abrirModulo();

    expect(await screen.findByRole('switch', { name: SW('identidad.roles.leer') })).toHaveAttribute('aria-checked', 'true');
    expect(screen.getByRole('switch', { name: SW('identidad.roles.crear') })).toHaveAttribute('aria-checked', 'false');
    expect(screen.getByRole('switch', { name: SW('compras.ordenes.leer') })).toHaveAttribute('aria-checked', 'true');
    expect(screen.getByRole('switch', { name: SW('identidad.roles.editar') })).toHaveAttribute('aria-checked', 'false');
    expect(screen.getByText(/1 concedido\(s\) \/ 1 denegado\(s\) respecto al rol/i)).toBeInTheDocument();
    expect(screen.getByText(/personalizado: quitado/i)).toBeInTheDocument();
    expect(screen.getByText(/personalizado: añadido/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /guardar cambios/i })).toBeDisabled();
  });

  it('apagar un permiso del rol deniega, encenderlo de nuevo vuelve a heredado', async () => {
    mockBase(efectivos([]));
    renderPanel();
    await abrirModulo();

    const sw = await screen.findByRole('switch', { name: SW('identidad.roles.leer') });
    fireEvent.click(sw);
    expect(sw).toHaveAttribute('aria-checked', 'false');
    expect(screen.getByText(/0 concedido\(s\) \/ 1 denegado\(s\) respecto al rol/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /guardar cambios/i })).toBeEnabled();

    fireEvent.click(sw);
    expect(sw).toHaveAttribute('aria-checked', 'true');
    expect(screen.getByText(/0 concedido\(s\) \/ 0 denegado\(s\) respecto al rol/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /guardar cambios/i })).toBeDisabled();
  });

  it('guarda con PUT batch enviando solo las excepciones (denegar y conceder)', async () => {
    mockBase(efectivos([LEER]));
    const captura = guardadoCapturado();
    renderPanel();
    await abrirModulo();

    fireEvent.click(await screen.findByRole('switch', { name: SW('identidad.roles.leer') }));
    fireEvent.click(screen.getByRole('switch', { name: SW('identidad.roles.editar') }));
    expect(screen.getByText(/1 concedido\(s\) \/ 1 denegado\(s\) respecto al rol/i)).toBeInTheDocument();
    expect(screen.getByText(/hay cambios sin guardar/i)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /guardar cambios/i }));

    await waitFor(() => expect(captura.body).not.toBeNull());
    expect(captura.body).toEqual({
      overrides: expect.arrayContaining([
        { permisoId: 'p-leer', efecto: 'Denegar', motivo: null },
        { permisoId: 'p-editar', efecto: 'Conceder', motivo: null },
      ]),
    });
    expect((captura.body as { overrides: unknown[] }).overrides).toHaveLength(2);
    expect(captura.key).toBeTruthy();
  });

  it('el botón de fila vuelve el permiso al valor del rol', async () => {
    mockBase(efectivos([LEER, CREAR_DENEGADO], 0, 1));
    renderPanel();
    await abrirModulo();

    const sw = await screen.findByRole('switch', { name: SW('identidad.roles.crear') });
    expect(sw).toHaveAttribute('aria-checked', 'false');
    fireEvent.click(screen.getByRole('button', { name: 'Volver al valor del rol de identidad.roles.crear' }));

    expect(sw).toHaveAttribute('aria-checked', 'true');
    expect(screen.queryByRole('button', { name: /volver al valor del rol de identidad\.roles\.crear/i })).not.toBeInTheDocument();
    expect(screen.getByText(/0 concedido\(s\) \/ 0 denegado\(s\) respecto al rol/i)).toBeInTheDocument();
    expect(screen.getByText(/hay cambios sin guardar/i)).toBeInTheDocument();
  });

  it('no permite encender un permiso que el rol no incluye y el actor no posee', async () => {
    mockBase(efectivos([]));
    renderPanel();
    await abrirModulo();

    const sw = await screen.findByRole('switch', { name: SW('compras.ordenes.leer') });
    expect(sw).toBeDisabled();
    expect(sw).toHaveAttribute('title', expect.stringMatching(/que tú posees/i));
    expect(screen.getByRole('switch', { name: SW('identidad.roles.editar') })).toBeEnabled();
  });

  it('el interruptor de grupo muestra estado mixto, enciende lo posible y avisa de los omitidos', async () => {
    mockBase(efectivos([]));
    renderPanel();

    const grupo = await screen.findByRole('switch', { name: GRUPO });
    expect(grupo).toHaveAttribute('aria-checked', 'mixed');

    fireEvent.click(grupo);
    // editar se concede (el actor lo posee); ajeno se omite (el actor no lo posee).
    expect(screen.getByText(/1 concedido\(s\) \/ 0 denegado\(s\) respecto al rol/i)).toBeInTheDocument();
    expect(screen.getByText(/se omitieron 1 permiso\(s\)/i)).toBeInTheDocument();
    expect(grupo).toHaveAttribute('aria-checked', 'mixed');
  });

  it('el interruptor de grupo enciende todo y luego apaga todo (deniega lo del rol, descarta lo concedido)', async () => {
    mockBase(efectivos([LEER, CREAR_DENEGADO, AJENO_CONCEDIDO], 1, 1));
    renderPanel();

    const grupo = await screen.findByRole('switch', { name: GRUPO });
    expect(grupo).toHaveAttribute('aria-checked', 'mixed');

    // Encender: quita la denegación de crear y concede editar; sin omitidos.
    fireEvent.click(grupo);
    expect(grupo).toHaveAttribute('aria-checked', 'true');
    expect(screen.getByText(/2 concedido\(s\) \/ 0 denegado\(s\) respecto al rol/i)).toBeInTheDocument();
    expect(screen.queryByText(/se omitieron/i)).not.toBeInTheDocument();

    // Apagar: deniega leer y crear (rol) y descarta lo concedido.
    fireEvent.click(grupo);
    expect(grupo).toHaveAttribute('aria-checked', 'false');
    expect(screen.getByText(/0 concedido\(s\) \/ 2 denegado\(s\) respecto al rol/i)).toBeInTheDocument();

    await abrirModulo();
    expect(await screen.findByRole('switch', { name: SW('compras.ordenes.leer') })).toHaveAttribute('aria-checked', 'false');
  });

  it('Restablecer grupo elimina del borrador todas las excepciones del grupo', async () => {
    mockBase(efectivos([LEER, CREAR_DENEGADO, AJENO_CONCEDIDO], 1, 1));
    renderPanel();

    const reset = await screen.findByRole('button', { name: 'Restablecer grupo identidad' });
    await waitFor(() => expect(reset).toBeEnabled());
    fireEvent.click(reset);

    expect(screen.getByText(/0 concedido\(s\) \/ 0 denegado\(s\) respecto al rol/i)).toBeInTheDocument();
    expect(screen.getByText(/hay cambios sin guardar/i)).toBeInTheDocument();
    expect(reset).toBeDisabled();
  });

  it('restablecer pide confirmación y llama DELETE', async () => {
    mockBase(efectivos([CREAR_DENEGADO], 0, 1));
    let borrado = false;
    mswServer.use(
      http.delete(URL_OVERRIDE, () => {
        borrado = true;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    renderPanel();
    const boton = await screen.findByRole('button', { name: /restablecer al rol/i });
    await waitFor(() => expect(boton).toBeEnabled());
    fireEvent.click(boton);

    expect(await screen.findByText(/se borrarán las 1 excepción/i)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /^restablecer$/i }));

    await waitFor(() => expect(borrado).toBe(true));
  });

  it.each([
    [403, 'PERMISOS_OVERRIDE_ESCALADA', 'Sin escalada'],
    [409, 'CONFLICTO', 'Conflicto de datos'],
    [422, 'USUARIO_INACTIVO', 'Usuario inactivo'],
  ])('error %i del PUT se maneja sin romper el panel', async (status, code, title) => {
    mockBase(efectivos([LEER]));
    let intentos = 0;
    mswServer.use(
      http.put(URL_OVERRIDE, () => {
        intentos += 1;
        return HttpResponse.json(
          { type: 'about:blank', title, status, code },
          { status, headers: { 'Content-Type': 'application/problem+json' } },
        );
      }),
    );

    renderPanel();
    await abrirModulo();
    fireEvent.click(await screen.findByRole('switch', { name: SW('identidad.roles.leer') }));
    fireEvent.click(screen.getByRole('button', { name: /guardar cambios/i }));

    await waitFor(() => expect(intentos).toBe(1));
    // El borrador se conserva y el botón vuelve a habilitarse para reintentar.
    await waitFor(() => expect(screen.getByRole('button', { name: /guardar cambios/i })).toBeEnabled());
    expect(screen.getByRole('switch', { name: SW('identidad.roles.leer') })).toHaveAttribute('aria-checked', 'false');
  });

  it('deshabilita la edición al mirar la propia cuenta', async () => {
    useAuthStore.setState({ user: { id: USUARIO, email: 'a@b.com', nombre: 'Yo' } });
    mockBase(efectivos([]));
    renderPanel();
    await abrirModulo();

    expect(await screen.findByText(/no puedes editar tus propios permisos/i)).toBeInTheDocument();
    expect(screen.getByRole('switch', { name: SW('identidad.roles.leer') })).toBeDisabled();
  });

  it('bloquea todo (incluido el grupo) solo para el rol super-admin', async () => {
    mockBase(efectivos([], 0, 0, { rolCodigo: 'super-admin', rolEsSuperAdmin: true }));
    renderPanel();
    await abrirModulo();

    expect(
      await screen.findByText('Los permisos del rol super-admin se gestionan por bootstrap; no admiten excepciones.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('switch', { name: SW('identidad.roles.leer') })).toBeDisabled();
    expect(screen.getByRole('switch', { name: GRUPO })).toBeDisabled();
  });

  it('un rol de sistema que no es super-admin (admin-catalogos) SÍ es editable', async () => {
    mockBase(efectivos([], 0, 0, { rolCodigo: 'admin-catalogos', rolEsSuperAdmin: false }));
    renderPanel();
    await abrirModulo();

    const sw = await screen.findByRole('switch', { name: SW('identidad.roles.leer') });
    expect(sw).toBeEnabled();
    expect(screen.getByRole('switch', { name: GRUPO })).toBeEnabled();
    expect(screen.queryByText(/se gestionan por bootstrap/i)).not.toBeInTheDocument();
    fireEvent.click(sw);
    expect(sw).toHaveAttribute('aria-checked', 'false');
  });
});
