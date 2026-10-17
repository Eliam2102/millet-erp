import { describe, expect, it } from 'vitest';
import { offsetLimitAPagina, paginaAOffsetLimit } from '@/components/erp/data/paginacion';

describe('paginacion', () => {
  it('page/pageSize → offset/limit con defaults', () => {
    expect(paginaAOffsetLimit({})).toEqual({ offset: 0, limit: 25 });
    expect(paginaAOffsetLimit({ page: 3, pageSize: 50 })).toEqual({ offset: 100, limit: 50 });
  });

  it('offset/limit → page/pageSize omite los defaults', () => {
    expect(offsetLimitAPagina(0, 25)).toEqual({ page: undefined, pageSize: undefined });
    expect(offsetLimitAPagina(100, 50)).toEqual({ page: 3, pageSize: 50 });
  });
});
