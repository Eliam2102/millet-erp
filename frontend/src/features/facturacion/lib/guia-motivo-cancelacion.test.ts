import { describe, expect, it } from 'vitest';
import { GUIA_MOTIVO_CANCELACION, textoAceptacion } from '@/features/facturacion/lib/guia-motivo-cancelacion';

describe('guía por motivo de cancelación', () => {
  it('solo el 01 emite primero; el 03 no emite factura nueva', () => {
    expect(GUIA_MOTIVO_CANCELACION['01']).toMatchObject({ cancelaPrimero: false, emiteNueva: true });
    expect(GUIA_MOTIVO_CANCELACION['02']).toMatchObject({ cancelaPrimero: true, emiteNueva: true });
    expect(GUIA_MOTIVO_CANCELACION['03']).toMatchObject({ cancelaPrimero: true, emiteNueva: false });
    expect(GUIA_MOTIVO_CANCELACION['04']).toMatchObject({ cancelaPrimero: true, emiteNueva: true });
  });

  it('explica la aceptación del receptor', () => {
    expect(textoAceptacion(true, [])).toMatch(/debe aceptar/);
    expect(textoAceptacion(false, ['Público en general'])).toBe(
      'No requiere aceptación del receptor: público en general.',
    );
  });
});
