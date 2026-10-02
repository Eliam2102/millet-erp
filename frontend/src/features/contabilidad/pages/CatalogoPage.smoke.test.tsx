import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { conPermisos, limpiarAuth } from '@/features/centros-costo/pages/__fixtures__/configuracion-test-harness';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to, className }: { children: React.ReactNode; to: string; className?: string }) => (
    <a href={to} className={className}>{children}</a>
  ),
}));

import { CatalogoPage } from './CatalogoPage';

const { Leer, Administrar, Importar } = {
  Leer: PermisosCanonicos.ContabilidadCatalogoLeer,
  Administrar: PermisosCanonicos.ContabilidadCatalogoAdministrar,
  Importar: PermisosCanonicos.ContabilidadCatalogoImportar,
};

const nodo = (o: Record<string, unknown>) => ({
  id: 'x', codigo: 'FIX-1', nombre: 'FIX cuenta', nivel: 1, tipo: 'Titulo', activa: true,
  pendienteValidacion: false, tieneHijos: false, ...o,
});
const RAICES = [
  nodo({ id: 'a', codigo: 'FIX-100', nombre: 'FIX Activo', tieneHijos: true }),
  nodo({ id: 'p', codigo: 'FIX-900', nombre: 'FIX Sin validar', tipo: null, pendienteValidacion: true }),
  nodo({ id: 'i', codigo: 'FIX-800', nombre: 'FIX Baja', activa: false }),
];

function arbol(respuesta: (raizId: string | null) => object[], estado = 200) {
  mswServer.use(
    http.get('*/api/v1/contabilidad/cuentas/arbol', ({ request }) =>
      HttpResponse.json(respuesta(new URL(request.url).searchParams.get('raizId')), { status: estado })),
  );
}
const pagina = (items: unknown[]) => ({ items, total: items.length, offset: 0, limit: 50 });

beforeEach(() => conPermisos([Leer, Administrar, Importar]));
afterEach(limpiarAuth);

describe('<CatalogoPage>', () => {
  it('cargando: muestra skeleton y luego el árbol con insignias', async () => {
    arbol((r) => (r === null ? RAICES : [nodo({ id: 'h', codigo: 'FIX-110', nombre: 'FIX Hija' })]));
    render(<CatalogoPage />, { wrapper: createQueryWrapper() });
    expect(screen.getByTestId('arbol-cargando')).toBeInTheDocument();
    expect(await screen.findByText('FIX-100')).toBeInTheDocument();
    expect(screen.getByText('Inactiva')).toBeInTheDocument();
    // Expandir carga la hija (árbol perezoso por raizId).
    fireEvent.click(screen.getByRole('button', { name: 'Expandir FIX-100' }));
    expect(await screen.findByText('FIX-110')).toBeInTheDocument();
  });

  it('insignia pendiente: tipo nulo muestra "Pendiente" explícito, nunca un valor supuesto', async () => {
    arbol(() => RAICES);
    render(<CatalogoPage />, { wrapper: createQueryWrapper() });
    await screen.findByText('FIX-900');
    expect(screen.getByText('Tipo: Pendiente')).toBeInTheDocument();
    expect(screen.getByText('Pendiente de validación')).toBeInTheDocument();
    // Los otros dos nodos sí son Título; el pendiente no lleva ni Título ni Afectable.
    expect(screen.getAllByText('Título')).toHaveLength(2);
    expect(screen.queryByText('Afectable')).not.toBeInTheDocument();
  });

  it('vacío: catálogo vacío ofrece el CTA de importar', async () => {
    arbol(() => []);
    render(<CatalogoPage />, { wrapper: createQueryWrapper() });
    expect(await screen.findByTestId('catalogo-vacio')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Importar catálogo/ })).toHaveAttribute('href', '/contabilidad/importacion');
  });

  it('fallo recuperable: error en el árbol ofrece Reintentar y se recupera', async () => {
    let falla = true;
    mswServer.use(
      http.get('*/api/v1/contabilidad/cuentas/arbol', () =>
        falla ? HttpResponse.json({ title: 'Boom', status: 500 }, { status: 500 }) : HttpResponse.json(RAICES)),
    );
    render(<CatalogoPage />, { wrapper: createQueryWrapper() });
    fireEvent.click(await screen.findByRole('button', { name: 'Reintentar' }));
    falla = false;
    expect(await screen.findByText('FIX-100')).toBeInTheDocument();
  });

  it('sin permiso de escritura: no renderiza Nueva cuenta ni Importar (solo lectura)', async () => {
    conPermisos([Leer]);
    arbol(() => RAICES);
    render(<CatalogoPage />, { wrapper: createQueryWrapper() });
    await screen.findByText('FIX-100');
    expect(screen.queryByRole('button', { name: /Nueva cuenta/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Importar/ })).not.toBeInTheDocument();
  });

  it('con permisos de escritura: muestra Nueva cuenta e Importar', async () => {
    arbol(() => RAICES);
    render(<CatalogoPage />, { wrapper: createQueryWrapper() });
    await screen.findByText('FIX-100');
    expect(screen.getByRole('button', { name: /Nueva cuenta/ })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Importar/ })).toBeInTheDocument();
  });

  it('búsqueda con debounce 200 ms: cambia a lista y consulta con q; filtro pendientes viaja como pendientes=true', async () => {
    arbol(() => RAICES);
    const urls: URL[] = [];
    mswServer.use(
      http.get('*/api/v1/contabilidad/cuentas', ({ request }) => {
        urls.push(new URL(request.url));
        return HttpResponse.json(pagina([{
          id: 'p', codigo: 'FIX-900', nombre: 'FIX Sin validar', padreId: null, nivel: 1, naturaleza: null, tipo: null,
          estatus: 'Activo', activa: true, cuentaControl: 'Ninguna', codigoAgrupador: null, grupoReporte: null,
          pendienteValidacion: true, version: 1,
        }]));
      }),
    );
    render(<CatalogoPage />, { wrapper: createQueryWrapper() });
    await screen.findByText('FIX-100');

    const caja = screen.getByLabelText('Buscar cuenta');
    fireEvent.change(caja, { target: { value: 'F' } });
    fireEvent.change(caja, { target: { value: 'FIX' } });
    // Antes de 200 ms no hay consulta de lista.
    expect(urls).toHaveLength(0);
    await waitFor(() => expect(urls.length).toBeGreaterThan(0));
    expect(urls.at(-1)!.searchParams.get('q')).toBe('FIX');
    expect(urls.every((u) => u.searchParams.get('q') === 'FIX')).toBe(true); // una sola búsqueda, no una por tecla
    expect(await screen.findByText('Naturaleza: Pendiente')).toBeInTheDocument();

    fireEvent.click(screen.getByLabelText('Pendientes de validación'));
    await waitFor(() => expect(urls.at(-1)!.searchParams.get('pendientes')).toBe('true'));
  });

  it('lista sin resultados con filtros: mensaje de sin resultados (no el CTA de catálogo vacío)', async () => {
    arbol(() => RAICES);
    mswServer.use(http.get('*/api/v1/contabilidad/cuentas', () => HttpResponse.json(pagina([]))));
    render(<CatalogoPage />, { wrapper: createQueryWrapper() });
    await screen.findByText('FIX-100');
    fireEvent.change(screen.getByLabelText('Buscar cuenta'), { target: { value: 'zzz' } });
    expect(await screen.findByText('Sin resultados para los filtros aplicados.')).toBeInTheDocument();
    expect(screen.queryByTestId('catalogo-vacio')).not.toBeInTheDocument();
  });
});
