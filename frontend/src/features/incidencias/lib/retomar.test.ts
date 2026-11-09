import { describe, expect, it } from 'vitest';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { puedeRetomar } from './retomar';

const proveedor = '/admin/datos-maestros/proveedores/019e0000-0000-7000-8000-000000000001';

describe('puedeRetomar (B10b · U-4)', () => {
  it('sin permiso de Proveedores no ofrece retomar en el expediente del proveedor', () => {
    expect(puedeRetomar(proveedor, [])).toBe(false);
    expect(puedeRetomar(proveedor, [P.CompartidoCatalogosAdministrar])).toBe(false);
  });

  it('con permiso de Proveedores sí', () => {
    expect(puedeRetomar(proveedor, [P.DatosMaestrosProveedoresGestionar])).toBe(true);
  });

  it('aplica la guarda del menú a las rutas de módulo e ignora la query', () => {
    const ruta = '/almacen/asignaciones?articuloId=019e0000-0000-7000-8000-000000000002';
    expect(puedeRetomar(ruta, [])).toBe(false);
    expect(puedeRetomar(ruta, [P.AlmacenAsignacionesRead])).toBe(true);
  });
});
