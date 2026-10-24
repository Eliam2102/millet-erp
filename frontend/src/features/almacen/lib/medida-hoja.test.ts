import { describe, expect, it } from 'vitest';
import { etiquetaMedidaMm, hojasDe, textoMedida } from '@/features/almacen/lib/medida-hoja';

describe('medida-hoja', () => {
  it('etiqueta y hojas desde m²', () => {
    expect(etiquetaMedidaMm(2440, 3660)).toBe('2440 × 3660 mm');
    expect(etiquetaMedidaMm(null, 3660)).toBeNull();
    expect(hojasDe(178.608, 8.9304)).toBe(20);
    expect(hojasDe(10, null)).toBeNull();
  });

  it('textoMedida solo aplica a filas por medida', () => {
    expect(textoMedida({ productoAwVarianteId: null }, 5)).toBeNull();
    expect(
      textoMedida({ productoAwVarianteId: 'a', anchoMm: 2140, altoMm: 3300, areaHojaM2: 7.062 }, 105.93),
    ).toBe('2140 × 3300 mm · 15 hojas');
    expect(
      textoMedida({ productoAwVarianteId: 'a', anchoMm: 2140, altoMm: 3300, areaHojaM2: 7.062 }, 7.062),
    ).toBe('2140 × 3300 mm · 1 hoja');
  });
});
