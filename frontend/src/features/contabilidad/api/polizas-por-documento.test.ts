import { describe, expect, it } from 'vitest';
import { indexarPolizas, polizaPrincipal, type PolizaDeDocumento } from './polizas-por-documento';

const p = (id: string, estado: PolizaDeDocumento['estado'], folio: string | null = id): PolizaDeDocumento => ({
  id,
  folio,
  fecha: '2026-11-01',
  estado,
});

describe('indexarPolizas', () => {
  it('indexa por documento y tolera la respuesta vacía', () => {
    expect(indexarPolizas(undefined)).toEqual({});
    const mapa = indexarPolizas([{ documentoId: 'd1', polizas: [p('a', 'Autorizada')] }]);
    expect(mapa.d1?.[0]?.id).toBe('a');
    expect(mapa.d2).toBeUndefined();
  });
});

describe('polizaPrincipal', () => {
  it('prefiere la primera no anulada', () => {
    expect(polizaPrincipal([p('a', 'Anulada'), p('b', 'Autorizada')])?.id).toBe('b');
  });
  it('si todas están anuladas, la primera; sin pólizas, null', () => {
    expect(polizaPrincipal([p('a', 'Anulada'), p('b', 'Anulada')])?.id).toBe('a');
    expect(polizaPrincipal([])).toBeNull();
    expect(polizaPrincipal(undefined)).toBeNull();
  });
  it('un borrador cuenta como vigente (sin folio)', () => {
    expect(polizaPrincipal([p('a', 'Borrador', null)])).toMatchObject({ id: 'a', folio: null });
  });
});
