import { describe, expect, it } from 'vitest';
import { EstadoTimbrado } from '@/features/facturacion/api/types';
import { FacturasSearchSchema } from '@/features/facturacion/lib/facturas-search-schema';

describe('FacturasSearchSchema (prueba manual S3-4, M17)', () => {
  it('acepta el estado Por autorizar (9)', () => {
    expect(FacturasSearchSchema.parse({ estado: 9 }).estado).toBe(EstadoTimbrado.PorAutorizar);
  });

  it('acepta todos los estados de timbrado del enum', () => {
    for (const valor of Object.values(EstadoTimbrado)) {
      expect(FacturasSearchSchema.parse({ estado: valor }).estado).toBe(valor);
    }
  });

  it('un estado desconocido cae a Todos sin perder los demás filtros', () => {
    const r = FacturasSearchSchema.parse({ estado: 99, q: 'FA-1' });
    expect(r.estado).toBeUndefined();
    expect(r.q).toBe('FA-1');
    expect(FacturasSearchSchema.parse({ estado: 'x' }).estado).toBeUndefined();
  });
});
