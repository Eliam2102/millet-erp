import { describe, expect, it } from 'vitest';
import {
  accionHistorial,
  etiquetaEntidadHistorial,
  resumenHistorialLegible,
} from './historial-etiquetas';

describe('etiquetaEntidadHistorial', () => {
  it('usa la etiqueta de negocio para las entidades conocidas', () => {
    expect(etiquetaEntidadHistorial('OrdenCompra')).toBe('la orden de compra');
    expect(etiquetaEntidadHistorial('AutorizacionOC')).toBe('la firma de autorización');
    expect(etiquetaEntidadHistorial('LineaOrdenCompra')).toBe('una línea de la orden');
  });

  it('separa en palabras minúsculas las entidades desconocidas', () => {
    expect(etiquetaEntidadHistorial('ProveedorContacto')).toBe('proveedor contacto');
  });
});

describe('accionHistorial', () => {
  it.each([
    ['crear', 'OrdenCompra', 'Se creó la orden de compra'],
    ['actualizar', 'OrdenCompra', 'Se modificó la orden de compra'],
    ['borrar', 'LineaOrdenCompra', 'Se eliminó una línea de la orden'],
  ])('%s sobre %s → %j', (operacion, entidad, esperado) => {
    expect(accionHistorial(operacion, entidad)).toBe(esperado);
  });
});

describe('resumenHistorialLegible', () => {
  it.each([
    ['Creó AutorizacionOC AutorizacionOC 01a12f96', 'Creó la firma de autorización'],
    ['Creó LineaOrdenCompra LineaOrdenCompra 01a12ebb', 'Creó una línea de la orden'],
    ['Creó OrdenCompra OrdenCompra 3f2a9c10', 'Creó la orden de compra'],
    [
      'Modificó OrdenCompra OrdenCompra 3f2a9c10 — Estado: EnAutorizacionJefeCompras → Autorizada',
      'Modificó la orden de compra — Estado: En autorizacion jefe compras → Autorizada',
    ],
    [
      'Creó Empleado EMP-0012 · Juana Pérez',
      'Creó empleado EMP-0012 · Juana Pérez',
    ],
  ])('traduce %j', (entrada, esperado) => {
    expect(resumenHistorialLegible(entrada)).toBe(esperado);
  });

  it('no deja pasar el id crudo ni el nombre repetido', () => {
    const texto = resumenHistorialLegible('Creó AutorizacionOC AutorizacionOC 01a12f96');
    expect(texto).not.toMatch(/01a12f96/);
    expect(texto).not.toMatch(/AutorizacionOC/);
  });

  it('devuelve null cuando no hay resumen', () => {
    expect(resumenHistorialLegible(null)).toBeNull();
    expect(resumenHistorialLegible('   ')).toBeNull();
  });
});
