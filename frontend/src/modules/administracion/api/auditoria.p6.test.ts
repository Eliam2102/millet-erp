import { describe, expect, it } from 'vitest';
import { buildAuditoriaPath } from './auditoria';

describe('filtros de bitácora P6', () => {
  it('conserva filtros de pantalla en la exportación con caracteres reservados', () => {
    const path = buildAuditoriaPath({
      desde: '2026-10-08',
      hasta: '2026-10-09',
      q: 'Alta & cambio',
      modulo: 'Catalogos',
      recurso: 'FormaPago',
      accion: 'actualizar',
      sucursalId: 's-1',
      actorTipo: 'usuario',
      offset: 200,
      limit: 200,
    });
    const params = new URL(path.replace('auditoria?', 'auditoria/exportar?'), 'https://local.test')
      .searchParams;
    expect(params.get('q')).toBe('Alta & cambio');
    expect(params.get('recurso')).toBe('FormaPago');
    expect(params.get('sucursalId')).toBe('s-1');
    expect(params.get('accion')).toBe('actualizar');
    expect(params.get('zonaHoraria')).toBeTruthy();
  });
});
