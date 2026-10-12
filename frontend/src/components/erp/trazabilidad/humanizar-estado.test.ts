import { describe, expect, it } from 'vitest';
import { humanizarEstado } from './humanizar-estado';

describe('humanizarEstado', () => {
  it.each([
    ['EnSurtido', 'En surtido'],
    ['Autorizada', 'Autorizada'],
    ['EnAutorizacionJefeCompras', 'En autorizacion jefe compras'],
    ['', ''],
    ['En surtido', 'En surtido'],
    ['pendiente_revision', 'pendiente_revision'],
  ])('humaniza %j como %j', (entrada, esperado) => {
    expect(humanizarEstado(entrada)).toBe(esperado);
  });
});
