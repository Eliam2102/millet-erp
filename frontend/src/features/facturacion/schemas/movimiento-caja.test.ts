import { describe, expect, it } from 'vitest';
import {
  MOTIVO_RETIRO_MINIMO,
  TIPO_MOVIMIENTO_DEPOSITO,
  TIPO_MOVIMIENTO_RETIRO,
  errorDescripcionMovimiento,
} from '@/features/facturacion/schemas/movimiento-caja';

describe('errorDescripcionMovimiento (86e3tfdwa)', () => {
  it('exige descripción en cualquier movimiento', () => {
    expect(errorDescripcionMovimiento(TIPO_MOVIMIENTO_DEPOSITO, '   ')).toBe(
      'La descripción es obligatoria.',
    );
  });

  it('rechaza un retiro con motivo de menos de 15 caracteres sin contar espacios de los extremos', () => {
    expect(MOTIVO_RETIRO_MINIMO).toBe(15);
    expect(errorDescripcionMovimiento(TIPO_MOVIMIENTO_RETIRO, '  Camión taller  ')).toBe(
      'El motivo del retiro debe tener al menos 15 caracteres (llevas 13).',
    );
  });

  it('acepta un retiro con motivo de 15 caracteres', () => {
    expect(errorDescripcionMovimiento(TIPO_MOVIMIENTO_RETIRO, 'Reparación 1234')).toBeNull();
  });

  it('no aplica el mínimo a los depósitos', () => {
    expect(errorDescripcionMovimiento(TIPO_MOVIMIENTO_DEPOSITO, 'Depósito')).toBeNull();
  });
});
