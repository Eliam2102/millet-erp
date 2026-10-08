import { describe, expect, it } from 'vitest';
import { antiguedadDias, calcularPrioridad, compararPrioridad } from './prioridad';

const ahora = new Date('2026-10-08T05:00:00Z'); // 7 de octubre en la zona del ERP.
describe('prioridad de Inicio', () => {
  it.each([
    [0, undefined, 'normal'],
    [10, undefined, 'normal'],
    [11, undefined, 'atencion'],
    [25, undefined, 'atencion'],
    [26, undefined, 'critico'],
    [1, '2026-10-04', 'normal'],
    [1, '2026-10-03', 'atencion'],
    [1, '2026-09-30', 'atencion'],
    [1, '2026-09-29', 'critico'],
    [0, '2020-01-01', 'normal'],
  ] as const)('%s pendientes con fecha %s: %s', (total, fechaMasAntigua, nivel) => {
    expect(calcularPrioridad({ total, fechaMasAntigua }, ahora).nivel).toBe(nivel);
  });
  it('usa atrasadas sin inventar su antigüedad', () => {
    expect(calcularPrioridad({ total: 2, atrasadas: 1 }, ahora)).toMatchObject({
      nivel: 'atencion',
      antiguedad: '',
      dias: undefined,
    });
    expect(calcularPrioridad({ total: 11, atrasadas: 11 }, ahora).nivel).toBe('critico');
  });
  it('respeta DateOnly, zona del ERP y descarta fechas inválidas o futuras', () => {
    expect(antiguedadDias('2026-10-07', ahora)).toBe(0);
    expect(antiguedadDias('2026-10-07T05:00:00Z', ahora)).toBe(1);
    expect(antiguedadDias('inválida', ahora)).toBeUndefined();
    expect(antiguedadDias('2026-10-09', ahora)).toBeUndefined();
    expect(antiguedadDias(undefined, ahora)).toBeUndefined();
  });
  it('ordena crítico, atención, normal; desempata por antigüedad y cantidad sin mutar', () => {
    const entradas = [
      { total: 0 },
      { total: 11 },
      { total: 26 },
      { total: 1, fechaMasAntigua: '2026-09-01' },
      { total: 12 },
    ];
    const original = entradas.map((c) => calcularPrioridad(c, ahora));
    const ordenados = [...original].sort(compararPrioridad);
    expect(ordenados.map((p) => p.total)).toEqual([1, 26, 12, 11, 0]);
    expect(original[0].total).toBe(0);
    expect(compararPrioridad(original[0], original[0])).toBe(0);
  });
  it('el motivo explica la señal y no atribuye edades desconocidas', () => {
    expect(calcularPrioridad({ total: 3, fechaMasAntigua: '2026-10-02' }, ahora).motivo).toBe(
      '3 pendientes · el más antiguo: hace 5 días',
    );
    expect(calcularPrioridad({ total: 0 }, ahora).motivo).toBe('Sin pendientes');
    expect(calcularPrioridad({ total: 1 }, ahora).motivo).toBe('1 pendiente');
  });
});
