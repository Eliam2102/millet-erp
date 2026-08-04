import { describe, expect, it } from 'vitest';
import { crearAgregarLineaManualSchema } from '@/features/compras/ordenes/schemas/agregar-linea-manual';

/**
 * Fase E PR3.1 — el CC-Máquina pasa de opcional a REQUERIDO en la línea
 * MANUAL de OC. La exigencia es condicional (`ccRequerido`) porque el mismo
 * schema resuelve el form al editar una línea HEREDADA de RQ, donde el campo
 * es read-only y el submit manda null = "no tocar" (ADR-0050).
 */

const CC_ID = '0c000000-0000-0000-0000-000000000001';

const baseValida = {
  articuloId: '11111111-1111-4111-8111-111111111111',
  cantidad: 10,
  unidadMedida: 'PZA',
  precioUnitario: 100,
  departamentoSolicitanteId: '44444444-4444-4444-8444-444444444444',
  centroCostoId: CC_ID,
};

describe('crearAgregarLineaManualSchema — CC-Máquina obligatorio (PR3.1)', () => {
  const manual = crearAgregarLineaManualSchema(); // ccRequerido = true (default)
  const heredada = crearAgregarLineaManualSchema(() => 5, false);

  it('línea manual: parsea con CC-Máquina', () => {
    const result = manual.parse(baseValida);
    expect(result.centroCostoId).toBe(CC_ID);
  });

  it('línea manual: rechaza sin CC-Máquina (null, undefined y vacío)', () => {
    for (const valor of [null, undefined, '']) {
      expect(() =>
        manual.parse({ ...baseValida, centroCostoId: valor }),
      ).toThrow(/CC-Máquina requerido/);
    }
  });

  it('línea heredada: acepta CC-Máquina en null (el submit manda "no tocar")', () => {
    // Sin esta rama, una línea heredada legada con CC null quedaría imposible
    // de guardar: campo requerido pero pintado read-only.
    const result = heredada.parse({ ...baseValida, centroCostoId: null });
    expect(result.centroCostoId).toBeNull();
  });

  it('ambas ramas siguen rechazando un CC-Máquina con forma inválida', () => {
    expect(() =>
      manual.parse({ ...baseValida, centroCostoId: 'no-es-uuid' }),
    ).toThrow();
    expect(() =>
      heredada.parse({ ...baseValida, centroCostoId: 'no-es-uuid' }),
    ).toThrow();
  });
});
