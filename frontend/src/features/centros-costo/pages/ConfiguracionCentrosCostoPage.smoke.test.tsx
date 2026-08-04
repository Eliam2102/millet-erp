import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { createQueryWrapper } from '@/test/test-query-client';
import { ConfiguracionCentrosCostoPage } from './ConfiguracionCentrosCostoPage';
import { etiquetaNivel } from '@/features/centros-costo/lib/etiquetas';
import {
  abrirMenuAcciones,
  conPermisos,
  esperarArbol,
  instalarHandlers,
  limpiarAuth,
  type Capturada,
} from './__fixtures__/configuracion-test-harness';

/**
 * DoD de FE-PR2, parte CRUD ("Contabilidad opera el árbol end-to-end"):
 * gating por permiso, padre heredado en el título, POST con
 * Idempotency-Key y PATCH con If-Match capturado del ETag del detalle
 * (la prueba end-to-end de la infra que este PR estrena en el catálogo).
 * Vocabulario esperado construido VÍA el helper (la lint rule prohíbe los
 * literales). Dividido del resto de interacciones por memoria de jsdom
 * (ver el arnés).
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

describe('<ConfiguracionCentrosCostoPage> — CRUD (DoD FE-PR2)', () => {
  it('con solo catalogo.leer NO hay acciones de mutación (consulta pura)', async () => {
    conPermisos(['centros_costo.catalogo.leer']);
    render(<ConfiguracionCentrosCostoPage />, { wrapper: createQueryWrapper() });
    await esperarArbol();

    expect(
      screen.queryByRole('button', { name: /Acciones de/ }),
    ).not.toBeInTheDocument();
    expect(screen.queryByText(/^Nueva /)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Grupos' })).not.toBeInTheDocument();
  });

  it('el PADRE HEREDADO va en el título del modal de crear hijo', async () => {
    render(<ConfiguracionCentrosCostoPage />, { wrapper: createQueryWrapper() });
    await esperarArbol();

    abrirMenuAcciones('101');
    const d2 = etiquetaNivel('dim2', 'configuracion');
    fireEvent.click(await screen.findByText(`Nueva ${d2} aquí`));

    expect(
      await screen.findByText(`Nueva ${d2} en 101 — CONKAL`),
    ).toBeInTheDocument();
  });

  it('el selector de grupo es un combobox buscable (no un Select) dentro del modal', async () => {
    // Fix de UX: 44 grupos desbordaban un Select sin buscador. El control
    // del grupo pasó a CatalogoEagerCombobox (⇅), montado DENTRO del Dialog
    // — esta aserción prueba el swap y que el Popover-en-Dialog monta.
    render(<ConfiguracionCentrosCostoPage />, { wrapper: createQueryWrapper() });
    await esperarArbol();

    abrirMenuAcciones('101');
    const d2 = etiquetaNivel('dim2', 'configuracion');
    fireEvent.click(await screen.findByText(`Nueva ${d2} aquí`));

    // El trigger del combobox (role combobox + aria-label del selector de
    // grupo); un Select nativo no expone esta combinación.
    expect(
      await screen.findByRole('combobox', { name: 'Seleccionar grupo' }),
    ).toBeInTheDocument();
  });

  it('crear una raíz manda POST con Idempotency-Key', async () => {
    render(<ConfiguracionCentrosCostoPage />, { wrapper: createQueryWrapper() });
    await esperarArbol();

    const d1 = etiquetaNivel('dim1', 'configuracion');
    fireEvent.click(screen.getByRole('button', { name: `Nueva ${d1}` }));
    fireEvent.change(screen.getByLabelText(/Clave/), { target: { value: '106' } });
    fireEvent.change(screen.getByLabelText(/Nombre/), { target: { value: 'PLANTA NUEVA' } });
    fireEvent.click(screen.getByRole('button', { name: 'Guardar' }));

    await waitFor(() => expect(capturadas).toHaveLength(1));
    expect(capturadas[0].metodo).toBe('POST');
    expect(capturadas[0].url).toBe('/dim1/');
    expect(capturadas[0].idempotencyKey).toBeTruthy();
    expect(capturadas[0].body).toMatchObject({ clave: '106', nombre: 'PLANTA NUEVA' });
  });

  it('editar manda PATCH con el If-Match capturado del ETag del detalle (end-to-end)', async () => {
    render(<ConfiguracionCentrosCostoPage />, { wrapper: createQueryWrapper() });
    await esperarArbol();

    fireEvent.click(screen.getByRole('button', { name: 'Expandir 20PDMC' }));
    await waitFor(() => expect(screen.getByText('MCLC101')).toBeInTheDocument());

    abrirMenuAcciones('MCLC101');
    fireEvent.click(await screen.findByText('Editar'));

    await waitFor(() =>
      expect(screen.getByLabelText(/Nombre/)).toHaveValue('GANTRY'),
    );
    fireEvent.change(screen.getByLabelText(/Nombre/), {
      target: { value: 'GANTRY 2' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Guardar' }));

    await waitFor(() => expect(capturadas).toHaveLength(1));
    expect(capturadas[0].metodo).toBe('PATCH');
    expect(capturadas[0].url).toBe('/dim3/d3-1');
    expect(capturadas[0].ifMatch).toBe('"5"'); // RFC 7232, entrecomillado
  });
});
