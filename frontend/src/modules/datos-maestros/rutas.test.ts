import type {} from '@/router';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { toast } from 'sonner';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';

vi.mock('sonner', () => ({ toast: { info: vi.fn() } }));
vi.mock('@/modules/datos-maestros/components/ArticulosLayout', () => ({ ArticulosLayout: () => null }));
vi.mock('@/modules/datos-maestros/components/ArticuloDetalle', () => ({ ArticuloDetalle: () => null }));
vi.mock('@/modules/datos-maestros/components/SheetNuevoArticulo', () => ({ NuevoArticuloProvider: () => null }));
vi.mock('@/modules/datos-maestros/components/ProveedoresLayout', () => ({ ProveedoresLayout: () => null }));
vi.mock('@/modules/datos-maestros/components/ProveedorDetalle', () => ({ ProveedorDetalle: () => null }));
vi.mock('@/modules/datos-maestros/components/SheetNuevoProveedor', () => ({ NuevoProveedorProvider: () => null }));

import { Route as articulos } from '@/routes/_app/admin/datos-maestros/articulos/index';
import { Route as articulo } from '@/routes/_app/admin/datos-maestros/articulos/$id';
import { Route as proveedores } from '@/routes/_app/admin/datos-maestros/proveedores/index';
import { Route as proveedor } from '@/routes/_app/admin/datos-maestros/proveedores/$id';

const permisosAnteriores = useAuthStore.getState().permisos;
afterEach(() => {
  useAuthStore.setState({ permisos: permisosAnteriores });
  vi.mocked(toast.info).mockClear();
});

// F-RQOC-03: sin el permiso de la pantalla se avisa y se regresa a Inicio (antes era en silencio).
describe.each([
  ['Artículos', articulos, P.DatosMaestrosArticulosGestionar],
  ['Artículos', articulo, P.DatosMaestrosArticulosGestionar],
  ['Proveedores', proveedores, P.DatosMaestrosProveedoresGestionar],
  ['Proveedores', proveedor, P.DatosMaestrosProveedoresGestionar],
])('guarda de %s', (nombre, ruta, permiso) => {
  it('sin permiso avisa y redirige a inicio', () => {
    useAuthStore.setState({ permisos: [P.CompartidoCatalogosAdministrar] });
    const guard = ruta.options.beforeLoad as () => void;
    expect(guard).toThrow(expect.objectContaining({ options: expect.objectContaining({ to: '/' }) }));
    expect(toast.info).toHaveBeenCalledWith(`No tienes permiso para abrir ${nombre}.`, expect.anything());
  });
  it('con permiso entra sin aviso', () => {
    useAuthStore.setState({ permisos: [permiso] });
    expect(ruta.options.beforeLoad as () => void).not.toThrow();
    expect(toast.info).not.toHaveBeenCalled();
  });
});
