import { describe, expect, it } from 'vitest';
import { esRechazoTipoCambio, textoCausa, tiposCambioDelDetalle } from './motor';

describe('M-207 · rechazos por tipo de cambio', () => {
  it('lee el TC del documento y del catálogo del detalle y calcula la diferencia', () => {
    expect(tiposCambioDelDetalle('18.9500|18.4520')).toEqual({ documento: 18.95, catalogo: 18.452, diferenciaPct: 2.7 });
    expect(tiposCambioDelDetalle('USD')).toBeNull();
    expect(tiposCambioDelDetalle(null)).toBeNull();
  });

  it('reconoce las causas de tipo de cambio y las nombra en español', () => {
    expect(esRechazoTipoCambio('TipoCambioFueraDeTolerancia')).toBe(true);
    expect(esRechazoTipoCambio('TipoCambioFaltante')).toBe(true);
    expect(esRechazoTipoCambio('RolSinCuenta')).toBe(false);
    expect(textoCausa('TipoCambioFueraDeTolerancia')).toBe('Tipo de cambio fuera de tolerancia');
    expect(textoCausa('TipoCambioFaltante')).toBe('Sin tipo de cambio');
  });
});
