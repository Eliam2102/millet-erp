import { fireEvent, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { useAuthStore } from '@/lib/auth/auth-store';
import type { NodoCeCo } from '@/features/centros-costo/api/types';

/**
 * Arnés compartido de los tests de la pantalla de configuración
 * (CECO-FE-PR2). Extraído a un módulo aparte para poder DIVIDIR los tests
 * en varios archivos: 8 renders de la página (Radix Dialog/DropdownMenu +
 * MSW + Query) en UN solo worker agotan el heap de jsdom — los portales
 * y el scroll-lock no se liberan entre tests. Repartidos en archivos de
 * ~4 tests, cada worker se mantiene bajo el límite.
 */

// GUIDs de grupos = los CONGELADOS reales de la siembra (0000000c-…);
// z.string().uuid() los rechazaría (no cumplen version/variant RFC 4122)
// — por eso el schema usa un regex de formato, no .uuid().
export const GRUPO_DIM2_ID = '0000000c-0001-0000-0000-000000000001';
export const GRUPO_DIM3_ID = '0000000c-0002-0000-0000-000000000001';

export const DIM1: NodoCeCo = {
  tipo: 'dim1', id: 'd1-1', clave: '101', nombre: 'CONKAL',
  estatus: 0, grupo: null, esHoja: false, dim2Vivas: 2, dim3Vivas: 3,
};
export const DIM2S: NodoCeCo[] = [
  { tipo: 'dim2', id: 'd2-1', clave: '20PDMC', nombre: 'CORTE',
    estatus: 0, grupo: 'OPERACIONES', esHoja: false, dim2Vivas: 0, dim3Vivas: 2 },
  { tipo: 'dim2', id: 'd2-2', clave: '20PDTE', nombre: 'TEMPLADO',
    estatus: 0, grupo: 'OPERACIONES', esHoja: false, dim2Vivas: 0, dim3Vivas: 1 },
];
export const DIM3S: NodoCeCo[] = [
  { tipo: 'dim3', id: 'd3-1', clave: 'MCLC101', nombre: 'GANTRY',
    estatus: 0, grupo: 'LINEA / CORTE 1', esHoja: true, dim2Vivas: 0, dim3Vivas: 0 },
];

/** Capturas de las mutaciones que MSW recibe (headers + body). */
export interface Capturada {
  metodo: string;
  url: string;
  idempotencyKey: string | null;
  ifMatch: string | null;
  body: unknown;
}

export function instalarHandlers(capturadas: Capturada[]) {
  mswServer.use(
    http.get('*/api/v1/centros-costo/jerarquia', ({ request }) => {
      const url = new URL(request.url);
      const nodoTipo = url.searchParams.get('nodoTipo');
      const nodoId = url.searchParams.get('nodoId') ?? '';
      if (nodoTipo === 'raiz') return HttpResponse.json([DIM1]);
      if (nodoTipo === 'dim1' && nodoId === 'd1-1') return HttpResponse.json(DIM2S);
      if (nodoTipo === 'dim2' && nodoId === 'd2-1') return HttpResponse.json(DIM3S);
      return HttpResponse.json([]);
    }),
    http.get('*/api/v1/centros-costo/dim3/d3-1', () =>
      HttpResponse.json(
        { id: 'd3-1', dim2Id: 'd2-1', clave: 'MCLC101', nombre: 'GANTRY',
          grupoDim3Id: GRUPO_DIM3_ID, estatus: 0, version: 5 },
        { headers: { ETag: '"5"' } },
      ),
    ),
    http.get('*/api/v1/centros-costo/dim1/d1-1', () =>
      HttpResponse.json(
        { id: 'd1-1', clave: '101', nombre: 'CONKAL', estatus: 0, version: 3 },
        { headers: { ETag: '"3"' } },
      ),
    ),
    http.get('*/api/v1/centros-costo/dim2/d2-1', () =>
      HttpResponse.json(
        { id: 'd2-1', dim1Id: 'd1-1', clave: '20PDMC', nombre: 'CORTE',
          grupoDim2Id: GRUPO_DIM2_ID, estatus: 0, version: 2 },
        { headers: { ETag: '"2"' } },
      ),
    ),
    http.get('*/api/v1/centros-costo/grupos-dim3/', () =>
      HttpResponse.json({
        items: [{ id: GRUPO_DIM3_ID, nombre: 'LINEA / CORTE 1', estatus: 0, version: 1 }],
        total: 1, offset: 0, limit: 100,
      }),
    ),
    http.get('*/api/v1/centros-costo/grupos-dim2/', () =>
      HttpResponse.json({
        items: [{ id: GRUPO_DIM2_ID, nombre: 'OPERACIONES', estatus: 0, version: 1 }],
        total: 1, offset: 0, limit: 100,
      }),
    ),
    http.get('*/api/v1/centros-costo/dim1/', () =>
      HttpResponse.json({ items: [], total: 0, offset: 0, limit: 8 }),
    ),
    http.get('*/api/v1/centros-costo/dim2/', () =>
      HttpResponse.json({ items: [], total: 0, offset: 0, limit: 8 }),
    ),
    http.get('*/api/v1/centros-costo/dim3/', ({ request }) => {
      const q = new URL(request.url).searchParams.get('q');
      if (q && 'gantry'.includes(q.toLowerCase())) {
        return HttpResponse.json({
          items: [{
            id: 'd3-1', dim2Id: 'd2-1', clave: 'MCLC101', nombre: 'GANTRY',
            grupoDim3Id: GRUPO_DIM3_ID, grupoDim3Nombre: 'LINEA / CORTE 1',
            dim2Clave: '20PDMC', dim2Nombre: 'CORTE', estatus: 0, version: 5,
          }],
          total: 1, offset: 0, limit: 8,
        });
      }
      return HttpResponse.json({ items: [], total: 0, offset: 0, limit: 8 });
    }),
    http.post('*/api/v1/centros-costo/:recurso/', async ({ request, params }) => {
      capturadas.push({
        metodo: 'POST', url: `/${params.recurso as string}/`,
        idempotencyKey: request.headers.get('Idempotency-Key'),
        ifMatch: request.headers.get('If-Match'),
        body: await request.json(),
      });
      return HttpResponse.json(
        { id: 'nuevo', clave: 'X', nombre: 'X', estatus: 0, version: 1 },
        { status: 201, headers: { ETag: '"1"' } },
      );
    }),
    http.patch('*/api/v1/centros-costo/:recurso/:id', async ({ request, params }) => {
      capturadas.push({
        metodo: 'PATCH', url: `/${params.recurso as string}/${params.id as string}`,
        idempotencyKey: request.headers.get('Idempotency-Key'),
        ifMatch: request.headers.get('If-Match'),
        body: await request.json(),
      });
      return HttpResponse.json(
        { id: params.id, clave: 'X', nombre: 'X', estatus: 0, version: 6 },
        { headers: { ETag: '"6"' } },
      );
    }),
    http.post('*/api/v1/centros-costo/:recurso/:id/desactivar', ({ request, params }) => {
      capturadas.push({
        metodo: 'DESACTIVAR', url: `/${params.recurso as string}/${params.id as string}`,
        idempotencyKey: request.headers.get('Idempotency-Key'),
        ifMatch: request.headers.get('If-Match'),
        body: null,
      });
      return HttpResponse.json({
        id: params.id, estatus: 1, dim2Desactivadas: 2, dim3Desactivadas: 3,
      });
    }),
  );
}

export function conPermisos(permisos: string[]) {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos,
    errorMessage: null,
  });
}

export function limpiarAuth() {
  useAuthStore.setState({
    status: 'idle', accessToken: null, expiresAt: null, user: null,
    empresas: [], currentEmpresaId: null, permisos: [], errorMessage: null,
  });
}

/** Radix DropdownMenu en jsdom: se abre por teclado (camino estable). */
export function abrirMenuAcciones(clave: string) {
  const trigger = screen.getByRole('button', { name: `Acciones de ${clave}` });
  fireEvent.keyDown(trigger, { key: 'Enter' });
}

export async function esperarArbol() {
  // getByText lanza si no encuentra — sin matchers jest-dom (este módulo
  // no es un archivo de test; sus tipos no traen toBeInTheDocument).
  await waitFor(() => screen.getByText('101'));
  await waitFor(() => screen.getByText('20PDMC'));
}
