import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ConfiguracionCentrosCostoPage } from './ConfiguracionCentrosCostoPage';
import { useAuthStore } from '@/lib/auth/auth-store';
import type { NodoCeCo } from '@/features/centros-costo/api/types';

/**
 * Los TRES destinos de error de una mutación del catálogo (05 §6 / 8.1):
 *
 * - 409 CONCURRENCY_CONFLICT → ConflictResolutionDialog (recargar).
 * - 428 SIN code → el MISMO diálogo, matcheado por STATUS — el backend
 *   no manda code en el 428 (hallazgo de la verificación en vivo).
 * - 409 CECO_CLAVE_DUPLICADA → error INLINE en el campo clave (pide
 *   cambiar el valor, no recargar) y el modal sigue abierto.
 */

const DIM1: NodoCeCo = {
  tipo: 'dim1', id: 'd1-1', clave: '101', nombre: 'CONKAL',
  estatus: 0, grupo: null, esHoja: false, dim2Vivas: 2, dim3Vivas: 3,
};

function instalarBase() {
  mswServer.use(
    http.get('*/api/v1/centros-costo/jerarquia', ({ request }) => {
      const nodoTipo = new URL(request.url).searchParams.get('nodoTipo');
      return HttpResponse.json(nodoTipo === 'raiz' ? [DIM1] : []);
    }),
    http.get('*/api/v1/centros-costo/dim1/d1-1', () =>
      HttpResponse.json(
        { id: 'd1-1', clave: '101', nombre: 'CONKAL', estatus: 0, version: 3 },
        { headers: { ETag: '"3"' } },
      ),
    ),
  );
}

function patchResponde(status: number, problem: Record<string, unknown>) {
  mswServer.use(
    http.patch('*/api/v1/centros-costo/dim1/d1-1', () =>
      HttpResponse.json(problem, {
        status,
        headers: { 'Content-Type': 'application/problem+json' },
      }),
    ),
  );
}

async function abrirEditarYGuardar() {
  render(<ConfiguracionCentrosCostoPage />, { wrapper: createQueryWrapper() });
  await waitFor(() => expect(screen.getByText('101')).toBeInTheDocument());

  const trigger = screen.getByRole('button', { name: 'Acciones de 101' });
  fireEvent.keyDown(trigger, { key: 'Enter' });
  fireEvent.click(await screen.findByText('Editar'));
  await waitFor(() =>
    expect(screen.getByLabelText(/Nombre/)).toHaveValue('CONKAL'),
  );
  fireEvent.change(screen.getByLabelText(/Nombre/), {
    target: { value: 'CONKAL EDIT' },
  });
  fireEvent.click(screen.getByRole('button', { name: 'Guardar' }));
}

beforeEach(() => {
  instalarBase();
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: [
      'centros_costo.catalogo.leer',
      'centros_costo.catalogo.administrar',
    ],
    errorMessage: null,
  });
});

afterEach(() => {
  useAuthStore.setState({
    status: 'idle', accessToken: null, expiresAt: null, user: null,
    empresas: [], currentEmpresaId: null, permisos: [], errorMessage: null,
  });
});

describe('<ConfiguracionCentrosCostoPage> — conflictos 409/428/duplicado', () => {
  it('409 CONCURRENCY_CONFLICT abre el diálogo de recarga', async () => {
    patchResponde(409, {
      title: 'Conflicto de concurrencia',
      status: 409,
      code: 'CONCURRENCY_CONFLICT',
    });
    await abrirEditarYGuardar();

    expect(
      await screen.findByRole('button', { name: 'Refrescar y revisar' }),
    ).toBeInTheDocument();
  });

  it('428 SIN code abre el MISMO diálogo (matcheo por status, no por código)', async () => {
    patchResponde(428, {
      title: 'If-Match requerido',
      status: 428,
      detail: 'La mutación requiere el header If-Match (ADR-0012).',
      // Deliberadamente SIN code — así responde el backend real.
    });
    await abrirEditarYGuardar();

    expect(
      await screen.findByRole('button', { name: 'Refrescar y revisar' }),
    ).toBeInTheDocument();
  });

  it('409 CECO_CLAVE_DUPLICADA es error de campo: el modal sigue abierto y NO hay diálogo de recarga', async () => {
    patchResponde(409, {
      title: "Ya existe una clave '101'.",
      status: 409,
      detail: "Ya existe una clave '101' (unicidad global del catálogo).",
      code: 'CECO_CLAVE_DUPLICADA',
    });
    await abrirEditarYGuardar();

    expect(
      await screen.findByText(/unicidad global del catálogo/),
    ).toBeInTheDocument();
    // El modal de edición sigue abierto (se puede corregir la clave)…
    expect(screen.getByRole('button', { name: 'Guardar' })).toBeInTheDocument();
    // …y el diálogo de recarga NO apareció.
    expect(
      screen.queryByRole('button', { name: 'Refrescar y revisar' }),
    ).not.toBeInTheDocument();
  });
});
