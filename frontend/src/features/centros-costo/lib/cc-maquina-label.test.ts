import { describe, expect, it } from 'vitest';
import { formatCcMaquinaLabel } from './cc-maquina-label';

/**
 * La convención de display del CC-Máquina (ADR-0050, CC-G4): "clave — nombre"
 * con em dash, y "No catalogado" solo para el dato roto irresoluble.
 */
describe('formatCcMaquinaLabel', () => {
  it('formatea clave — nombre con em dash cuando ambos están', () => {
    expect(formatCcMaquinaLabel({ clave: 'MCLC101', nombre: 'Gantry' })).toBe(
      'MCLC101 — Gantry',
    );
  });

  it('cae a la clave sola si falta el nombre', () => {
    expect(formatCcMaquinaLabel({ clave: 'MCLC101', nombre: null })).toBe(
      'MCLC101',
    );
  });

  it('"No catalogado" para null/undefined o sin clave (dato roto)', () => {
    expect(formatCcMaquinaLabel(null)).toBe('No catalogado');
    expect(formatCcMaquinaLabel(undefined)).toBe('No catalogado');
    expect(formatCcMaquinaLabel({ clave: null, nombre: 'Gantry' })).toBe(
      'No catalogado',
    );
  });
});
