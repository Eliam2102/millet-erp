import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { mswServer } from '@/test/mocks/server';
import { createQueryWrapper } from '@/test/test-query-client';
import { SheetSincronizacionProductosAw } from '@/modules/datos-maestros/components/SheetSincronizacionProductosAw';
import {
  ProductoAwBajaAviso,
  ProductoAwOrigenSection,
  ProductoAwVariantesTable,
} from '@/modules/datos-maestros/components/ProductoAwOrigenSection';
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
