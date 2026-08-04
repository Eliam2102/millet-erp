import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import axe, { type AxeResults } from 'axe-core';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ArbolSaldos } from '@/features/almacen/components/ArbolSaldos';
import { useAuthStore } from '@/lib/auth/auth-store';
import type { NodoJerarquiaSaldo } from '@/features/almacen/api/types';

/**
 * Smoke del árbol lazy (PR6): cada expansión pide SOLO los hijos del nodo;
 * la cadena única (un solo hijo no-hoja) se auto-expande hasta ramificar.
 */

const SUCURSAL: NodoJerarquiaSaldo = {
  tipo: 'sucursal',
  id: 's-1',
  clave: 'CONKAL',
  nombre: 'Planta Conkal',
  cantidad: 35,
  valorInventarioMxn: 6300,
  esDefault: false,
  esHoja: false,
};

const ALMACEN: NodoJerarquiaSaldo = {
  tipo: 'almacen',
  id: 'a-1',
  clave: 'CRYSTAL',
  nombre: 'Almacén Crystal',
  cantidad: 35,
  valorInventarioMxn: 6300,
  esDefault: false,
  esHoja: false,
};

const SUBS: NodoJerarquiaSaldo[] = [
  {
    tipo: 'subAlmacen',
    id: 'sub-1',
    clave: 'HG1',
    nombre: 'Herramientas G1',
    cantidad: 30,
    valorInventarioMxn: 6000,
    esDefault: false,
    esHoja: false,
  },
  {
    tipo: 'subAlmacen',
    id: 'sub-2',
    clave: 'TA1',
    nombre: 'Templado A1',
    cantidad: 5,
    valorInventarioMxn: 300,
    esDefault: false,
    esHoja: false,
  },
];

const UBICACIONES: NodoJerarquiaSaldo[] = [
  {
    tipo: 'ubicacion',
    id: 'u-unica',
    clave: 'UNICA-01',
    nombre: 'Única',
    cantidad: 10,
    valorInventarioMxn: 1000,
    esDefault: true,
    esHoja: false,
  },
  {
    tipo: 'ubicacion',
    id: 'u-b1',
    clave: 'HG1-05',
    nombre: 'Rack 5',
    cantidad: 20,
    valorInventarioMxn: 5000,
    esDefault: false,
    esHoja: false,
  },
];

beforeEach(() => {
  useAuthStore.setState({
    status: 'authenticated',
    accessToken: 'test-token',
    expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u-test', email: 't@e.com', nombre: 'Test User' },
    empresas: [],
    currentEmpresaId: 'e-test',
    permisos: ['almacen.almacenes.leer'],
    errorMessage: null,
  });

  // Un solo handler: despacha por nodoTipo/nodoId como el endpoint real.
  mswServer.use(
    http.get('*/api/v1/almacen/saldos/jerarquia', ({ request }) => {
      const url = new URL(request.url);
      const nodoTipo = url.searchParams.get('nodoTipo');
      const nodoId = url.searchParams.get('nodoId');
      if (!nodoTipo) return HttpResponse.json([SUCURSAL]);
      if (nodoTipo === 'sucursal' && nodoId === 's-1')
        return HttpResponse.json([ALMACEN]);
      if (nodoTipo === 'almacen' && nodoId === 'a-1')
        return HttpResponse.json(SUBS);
      if (nodoTipo === 'subAlmacen' && nodoId === 'sub-1')
        return HttpResponse.json(UBICACIONES);
      return HttpResponse.json([]);
    }),
  );
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

describe('<ArbolSaldos> — árbol lazy con auto-expand de cadena única (PR6)', () => {
  it('auto-expande la cadena única y para donde ramifica', async () => {
    render(<ArbolSaldos nodoTipo="raiz" />, { wrapper: createQueryWrapper() });

    // Cadena única: sucursal → almacén se abren solos…
    await waitFor(() => expect(screen.getByText('CONKAL')).toBeInTheDocument());
    await waitFor(() => expect(screen.getByText('CRYSTAL')).toBeInTheDocument());
    // …hasta el nivel que ramifica (2 sub-almacenes visibles)…
    await waitFor(() => expect(screen.getByText('HG1')).toBeInTheDocument());
    expect(screen.getByText('TA1')).toBeInTheDocument();
    // …y NO sigue: las ubicaciones no se han pedido (lazy).
    expect(screen.queryByText('HG1-05')).not.toBeInTheDocument();
  });

  it('expandir un nodo carga sus hijos; la ÚNICA se distingue por badge', async () => {
    render(<ArbolSaldos nodoTipo="raiz" />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('HG1')).toBeInTheDocument());

    fireEvent.click(screen.getByRole('button', { name: 'Expandir HG1' }));

    await waitFor(() =>
      expect(screen.getByText('HG1-05')).toBeInTheDocument(),
    );
    expect(screen.getByText('UNICA-01')).toBeInTheDocument();
    expect(screen.getByText('ÚNICA')).toBeInTheDocument(); // el badge
    // Rollup del backend renderizado (es-MX, 2 decimales).
    expect(screen.getByText('20.00')).toBeInTheDocument();
  });

  it('acepta un salto directo (atajo modo ubicación) sin pasar por la raíz', async () => {
    render(<ArbolSaldos nodoTipo="subAlmacen" nodoId="sub-1" />, {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() =>
      expect(screen.getByText('HG1-05')).toBeInTheDocument(),
    );
    expect(screen.queryByText('CONKAL')).not.toBeInTheDocument();
  });

  it('axe-core: cero violations con el árbol expandido', async () => {
    const { container } = render(<ArbolSaldos nodoTipo="raiz" />, {
      wrapper: createQueryWrapper(),
    });
    await waitFor(() => expect(screen.getByText('HG1')).toBeInTheDocument());

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
