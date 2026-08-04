import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import axe, { type AxeResults } from 'axe-core';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ArbolCentrosCosto } from '@/features/centros-costo/components/ArbolCentrosCosto';
import { etiquetaTipoNodo } from '@/features/centros-costo/lib/etiquetas';
import { useAuthStore } from '@/lib/auth/auth-store';
import type { NodoCeCo } from '@/features/centros-costo/api/types';

/**
 * Smoke del árbol de configuración (CECO-FE-PR1, molde
 * ArbolSaldos.smoke): MSW despacha por nodoTipo/nodoId como el endpoint
 * real, auto-expand de cadena única, chips de grupo, conteos del backend
 * y cero violations de axe. El vocabulario esperado se construye VÍA el
 * helper (aquí no pueden vivir literales — lint rule).
 */

const DIM1_CONKAL: NodoCeCo = {
  tipo: 'dim1',
  id: 'd1-1',
  clave: '101',
  nombre: 'CONKAL',
  estatus: 0,
  grupo: null,
  esHoja: false,
  dim2Vivas: 2,
  dim3Vivas: 3,
};

const DIM2S: NodoCeCo[] = [
  {
    tipo: 'dim2',
    id: 'd2-1',
    clave: '20PDMC',
    nombre: 'CORTE',
    estatus: 0,
    grupo: 'OPERACIONES',
    esHoja: false,
    dim2Vivas: 0,
    dim3Vivas: 2,
  },
  {
    tipo: 'dim2',
    id: 'd2-2',
    clave: '20PDTE',
    nombre: 'TEMPLADO',
    estatus: 0,
    grupo: 'OPERACIONES',
    esHoja: false,
    dim2Vivas: 0,
    dim3Vivas: 1,
  },
];

const DIM3S: NodoCeCo[] = [
  {
    tipo: 'dim3',
    id: 'd3-1',
    clave: 'MCLC101',
    nombre: 'GANTRY',
    estatus: 0,
    grupo: 'LINEA / CORTE 1',
    esHoja: true,
    dim2Vivas: 0,
    dim3Vivas: 0,
  },
  {
    tipo: 'dim3',
    id: 'd3-2',
    clave: 'MCLC102',
    nombre: 'MESA DE CORTE',
    estatus: 2, // EnRevision — viva: se pinta con badge
    grupo: 'LINEA / CORTE 1',
    esHoja: true,
    dim2Vivas: 0,
    dim3Vivas: 0,
  },
];

function mockJerarquia(porNodo: {
  raiz: NodoCeCo[];
  dim1?: Record<string, NodoCeCo[]>;
  dim2?: Record<string, NodoCeCo[]>;
}) {
  mswServer.use(
    http.get('*/api/v1/centros-costo/jerarquia', ({ request }) => {
      const url = new URL(request.url);
      const nodoTipo = url.searchParams.get('nodoTipo');
      const nodoId = url.searchParams.get('nodoId') ?? '';
      if (nodoTipo === 'raiz') return HttpResponse.json(porNodo.raiz);
      if (nodoTipo === 'dim1')
        return HttpResponse.json(porNodo.dim1?.[nodoId] ?? []);
      if (nodoTipo === 'dim2')
        return HttpResponse.json(porNodo.dim2?.[nodoId] ?? []);
      return HttpResponse.json([]);
    }),
  );
}

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: ['centros_costo.catalogo.leer'],
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

describe('<ArbolCentrosCosto> — árbol de configuración lazy (CECO-FE-PR1)', () => {
  it('auto-expande la cadena única, pinta chips de grupo y conteos del backend', async () => {
    mockJerarquia({
      raiz: [DIM1_CONKAL],
      dim1: { 'd1-1': DIM2S },
      dim2: { 'd2-1': DIM3S },
    });
    render(<ArbolCentrosCosto />, { wrapper: createQueryWrapper() });

    // Cadena única: la dim1 sola se abre y para donde ramifica (2 dim2).
    await waitFor(() => expect(screen.getByText('101')).toBeInTheDocument());
    await waitFor(() => expect(screen.getByText('20PDMC')).toBeInTheDocument());
    expect(screen.getByText('20PDTE')).toBeInTheDocument();
    // …sin pedir las dim3 (lazy).
    expect(screen.queryByText('MCLC101')).not.toBeInTheDocument();

    // Chips de grupo por renglón (el grupo clasifica, no anida).
    expect(screen.getAllByText('OPERACIONES')).toHaveLength(2);

    // Conteos calculados por el backend con vocabulario del helper único.
    const d2 = etiquetaTipoNodo('dim2', 'configuracion');
    const d3 = etiquetaTipoNodo('dim3', 'configuracion');
    expect(screen.getByText(`2 × ${d2} · 3 × ${d3}`)).toBeInTheDocument();
  });

  it('expandir una dim2 carga sus hojas; EnRevision se pinta con badge', async () => {
    mockJerarquia({
      raiz: [DIM1_CONKAL],
      dim1: { 'd1-1': DIM2S },
      dim2: { 'd2-1': DIM3S },
    });
    render(<ArbolCentrosCosto />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('20PDMC')).toBeInTheDocument());

    fireEvent.click(screen.getByRole('button', { name: 'Expandir 20PDMC' }));

    await waitFor(() =>
      expect(screen.getByText('MCLC101')).toBeInTheDocument(),
    );
    expect(screen.getByText('MCLC102')).toBeInTheDocument();
    expect(screen.getByText('En revisión')).toBeInTheDocument();
    expect(screen.getAllByText('LINEA / CORTE 1')).toHaveLength(2);
  });

  it('raíz vacía muestra el mensaje pre-siembra', async () => {
    mockJerarquia({ raiz: [] });
    render(<ArbolCentrosCosto />, { wrapper: createQueryWrapper() });
    await waitFor(() =>
      expect(screen.getByTestId('arbol-vacio')).toBeInTheDocument(),
    );
  });

  it('axe-core: cero violations con el árbol expandido', async () => {
    mockJerarquia({
      raiz: [DIM1_CONKAL],
      dim1: { 'd1-1': DIM2S },
      dim2: { 'd2-1': DIM3S },
    });
    const { container } = render(<ArbolCentrosCosto />, {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(screen.getByText('20PDMC')).toBeInTheDocument());

    const results: AxeResults = await axe.run(container, {
      runOnly: {
        type: 'tag',
        values: ['wcag2a', 'wcag2aa', 'wcag21aa'],
      },
    });
    if (results.violations.length > 0) {
      const detalle = results.violations
        .map((v) => `${v.id} (${v.impact}): ${v.description}`)
        .join('\n');
      throw new Error(`axe-core violations:\n${detalle}`);
    }
  });
});
