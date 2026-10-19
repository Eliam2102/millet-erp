import { describe, expect, it } from 'vitest';
import { etiquetaAviso, tonoPlazo, umbralesSla } from './sla-revision';

describe('sla-revision', () => {
  it('umbrales por plazo', () => {
    expect(umbralesSla(5)).toEqual({ aviso: 3, vencido: 5, escalamiento: 10 });
    expect(umbralesSla(15)).toEqual({ aviso: 8, vencido: 12, escalamiento: 20 });
    expect(umbralesSla(10)).toEqual({ aviso: 6, vencido: 10, escalamiento: 20 });
  });

  it('tono del plazo', () => {
    expect(tonoPlazo(2, 5)).toBe('success');
    expect(tonoPlazo(3, 5)).toBe('warning');
    expect(tonoPlazo(5, 5)).toBe('danger');
    expect(tonoPlazo(7, 15)).toBe('success');
    expect(tonoPlazo(9, null)).toBe('neutral');
  });

  it('etiqueta del aviso', () => {
    expect(etiquetaAviso(null)).toBeNull();
    expect(etiquetaAviso(1)).toEqual({ texto: 'Aviso enviado', tono: 'warning' });
    expect(etiquetaAviso(2)).toEqual({ texto: 'Plazo vencido', tono: 'danger' });
    expect(etiquetaAviso(3)).toEqual({ texto: 'Escalada a Dirección de Finanzas', tono: 'danger' });
  });
});
