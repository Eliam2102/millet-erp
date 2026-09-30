import { describe, expect, it } from 'vitest';
import { aplanarDetalle } from '@/modules/datos-maestros/api/clientes';

describe('aplanarDetalle', () => {
  it('expone los contadores y el estado del resumen anidado del backend', () => {
    const d = aplanarDetalle({
      ejecucion: { id: 'e1', tipo: 'Barrido', estado: 'Parcial', leidos: 4, creados: 3 } as never,
      errores: [{ referencia: '91002', codigo: 'moneda_sin_equivalencia', mensaje: 'x' }],
      erroresTruncados: false,
    });
    expect(d.estado).toBe('Parcial');
    expect(d.leidos).toBe(4);
    expect(d.errores).toHaveLength(1);
  });
});
