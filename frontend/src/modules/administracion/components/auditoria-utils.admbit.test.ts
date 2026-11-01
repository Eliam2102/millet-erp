import { describe, expect, it } from 'vitest';
import { humanizarCampo, humanizarTextoConLookups } from './auditoria-utils';

describe('auditoria-utils · etiquetas del detalle (ADM-BIT, QA O3)', () => {
  it('el estado se llama «Estado» y el último cambio lleva acento', () => {
    expect(humanizarCampo('Estado')).toBe('Estado');
    expect(humanizarCampo('UltimoCambioEn')).toBe('Último Cambio En');
    expect(humanizarCampo('UltimoCambioPor')).toBe('Último Cambio Por');
    expect(humanizarCampo('UltimoCambioPorNombre')).toBe('Último Cambio Por');
    expect(humanizarCampo('UltimoCambioPorId')).toBe('Último Cambio Por (id)');
  });

  it('el título no repite la entidad y la nombra en español', () => {
    expect(
      humanizarTextoConLookups(
        'Modificó CuentaBancariaProveedor CuentaBancariaProveedor 0193abcd — Estado: Por validar → Activa',
      ),
    ).toBe('Modificó cuenta bancaria del proveedor — Estado: Por validar → Activa');
    expect(humanizarTextoConLookups('CuentaBancariaProveedor 0193abcd')).toBe('Cuenta bancaria del proveedor');
  });

  it('una entidad sin nombre propio se separa en palabras', () => {
    expect(humanizarTextoConLookups('Creó PoliticaCredito PoliticaCredito 0a1b2c3d')).toBe('Creó politica credito');
  });
});
