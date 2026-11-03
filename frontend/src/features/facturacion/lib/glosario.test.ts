import { describe, expect, it } from 'vitest';
import { ComportamientoFiscal } from '@/features/facturacion/api/types';
import {
  ETIQUETA_COMPORTAMIENTO_FISCAL,
  OPCIONES_COMPORTAMIENTO_FISCAL,
  opcionesComportamientoFiscal,
} from '@/features/facturacion/lib/glosario';

describe('comportamiento fiscal (86e3wzcn4)', () => {
  it('los documentos nuevos no ofrecen «Traslado (Carta Porte)»', () => {
    const valores = OPCIONES_COMPORTAMIENTO_FISCAL.map((o) => o.value);
    expect(valores).not.toContain(ComportamientoFiscal.TrasladoConCartaPorte);
    expect(opcionesComportamientoFiscal(ComportamientoFiscal.MostradorInmediato)).toBe(
      OPCIONES_COMPORTAMIENTO_FISCAL,
    );
    expect(opcionesComportamientoFiscal(undefined)).toBe(OPCIONES_COMPORTAMIENTO_FISCAL);
  });

  it('un documento guardado con esa opción la conserva y se sigue leyendo', () => {
    const opciones = opcionesComportamientoFiscal(ComportamientoFiscal.TrasladoConCartaPorte);
    expect(opciones.at(-1)).toEqual({
      value: ComportamientoFiscal.TrasladoConCartaPorte,
      label: 'Traslado (Carta Porte)',
    });
    expect(ETIQUETA_COMPORTAMIENTO_FISCAL.TrasladoConCartaPorte).toBe('Traslado (Carta Porte)');
  });
});
