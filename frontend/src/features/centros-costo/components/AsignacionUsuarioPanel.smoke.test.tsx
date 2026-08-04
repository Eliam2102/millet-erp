import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { AsignacionUsuarioPanel } from './AsignacionUsuarioPanel';
import { useAuthStore } from '@/lib/auth/auth-store';
import {
  TriEstado,
  type ArbolAsignacionResponse,
} from '@/features/centros-costo/api/types';

/**
 * Smoke del panel compartido de asignación. El panel recibe `usuarioId`
 * directo (sin selector): carga el árbol, marcar → POST + refetch que repinta,
 * `esAlcanceTotal` → banner + árbol deshabilitado, y `readOnly` deshabilita el
 * árbol SIN banner de alcance total (distingue el candado externo del interno).
 */

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
  idempotencyKey: string | null;
  body: unknown;
}
let capturadas: Capturada[];
let arbolesGet: ArbolAsignacionResponse[];

function instalar() {
  capturadas = [];
  mswServer.use(
    http.get('*/api/v1/centros-costo/asignaciones/:usuarioId/arbol', () => {
      const next = arbolesGet.length > 1 ? arbolesGet.shift()! : arbolesGet[0];
      return HttpResponse.json(next);
    }),
    http.post('*/api/v1/centros-costo/asignaciones/:usuarioId/marcar', async ({ request }) => {
      capturadas.push({
        idempotencyKey: request.headers.get('Idempotency-Key'),
        body: await request.json(),
      });
      return HttpResponse.json({
        usuarioId: 'u-1', hojasResueltas: 3, afectadas: 3, totalUsuario: 3,
      });
    }),
  );
}

beforeEach(() => {
  instalar();
  useAuthStore.setState({
    status: 'authenticated', accessToken: 't', expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'admin', email: 'a@e.com', nombre: 'Admin' }, empresas: [],
    currentEmpresaId: 'e-1', permisos: ['centros_costo.asignaciones.administrar'],
    errorMessage: null,
  });
});
afterEach(() => {
  useAuthStore.setState({
    status: 'idle', accessToken: null, expiresAt: null, user: null,
    empresas: [], currentEmpresaId: null, permisos: [], errorMessage: null,
  });
});

const chkCONKAL = () =>
  screen.getByRole('checkbox', { name: 'Alcance de 101 · CONKAL' });

describe('<AsignacionUsuarioPanel> — smoke', () => {
  it('carga el árbol + resumen para el usuarioId dado', async () => {
    arbolesGet = [arbol(TriEstado.Parcial)];
    render(<AsignacionUsuarioPanel usuarioId="u-1" />, {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() =>
      expect(screen.getByText('101 · CONKAL')).toBeInTheDocument(),
    );
    expect(screen.getByTestId('resumen-asignacion')).toBeInTheDocument();
    expect(chkCONKAL()).toHaveAttribute('aria-checked', 'mixed');
  });

  it('marcar manda POST con el body correcto y el árbol se repinta del REFETCH', async () => {
    arbolesGet = [arbol(TriEstado.Ninguno), arbol(TriEstado.Todo)];
    render(<AsignacionUsuarioPanel usuarioId="u-1" />, {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(chkCONKAL()).toHaveAttribute('aria-checked', 'false'));
    fireEvent.click(chkCONKAL());

    await waitFor(() => expect(capturadas).toHaveLength(1));
    expect(capturadas[0].idempotencyKey).toBeTruthy();
    expect(capturadas[0].body).toMatchObject({
      nivel: 0, nodoId: 'd1', grupoId: null, asignar: true,
    });

    await waitFor(() =>
      expect(chkCONKAL()).toHaveAttribute('aria-checked', 'true'),
    );
  });

  it('esAlcanceTotal: banner + árbol deshabilitado', async () => {
    arbolesGet = [arbol(TriEstado.Ninguno, /* esAlcanceTotal */ true)];
    render(<AsignacionUsuarioPanel usuarioId="u-1" />, {
      wrapper: createQueryWrapper(),
    });

    expect(await screen.findByTestId('badge-alcance-total')).toBeInTheDocument();
    await waitFor(() => expect(chkCONKAL()).toBeDisabled());
  });

  it('readOnly=true: árbol deshabilitado SIN banner de alcance total', async () => {
    arbolesGet = [arbol(TriEstado.Parcial)];
    render(<AsignacionUsuarioPanel usuarioId="u-1" readOnly />, {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(chkCONKAL()).toBeDisabled());
    // El disable viene del candado externo, NO de alcance total.
    expect(screen.queryByTestId('badge-alcance-total')).not.toBeInTheDocument();
    // Clic en un checkbox deshabilitado no dispara mutación.
    fireEvent.click(chkCONKAL());
    expect(capturadas).toHaveLength(0);
  });

  it('readOnly=false (default): el árbol es editable', async () => {
    arbolesGet = [arbol(TriEstado.Ninguno)];
    render(<AsignacionUsuarioPanel usuarioId="u-1" />, {
      wrapper: createQueryWrapper(),
    });

    await waitFor(() => expect(chkCONKAL()).not.toBeDisabled());
    fireEvent.click(chkCONKAL());
    await waitFor(() => expect(capturadas).toHaveLength(1));
  });
});
