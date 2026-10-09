import { describe, expect, it } from 'vitest';
import type { AutorizacionOc } from '../api/types';
import { agruparCiclosAutorizacion } from './ciclos-autorizacion';

const firmasPrueba: AutorizacionOc[] = [
  { id: 'n1-c1', ciclo: 1, nivel: 1, resultado: 1, usuarioId: 'jefe', fechaHora: '2026-10-09T12:00:00Z', motivoRechazoId: null, motivoRechazoTexto: null, notas: null },
  { id: 'n2-c1', ciclo: 1, nivel: 2, resultado: 2, usuarioId: 'direccion', fechaHora: '2026-10-09T12:00:00Z', motivoRechazoId: 'motivo', motivoRechazoTexto: 'Corregir precio', notas: 'Revisar cotización' },
  { id: 'n1-c2', ciclo: 2, nivel: 1, resultado: 1, usuarioId: 'jefe', fechaHora: '2026-10-09T12:00:00Z', motivoRechazoId: null, motivoRechazoTexto: null, notas: null },
  { id: 'n2-c2', ciclo: 2, nivel: 2, resultado: 1, usuarioId: 'direccion', fechaHora: '2026-10-09T12:00:00Z', motivoRechazoId: null, motivoRechazoTexto: null, notas: 'Corregida' },
];

describe('Ciclos de autorización', () => {
  it('separa los ciclos con fechas iguales y conserva personas, rechazo y motivo', () => {
    const input = [...firmasPrueba].reverse();
    const copia = [...input];
    const ciclos = agruparCiclosAutorizacion(input);
    expect(ciclos.map((c) => c.ciclo)).toEqual([2, 1]);
    expect(ciclos[0].firmas.map((f) => f.id)).toEqual(['n1-c2', 'n2-c2']);
    expect(ciclos[1].firmas).toEqual(firmasPrueba.slice(0, 2));
    expect(input).toEqual(copia);
  });
  it('acepta un historial vacío', () => {
    expect(agruparCiclosAutorizacion([])).toEqual([]);
  });
});
