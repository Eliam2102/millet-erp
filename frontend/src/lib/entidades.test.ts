import { describe, expect, it } from 'vitest';
import { nombreEntidad, textoActividad } from './entidades';

describe('nombres de actividad', () => {
  it.each([
    ['SolicitudCatalogo', 'Solicitud de catálogo'],
    ['ImportacionCatalogo', 'Importación de catálogo'],
    ['FacturaProveedor', 'Factura de proveedor'],
    ['OrdenCompra', 'Orden de compra'],
    ['Requisicion', 'Requisición'],
    ['SesionUsuario', 'Sesión de usuario'],
    ['DocumentoFiscalNuevo', 'Documento fiscal nuevo'],
    ['XMLRecibido', 'Xml recibido'],
  ])('traduce %s', (entidad, esperado) => expect(nombreEntidad(entidad)).toBe(esperado));
  it('traduce y elimina entidades repetidas sin alterar folios', () => {
    expect(
      textoActividad('Modificó SolicitudCatalogo SolicitudCatalogo', 'SolicitudCatalogo'),
    ).toBe('Modificó Solicitud de catálogo');
    expect(textoActividad('OrdenCompra OC-001', 'OrdenCompra')).toBe('Orden de compra OC-001');
    expect(textoActividad('Creó DocumentoFiscalNuevo', 'DocumentoFiscalNuevo')).toBe(
      'Creó Documento fiscal nuevo',
    );
  });
});
