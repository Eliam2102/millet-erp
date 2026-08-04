import { describe, expect, it } from 'vitest';
import {
  Clasificacion,
  EstadoRequisicion,
  Naturaleza,
  Prioridad,
  clasificacionToString,
  estadoToKey,
  estadoToString,
  naturalezaToKey,
  naturalezaToString,
  prioridadToString,
} from '@/features/compras/api/types';

describe('enums (mirror manual del backend)', () => {
  it('EstadoRequisicion: 8 valores numéricos 0-7', () => {
    expect(EstadoRequisicion.Borrador).toBe(0);
    expect(EstadoRequisicion.EnAutorizacion).toBe(1);
    expect(EstadoRequisicion.Autorizada).toBe(2);
    expect(EstadoRequisicion.EnSurtido).toBe(3);
    expect(EstadoRequisicion.Cerrada).toBe(4);
    expect(EstadoRequisicion.Cancelada).toBe(5);
    expect(EstadoRequisicion.Rechazada).toBe(6);
    expect(EstadoRequisicion.Eliminada).toBe(7);
    // ADR-0043: terminales de cierre manual.
    expect(EstadoRequisicion.CerradaSinSurtir).toBe(8);
    expect(EstadoRequisicion.CerradaSurtidaParcial).toBe(9);
    expect(Object.keys(EstadoRequisicion)).toHaveLength(10);
  });

  it('Clasificacion: 4 valores 0-3', () => {
    expect(Clasificacion.Servicio).toBe(0);
    expect(Clasificacion.OrdenCompra).toBe(1);
    expect(Clasificacion.MateriaPrima).toBe(2);
    expect(Clasificacion.Pinturas).toBe(3);
  });

  it('Prioridad: 3 valores 0-2', () => {
    expect(Prioridad.Baja).toBe(0);
    expect(Prioridad.Normal).toBe(1);
    expect(Prioridad.Alta).toBe(2);
  });

  it('Naturaleza: 4 valores 0-3', () => {
    expect(Naturaleza.Estandar).toBe(0);
    expect(Naturaleza.Servicio).toBe(1);
    expect(Naturaleza.Critico).toBe(2);
    expect(Naturaleza.Riesgo).toBe(3);
  });
});

describe('helpers de label', () => {
  it('estadoToString cubre los 8 estados con labels en español', () => {
    expect(estadoToString(EstadoRequisicion.Borrador)).toBe('Borrador');
    expect(estadoToString(EstadoRequisicion.EnAutorizacion)).toBe('En autorización');
    expect(estadoToString(EstadoRequisicion.EnSurtido)).toBe('En surtido');
  });

  it('estadoToKey devuelve la key del enum (sin espacios) — alimentar glosario', () => {
    expect(estadoToKey(EstadoRequisicion.EnAutorizacion)).toBe('EnAutorizacion');
    expect(estadoToKey(EstadoRequisicion.EnSurtido)).toBe('EnSurtido');
  });

  it('naturalezaToString incluye tildes; naturalezaToKey no', () => {
    expect(naturalezaToString(Naturaleza.Critico)).toBe('Crítico');
    expect(naturalezaToKey(Naturaleza.Critico)).toBe('Critico');
    expect(naturalezaToString(Naturaleza.Estandar)).toBe('Estándar');
    expect(naturalezaToKey(Naturaleza.Estandar)).toBe('Estandar');
  });

  it('clasificacionToString y prioridadToString', () => {
    expect(clasificacionToString(Clasificacion.OrdenCompra)).toBe(
      'Orden de compra',
    );
    expect(clasificacionToString(Clasificacion.MateriaPrima)).toBe(
      'Materia prima',
    );
    expect(prioridadToString(Prioridad.Alta)).toBe('Alta');
  });
});
