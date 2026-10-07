import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { SheetSincronizacionProductosAw } from '@/modules/datos-maestros/components/SheetSincronizacionProductosAw';
import {
  ProductoAwBajaAviso,
  ProductoAwComposicion,
  ProductoAwOrigenSection,
  ProductoAwVariantesTable,
} from '@/modules/datos-maestros/components/ProductoAwOrigenSection';
import { ListaProductosAwCompacta } from '@/modules/datos-maestros/components/ListaProductosAwCompacta';
import { ProductosAwLayout } from '@/modules/datos-maestros/components/ProductosAwLayout';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import type { ProductoAwDetalle } from '@/modules/datos-maestros/api/types';

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children, to, ...rest }: { children: React.ReactNode; to: string }) => (
    <a href={to} {...rest}>{children}</a>
  ),
}));
vi.mock('@/modules/datos-maestros/components/nuevo-producto-aw-context', () => ({
  useNuevoProductoAw: () => ({ abrir: () => {} }),
}));

function setPermisos(permisos: string[]) {
  useAuthStore.setState({
    status: 'authenticated', accessToken: 't', expiresAt: new Date(Date.now() + 3600_000),
    user: { id: 'u', email: 'a@b.com', nombre: 'A' }, empresas: [], currentEmpresaId: 'e',
    permisos, errorMessage: null,
  });
}
const GESTIONAR = PermisosCanonicos.DatosMaestrosProductosAwGestionar;

afterEach(() => {
  useAuthStore.setState({ status: 'idle', accessToken: null, permisos: [], user: null });
});

const producto = (extra = {}) =>
  ({ id: 'p-1', referenciaExterna: 'DEMO-P001', descripcion: 'X', estatus: 0, ...extra }) as unknown as ProductoAwDetalle;

const resumen = (extra = {}) => ({
  leidos: 5, creados: 1, actualizados: 1, sinCambios: 1, pendientes: 1, conflictos: 1, errores: 1,
  erroresPorReferencia: [
    { referencia: 'P-9', codigo: 'UNIDAD_SIN_EQUIVALENCIA', mensaje: 'Producto no aplicado.' },
  ],
  ...extra,
});

describe('bandeja de productos A+W', () => {
  it('sin permiso no muestra el botón Sincronizar con A+W', async () => {
    setPermisos([]);
    mswServer.use(http.get('*/productos-aw', () => HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 })));
    render(<ProductosAwLayout idActivo={null} />, { wrapper: createQueryWrapper() });
    expect(screen.queryByRole('button', { name: /sincronizar con a\+w/i })).not.toBeInTheDocument();
  });

  it('con permiso lo muestra', async () => {
    setPermisos([GESTIONAR]);
    mswServer.use(http.get('*/productos-aw', () => HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 })));
    render(<ProductosAwLayout idActivo={null} />, { wrapper: createQueryWrapper() });
    expect(screen.getByRole('button', { name: /sincronizar con a\+w/i })).toBeInTheDocument();
  });
});

describe('<SheetSincronizacionProductosAw>', () => {
  it('muestra el resumen y el error con causa y acción sugerida', async () => {
    setPermisos([GESTIONAR]);
    mswServer.use(http.post('*/productos-aw/sincronizacion', () => HttpResponse.json(resumen())));
    render(<SheetSincronizacionProductosAw open onOpenChange={() => {}} />, { wrapper: createQueryWrapper() });
    fireEvent.click(screen.getByRole('button', { name: /iniciar sincronización/i }));
    expect(await screen.findByText('Unidad sin equivalencia')).toBeInTheDocument();
    expect(screen.getByText(/Da de alta la unidad/)).toBeInTheDocument();
    for (const t of ['Leídos', 'Creados', 'Actualizados', 'Sin cambios', 'Pendientes', 'Conflictos', 'Errores'])
      expect(screen.getByText(t)).toBeInTheDocument();
  });

  it('503 informa que el origen no está disponible', async () => {
    setPermisos([GESTIONAR]);
    mswServer.use(
      http.post('*/productos-aw/sincronizacion', () =>
        HttpResponse.json({ title: 'x', status: 503 }, { status: 503, headers: { 'Content-Type': 'application/problem+json' } }),
      ),
    );
    render(<SheetSincronizacionProductosAw open onOpenChange={() => {}} />, { wrapper: createQueryWrapper() });
    fireEvent.click(screen.getByRole('button', { name: /iniciar sincronización/i }));
    await waitFor(() => expect(screen.getByRole('button', { name: /iniciar sincronización/i })).toBeEnabled());
    expect(screen.queryByText('Leídos')).not.toBeInTheDocument();
  });
});

describe('detalle de producto A+W', () => {
  it('variantes: medida nula = "sin dato", nunca 0', () => {
    render(
      <ProductoAwVariantesTable
        variantes={[{ claveVariante: 'V1', altoMm: null, anchoMm: 1200, espesorMm: null, composicion: '6mm+PVB+6mm' }]}
      />,
    );
    expect(screen.getAllByText('sin dato')).toHaveLength(2);
    expect(screen.getByText('1200 mm')).toBeInTheDocument();
    expect(screen.getByText('6mm+PVB+6mm')).toBeInTheDocument();
    expect(screen.queryByText(/^0 mm$/)).not.toBeInTheDocument();
  });

  it('medidas: ancho antes que alto, orden numérico por ancho y luego alto, UC y conteo', () => {
    const v = (claveVariante: string, anchoMm: number | null, altoMm: number | null) =>
      ({ claveVariante, anchoMm, altoMm, espesorMm: 6, composicion: null });
    render(
      <ProductoAwVariantesTable
        unidadMedida="m²"
        variantes={[v('SD', null, null), v('B', 2440, 3660), v('A2', 1800, 2600), v('A1', 1800, 2000)]}
      />,
    );
    expect(screen.getByText(/Medidas \(4\)/)).toBeInTheDocument();
    expect(screen.getByText(/UC: m²/)).toBeInTheDocument();
    const cabecera = screen.getAllByRole('columnheader').map((c) => c.textContent);
    expect(cabecera.indexOf('Ancho')).toBeLessThan(cabecera.indexOf('Alto'));
    const claves = screen.getAllByRole('row').slice(1).map((r) => r.querySelector('td')?.textContent);
    expect(claves).toEqual(['A1', 'A2', 'B', 'SD']);
  });

  it('lista: muestra UC y "N medidas" solo con más de una variante', () => {
    const item = (id: string, numVariantes: number) =>
      ({ id, referenciaExterna: id, descripcion: 'DEMO', unidadMedida: 'm²', unidadMedidaId: null,
        categoriaId: null, claveProdServSat: null, claveUnidadSat: null, objetoImp: null,
        tasaIvaTraslado: null, origen: 'AW', datosFiscalesCompletos: true, estatus: 'Activo', numVariantes }) as never;
    render(<ListaProductosAwCompacta items={[item('DEMO-21', 21), item('DEMO-1', 1)]} idActivo={null} />);
    expect(screen.getByText('UC m² · 21 medidas')).toBeInTheDocument();
    expect(screen.getByText('UC m²')).toBeInTheDocument();
  });

  it('composición: sangría por nivel, orden de A+W y procesos atenuados', () => {
    const c = (orden: number, nivel: number, padreOrden: number | null, componenteRef: string, tipo: string | null, descripcion: string | null = 'DEMO') =>
      ({ orden, nivel, padreOrden, componenteRef, descripcion, tipo, espesorMm: null });
    render(
      <ProductoAwComposicion
        componentes={[
          c(3, 2, 2, 'DEMO-LAM', 'VLA', null),
          c(2, 1, null, 'DEMO-INS', 'Vidrio plano'),
          c(4, 3, 3, 'DEMO-600', 'Proceso'),
        ]}
      />,
    );
    expect(screen.getByText('Composición (3)')).toBeInTheDocument();
    const filas = screen.getAllByRole('treeitem');
    expect(filas.map((f) => f.querySelector('span')?.textContent)).toEqual(['DEMO-INS', 'DEMO-LAM', 'DEMO-600']);
    expect(filas.map((f) => f.getAttribute('aria-level'))).toEqual(['1', '2', '3']);
    expect(filas[0]).toHaveStyle({ paddingLeft: '0rem' });
    expect(filas[2]).toHaveStyle({ paddingLeft: '2.5rem' });
    expect(filas[2]).toHaveClass('text-muted-foreground');
    expect(filas[0]).not.toHaveClass('text-muted-foreground');
    expect(filas[1]).toHaveTextContent('sin dato');
  });

  it('composición vacía no renderiza la sección', () => {
    render(<ProductoAwComposicion componentes={[]} />);
    expect(screen.queryByRole('tree')).not.toBeInTheDocument();
  });

  it('origen: chips Tipo/Grupo/Mercancía/Modelo con valores crudos y "sin dato" si son nulos', async () => {
    setPermisos([GESTIONAR]);
    mswServer.use(http.get('*/productos-aw/p-1/sincronizacion', () => HttpResponse.json(estado())));
    const { unmount } = render(
      <ProductoAwOrigenSection producto={producto({ codigoModelo: 'DEMO-VT6', grupo: 'DEMO grupo', tipo: 'VTE', wgr: '370', wgrDescripcion: 'DEMO TEMPLADO' })} />,
      { wrapper: createQueryWrapper() },
    );
    expect(await screen.findByText('Modelo: DEMO-VT6')).toBeInTheDocument();
    expect(screen.getByText('Mercancía: 370 · DEMO TEMPLADO')).toBeInTheDocument();
    expect(screen.getByText('Grupo: DEMO grupo')).toBeInTheDocument();
    expect(screen.getByText('Tipo: VTE')).toBeInTheDocument();
    unmount();
    render(<ProductoAwOrigenSection producto={producto({ codigoModelo: null })} />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('Modelo: sin dato')).toBeInTheDocument();
    expect(screen.getByText('Mercancía: sin dato')).toBeInTheDocument();
    expect(screen.getByText('Grupo: sin dato')).toBeInTheDocument();
    expect(screen.getByText('Tipo: sin dato')).toBeInTheDocument();
  });

  it('lista: chip "N piezas" solo con numComponentes > 0', () => {
    const item = (id: string, numComponentes?: number) =>
      ({ id, referenciaExterna: id, descripcion: 'DEMO', unidadMedida: 'm²', origen: 'AW',
        datosFiscalesCompletos: true, estatus: 'Activo', numComponentes }) as never;
    render(<ListaProductosAwCompacta items={[item('DEMO-A', 9), item('DEMO-B', 0), item('DEMO-C')]} idActivo={null} />);
    expect(screen.getAllByText(/piezas$/)).toHaveLength(1);
    expect(screen.getByText('9 piezas')).toBeInTheDocument();
  });

  it('filtro Tipo: ofrece los tipos de A+W tal cual y manda ?tipo= exacto', async () => {
    setPermisos([GESTIONAR]);
    const urls: string[] = [];
    mswServer.use(
      http.get('*/productos-aw', ({ request }) => {
        urls.push(request.url);
        return HttpResponse.json({ items: [], offset: 0, limit: 200, total: 0 });
      }),
    );
    render(<ProductosAwLayout idActivo={null} />, { wrapper: createQueryWrapper() });
    await waitFor(() => expect(urls).toHaveLength(1));
    expect(new URL(urls[0]).searchParams.has('tipo')).toBe(false);
    fireEvent.click(screen.getByRole('combobox', { name: 'Tipo' }));
    for (const t of ['Vidrio plano', 'VTE', 'VLA', 'VC'])
      expect(await screen.findByRole('option', { name: t })).toBeInTheDocument();
    fireEvent.click(screen.getByRole('option', { name: 'VTE' }));
    await waitFor(() => expect(new URL(urls[urls.length - 1]).searchParams.get('tipo')).toBe('VTE'));
  });

  it('baja: aviso con fecha; sin baja no hay aviso', () => {
    const { rerender } = render(<ProductoAwBajaAviso producto={producto({ fechaBaja: '2026-09-01T00:00:00Z' })} />);
    expect(screen.getByRole('status')).toHaveTextContent(
      'Inactivo: no se usa en documentos nuevos; el historial se conserva.',
    );
    rerender(<ProductoAwBajaAviso producto={producto()} />);
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });

  const estado = (version = 3) => ({
    productoId: 'p-1', referencia: 'DEMO-P001', version,
    sincronizacion: {
      resultado: 'Aplicado', error: null,
      diferencias: JSON.stringify([{ Campo: 'Clave unidad SAT', Recibido: 'H87', Conservado: 'MTK', Motivo: 'No se sobrescribe.' }]),
      hashOrigen: 'h', leidoEnUtc: '2026-09-01T10:00:00Z', aplicadoEnUtc: '2026-09-01T10:00:00Z',
      versionContrato: 'v1', versionMapeo: 'm1', descripcionOrigen: null, unidadOrigenCruda: 'M2', bajaOrigenCruda: '0',
    },
  });

  it('origen: muestra recibido vs conservado y reintenta con If-Match; 409 no sobrescribe', async () => {
    setPermisos([GESTIONAR]);
    let ifMatch: string | null = null;
    mswServer.use(
      http.get('*/productos-aw/p-1/sincronizacion', () => HttpResponse.json(estado())),
      http.post('*/productos-aw/sincronizacion/DEMO-P001', ({ request }) => {
        ifMatch = request.headers.get('If-Match');
        return HttpResponse.json(
          { title: 'conflicto', status: 409, code: 'PRODUCTO_AW_CONFLICTO_VERSION' },
          { status: 409, headers: { 'Content-Type': 'application/problem+json' } },
        );
      }),
    );
    render(<ProductoAwOrigenSection producto={producto()} />, { wrapper: createQueryWrapper() });
    expect(await screen.findByText('Clave unidad SAT')).toBeInTheDocument();
    expect(screen.getByText('MTK')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /reintentar lectura/i }));
    await waitFor(() => expect(ifMatch).toBe('"3"'));
    // Tras el 409 el botón vuelve a estar disponible y la sección sigue mostrando lo vigente.
    await waitFor(() => expect(screen.getByRole('button', { name: /reintentar lectura/i })).toBeEnabled());
  });

  it('sin permiso no muestra la sección de origen', () => {
    setPermisos([]);
    mswServer.use(http.get('*/productos-aw/p-1/sincronizacion', () => HttpResponse.json(estado())));
    render(<ProductoAwOrigenSection producto={producto()} />, { wrapper: createQueryWrapper() });
    expect(screen.queryByText('Datos de origen A+W')).not.toBeInTheDocument();
  });
});
