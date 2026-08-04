import { describe, expect, it } from 'vitest';
import {
  AdminAprobadoresSearchSchema,
  DEFAULT_ADMIN_APROBADORES_SEARCH,
} from '@/features/compras/lib/admin-aprobadores-search-schema';

describe('AdminAprobadoresSearchSchema', () => {
  it('input vacío: tab=vigentes (default vía catch)', () => {
    const r = AdminAprobadoresSearchSchema.parse({});
    expect(r.tab).toBe('vigentes');
  });

  it('tab inválido cae a "vigentes" (catch en enum)', () => {
    const r = AdminAprobadoresSearchSchema.parse({ tab: 'cualquiera' });
    expect(r.tab).toBe('vigentes');
  });

  it('acepta tab=historico explícito', () => {
    const r = AdminAprobadoresSearchSchema.parse({ tab: 'historico' });
    expect(r.tab).toBe('historico');
  });

  it('coerciona rol string → number', () => {
    const r = AdminAprobadoresSearchSchema.parse({ rol: '2' });
    expect(r.rol).toBe(2);
  });

  it('rechaza rol fuera de rango (3)', () => {
    expect(() =>
      AdminAprobadoresSearchSchema.parse({ rol: 3 }),
    ).toThrow();
  });

  it('rechaza rol negativo', () => {
    expect(() =>
      AdminAprobadoresSearchSchema.parse({ rol: -1 }),
    ).toThrow();
  });

  it('acepta filtros completos juntos', () => {
    const r = AdminAprobadoresSearchSchema.parse({
      tab: 'historico',
      departamentoId: 'd-1',
      rol: 0,
      usuarioId: 'u-1',
    });
    expect(r).toEqual({
      tab: 'historico',
      departamentoId: 'd-1',
      rol: 0,
      usuarioId: 'u-1',
    });
  });

  it('DEFAULT contiene tab vigentes', () => {
    expect(DEFAULT_ADMIN_APROBADORES_SEARCH.tab).toBe('vigentes');
  });
});
