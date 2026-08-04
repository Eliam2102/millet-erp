import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import axe, { type AxeResults } from 'axe-core';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { ConfiguracionCentrosCostoPage } from './ConfiguracionCentrosCostoPage';
import { etiquetaNivel } from '@/features/centros-costo/lib/etiquetas';
import type { NodoCeCo } from '@/features/centros-costo/api/types';
import {
  abrirMenuAcciones,
  conPermisos,
  DIM1,
  DIM2S,
  DIM3S,
  esperarArbol,
  instalarHandlers,
  limpiarAuth,
  type Capturada,
} from './__fixtures__/configuracion-test-harness';

/**
 * DoD de FE-PR2, parte interacciones: cascada con la promesa del nodo +
 * If-Match, toggle de inactivos, búsqueda con expansión de rama (lookup
 * extra del abuelo) y axe. Archivo aparte del CRUD por la memoria de
 * jsdom (ver el arnés).
 */

let capturadas: Capturada[];

beforeEach(() => {
  capturadas = [];
  instalarHandlers(capturadas);
  conPermisos([
    'centros_costo.catalogo.leer',
    'centros_costo.catalogo.administrar',
  ]);
});

afterEach(limpiarAuth);

describe('<ConfiguracionCentrosCostoPage> — interacciones (DoD FE-PR2)', () => {
  it('desactivar una raíz advierte la CASCADA con los conteos del nodo y manda If-Match', async () => {
    render(<ConfiguracionCentrosCostoPage />, { wrapper: createQueryWrapper() });
    await esperarArbol();

    abrirMenuAcciones('101');
    fireEvent.click(await screen.findByText('Desactivar'));

    const d2 = etiquetaNivel('dim2', 'configuracion');
    const d3 = etiquetaNivel('dim3', 'configuracion');
    const dialogo = await screen.findByRole('alertdialog');
    expect(dialogo).toHaveTextContent(`desactivará 2 ${d2} y 3 ${d3} vivas`);

    const botonConfirmar = within(dialogo).getByRole('button', {
      name: 'Desactivar',
    });
    await waitFor(() => expect(botonConfirmar).toBeEnabled());
    fireEvent.click(botonConfirmar);

    await waitFor(() => expect(capturadas).toHaveLength(1));
    expect(capturadas[0].metodo).toBe('DESACTIVAR');
    expect(capturadas[0].url).toBe('/dim1/d1-1');
    expect(capturadas[0].ifMatch).toBe('"3"'); // ETag del detalle cargado al abrir
    expect(capturadas[0].idempotencyKey).toBeTruthy();
  });

  it('el filtro de estado re-consulta con incluirInactivos=true', async () => {
    // El sujeto NO cambió con el ajuste visual: cambiar el filtro de
    // estado dispara re-query con incluirInactivos=true. Solo cambió el
    // CONTROL (checkbox suelto → Select del ERP), así que la interacción
    // se adapta: Radix Select se abre y navega por TECLADO (estable en
    // jsdom, sin depender de pointer-capture).
    let ultimoIncluir: string | null = null;
    mswServer.use(
      http.get('*/api/v1/centros-costo/jerarquia', ({ request }) => {
        const url = new URL(request.url);
        ultimoIncluir = url.searchParams.get('incluirInactivos');
        // Solo la raíz trae la dim1; los hijos van vacíos — si se
        // devolviera [DIM1] a todo nivel, el auto-expand de cadena única
        // recursaría infinito (una dim1 sola SIEMPRE se auto-abre).
        const nodoTipo = url.searchParams.get('nodoTipo');
        return HttpResponse.json(nodoTipo === 'raiz' ? [DIM1] : []);
      }),
    );
    render(<ConfiguracionCentrosCostoPage />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('101')).toBeInTheDocument());

    const filtro = screen.getByLabelText('Filtro de estado');
    filtro.focus();
    fireEvent.keyDown(filtro, { key: 'Enter' }); // abre el listbox
    const opcion = await screen.findByRole('option', {
      name: 'Incluir inactivos',
    });
    fireEvent.click(opcion);

    await waitFor(() => expect(ultimoIncluir).toBe('true'));
  });

  it('la búsqueda expande la rama del resultado (lookup extra del abuelo)', async () => {
    // Dos raíces para que la cadena única NO auto-expanda: la expansión
    // visible solo puede venir de la búsqueda.
    const otraDim1: NodoCeCo = { ...DIM1, id: 'd1-2', clave: '102', nombre: 'CHICHI' };
    mswServer.use(
      http.get('*/api/v1/centros-costo/jerarquia', ({ request }) => {
        const url = new URL(request.url);
        const nodoTipo = url.searchParams.get('nodoTipo');
        const nodoId = url.searchParams.get('nodoId') ?? '';
        if (nodoTipo === 'raiz') return HttpResponse.json([DIM1, otraDim1]);
        if (nodoTipo === 'dim1' && nodoId === 'd1-1') return HttpResponse.json(DIM2S);
        if (nodoTipo === 'dim2' && nodoId === 'd2-1') return HttpResponse.json(DIM3S);
        return HttpResponse.json([]);
      }),
    );
    render(<ConfiguracionCentrosCostoPage />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(screen.getByText('101')).toBeInTheDocument());
    expect(screen.queryByText('MCLC101')).not.toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Buscar en el catálogo'), {
      target: { value: 'gantry' },
    });
    const resultados = await screen.findByTestId('buscador-resultados');
    fireEvent.click(await within(resultados).findByText('GANTRY'));

    // La rama completa quedó expandida: dim1 101 → 20PDMC → MCLC101.
    await waitFor(() => expect(screen.getByText('MCLC101')).toBeInTheDocument());
  });

  it('axe-core: cero violations con el árbol operativo', async () => {
    const { container } = render(<ConfiguracionCentrosCostoPage />, {
      wrapper: createQueryWrapper(),
    });
    await esperarArbol();

    const results: AxeResults = await axe.run(container, {
      runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21aa'] },
    });
    if (results.violations.length > 0) {
      const detalle = results.violations
        .map((v) => `${v.id} (${v.impact}): ${v.description}`)
        .join('\n');
      throw new Error(`axe-core violations:\n${detalle}`);
    }
  });
});
