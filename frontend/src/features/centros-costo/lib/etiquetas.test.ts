import { describe, expect, it } from 'vitest';
import { etiquetaNivel, etiquetaTipoNodo } from './etiquetas';

/**
 * Pin de la tabla canónica del 05 §0 (única fuente del vocabulario
 * Dim↔etiqueta). Este archivo es — junto con el helper — el ÚNICO lugar
 * del src donde estos literales pueden vivir (exención explícita de la
 * lint rule); en todos los demás, `no-restricted-syntax` los prohíbe.
 */
describe('etiquetas — helper único Dim↔etiqueta (05 §0, 07 §0)', () => {
  it('configuración y asignación comparten vocabulario "Dimensión N" / "Grupo dimensión N"', () => {
    for (const contexto of ['configuracion', 'asignacion'] as const) {
      expect(etiquetaNivel('dim1', contexto)).toBe('Dimensión 1');
      expect(etiquetaNivel('dim2', contexto)).toBe('Dimensión 2');
      expect(etiquetaNivel('dim3', contexto)).toBe('Dimensión 3');
      expect(etiquetaNivel('grupoDim2', contexto)).toBe('Grupo dimensión 2');
      expect(etiquetaNivel('grupoDim3', contexto)).toBe('Grupo dimensión 3');
    }
  });

  it('documentos: Dim3 es "Máquina" (el campo de captura de la Fase E)', () => {
    expect(etiquetaNivel('dim3', 'documentos')).toBe('Máquina');
  });

  it('en configuración/asignación NUNCA aparece "Máquina" (05 §0)', () => {
    for (const contexto of ['configuracion', 'asignacion'] as const) {
      for (const nivel of ['dim1', 'dim2', 'dim3', 'grupoDim2', 'grupoDim3'] as const) {
        expect(etiquetaNivel(nivel, contexto)).not.toContain('Máquina');
      }
    }
  });

  it('etiquetaTipoNodo delega en el mismo contrato', () => {
    expect(etiquetaTipoNodo('dim3', 'documentos')).toBe('Máquina');
    expect(etiquetaTipoNodo('dim1', 'configuracion')).toBe('Dimensión 1');
  });
});
