import { describe, expect, it } from 'vitest';
import { folioFactura } from './formato';

describe('folioFactura', () => {
  it('une serie y folio con guion', () => {
    expect(folioFactura({ serieProveedor: 'A', folioProveedor: '123' })).toBe('A-123');
  });
  it('omite la serie vacía', () => {
    expect(folioFactura({ serieProveedor: null, folioProveedor: '123' })).toBe('123');
  });
  it('sin folio ni serie', () => {
    expect(folioFactura({})).toBe('Sin folio');
  });
});
