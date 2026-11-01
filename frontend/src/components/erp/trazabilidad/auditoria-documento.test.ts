import { describe, expect, it } from 'vitest';
import { auditoriaDocumentoRoute } from './types';

describe('auditoriaDocumentoRoute (ADM-BIT «Abrir documento»)', () => {
  it('el proveedor y el cambio de su cuenta bancaria abren la ficha del proveedor', () => {
    expect(auditoriaDocumentoRoute('CuentaBancariaProveedor', 'prov-1')).toEqual({
      to: '/admin/datos-maestros/proveedores/$id',
      params: { id: 'prov-1' },
    });
    expect(auditoriaDocumentoRoute('Proveedor', 'prov-1')?.to).toBe('/admin/datos-maestros/proveedores/$id');
  });

  it('los documentos del mapa de trazabilidad usan su ruta de detalle', () => {
    expect(auditoriaDocumentoRoute('OrdenCompra', 'oc-1')).toEqual({
      to: '/compras/ordenes/$id',
      params: { id: 'oc-1' },
    });
    expect(auditoriaDocumentoRoute('FacturaVenta', 'f-1')?.to).toBe('/facturacion/facturas/$id');
  });

  it('una entidad sin pantalla de detalle no ofrece el enlace', () => {
    expect(auditoriaDocumentoRoute('Sesion', 'u-1')).toBeNull();
  });
});
