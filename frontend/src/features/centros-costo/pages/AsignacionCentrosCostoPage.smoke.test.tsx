import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { AsignacionCentrosCostoPage } from './AsignacionCentrosCostoPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import {
  TriEstado,
  type ArbolAsignacionResponse,
} from '@/features/centros-costo/api/types';

/**
 * Smoke de la página de asignación (FE-PR3): selección de usuario → carga
 * del árbol; marcar → POST con el body correcto → REFETCH que repinta el
 * tri-estado del backend (reconciliación refetch-only, no del response del
 * POST); alcance total → badge + árbol deshabilitado.
 */

const USUARIO = { id: 'u-1', nombre: 'Ada Lovelace', email: 'ada@millet.mx', activo: true };

function arbol(estadoDim1: TriEstado, esAlcanceTotal = false): ArbolAsignacionResponse {
  return {
    usuarioId: 'u-1',
    esAlcanceTotal,
    resumen: {
      dim1Vivas: 1,
      dim1Completas: estadoDim1 === TriEstado.Todo ? 1 : 0,
      dim2Vivas: 1, dim2Completas: 0, dim3Vivas: 3,
      dim3Asignadas: estadoDim1 === TriEstado.Todo ? 3 : estadoDim1 === TriEstado.Parcial ? 2 : 0,
    },
    dim1s: [
      {
        id: 'd1', clave: '101', nombre: 'CONKAL', estado: estadoDim1,
        dim3Vivas: 3,
        dim3Asignadas: estadoDim1 === TriEstado.Todo ? 3 : estadoDim1 === TriEstado.Parcial ? 2 : 0,
        grupos: [],
      },
    ],
  };
}

interface Capturada {
  url: string;
  idempotencyKey: string | null;
  body: unknown;
}
let capturadas: Capturada[];
/** Secuencia de árboles que devuelve el GET (se consume uno por request). */
let arbolesGet: ArbolAsignacionResponse[];

function instalar() {
  capturadas = [];
  mswServer.use(
    http.get('*/api/v1/identidad/usuarios', () =>
      HttpResponse.json({ items: [USUARIO], offset: 0, limit: 200, total: 1 }),
    ),
    http.get('*/api/v1/centros-costo/asignaciones/:usuarioId/arbol', () => {
      const next = arbolesGet.length > 1 ? arbolesGet.shift()! : arbolesGet[0];
      return HttpResponse.json(next);
    }),
    http.post('*/api/v1/centros-costo/asignaciones/:usuarioId/marcar', async ({ request }) => {
      capturadas.push({
        url: new URL(request.url).pathname,
        idempotencyKey: request.headers.get('Idempotency-Key'),
        body: await request.json(),
      });
      return HttpResponse.json({
        usuarioId: 'u-1', hojasResueltas: 3, afectadas: 3, totalUsuario: 3,
      });
    }),
  );
}

function conPermisos(permisos: string[]) {
  useAuthStore.setState({
    status: 'authenticated', accessToken: 't', expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'admin', email: 'a@e.com', nombre: 'Admin' }, empresas: [],
    currentEmpresaId: 'e-1', permisos, errorMessage: null,
  });
}

beforeEach(() => {
  instalar();
  conPermisos(['centros_costo.asignaciones.administrar']);
});
afterEach(() => {
  useAuthStore.setState({
    status: 'idle', accessToken: null, expiresAt: null, user: null,
    empresas: [], currentEmpresaId: null, permisos: [], errorMessage: null,
  });
});

/** Abre el UsuarioSelector (Popover+Command) y elige el usuario. */
async function seleccionarUsuario() {
  fireEvent.click(screen.getByRole('combobox', { name: 'Seleccionar usuario' }));
  fireEvent.click(await screen.findByText('Ada Lovelace'));
}

describe('<AsignacionCentrosCostoPage> — DoD FE-PR3', () => {
  it('sin usuario muestra el prompt de selección', () => {
    arbolesGet = [arbol(TriEstado.Ninguno)];
    render(<AsignacionCentrosCostoPage />, { wrapper: createQueryWrapper() });
    expect(screen.getByText(/Selecciona un usuario/)).toBeInTheDocument();
  });

  it('seleccionar usuario carga el árbol + resumen', async () => {
    arbolesGet = [arbol(TriEstado.Parcial)];
    render(<AsignacionCentrosCostoPage />, { wrapper: createQueryWrapper() });
    await seleccionarUsuario();

    await waitFor(() =>
      expect(screen.getByText('101 · CONKAL')).toBeInTheDocument(),
    );
    expect(screen.getByTestId('resumen-asignacion')).toBeInTheDocument();
    // Dim1 Parcial → checkbox indeterminate.
    expect(
      screen.getByRole('checkbox', { name: 'Alcance de 101 · CONKAL' }),
    ).toHaveAttribute('aria-checked', 'mixed');
  });

  it('marcar manda POST con el body correcto y el árbol se repinta del REFETCH', async () => {
    // 1er GET: Dim1 Ninguno. Tras marcar, 2º GET: Dim1 Todo (el backend
    // recomputó). La UI repinta del refetch, no del response del POST.
    arbolesGet = [arbol(TriEstado.Ninguno), arbol(TriEstado.Todo)];
    render(<AsignacionCentrosCostoPage />, { wrapper: createQueryWrapper() });
    await seleccionarUsuario();

    const chk = await screen.findByRole('checkbox', { name: 'Alcance de 101 · CONKAL' });
    await waitFor(() => expect(chk).toHaveAttribute('aria-checked', 'false'));

    fireEvent.click(chk);

    await waitFor(() => expect(capturadas).toHaveLength(1));
    expect(capturadas[0].idempotencyKey).toBeTruthy();
    expect(capturadas[0].body).toMatchObject({
      nivel: 0, nodoId: 'd1', grupoId: null, asignar: true,
    });

    // El refetch repinta a Todo (checked) — estado del backend, no del POST.
    await waitFor(() =>
      expect(
        screen.getByRole('checkbox', { name: 'Alcance de 101 · CONKAL' }),
      ).toHaveAttribute('aria-checked', 'true'),
    );
  });

  it('alcance total: badge + árbol deshabilitado', async () => {
    arbolesGet = [arbol(TriEstado.Ninguno, /* esAlcanceTotal */ true)];
    render(<AsignacionCentrosCostoPage />, { wrapper: createQueryWrapper() });
    await seleccionarUsuario();

    expect(await screen.findByTestId('badge-alcance-total')).toBeInTheDocument();
    await waitFor(() =>
      expect(
        screen.getByRole('checkbox', { name: 'Alcance de 101 · CONKAL' }),
      ).toBeDisabled(),
    );
  });
});
