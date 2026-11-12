import { describe, expect, it } from 'vitest';
import { ReglaAplicadaLiberacion, ResultadoLiberacion, TipoCorreccionLiberacion } from '@/features/cxc/api/types';
import type { DecisionLiberacionResponse, ReevaluacionLiberacionResponse } from '@/features/cxc/api/types';
import { avisoReevaluacion } from '@/features/cxc/lib/reevaluacion';

const decision = (resultado: ResultadoLiberacion): DecisionLiberacionResponse => ({
  id: 'd-1',
  pedidoRef: '6024101',
  clienteId: 'c-1',
  moneda: 'MXN',
  montoPedido: 50000,
  creditoDisponibleSnapshot: 80000,
  resultado,
  reglaAplicada: ReglaAplicadaLiberacion.Credito,
  overrideId: null,
  decididoPor: 'u-1',
  decididoEn: '2026-11-11T12:00:00Z',
});

const respuesta = (resultado: ResultadoLiberacion): ReevaluacionLiberacionResponse => ({
  anterior: decision(ResultadoLiberacion.Retenido),
  nueva: decision(resultado),
  correccion: {
    id: 'k-1',
    pedidoRef: '6024101',
    decisionRetenidaId: 'd-0',
    decisionReevaluadaId: 'd-1',
    tipo: TipoCorreccionLiberacion.SaldoAplicado,
    detalle: 'Cobro aplicado a la factura correcta',
    corregidaPor: 'u-2',
    corregidaEn: '2026-11-11T13:00:00Z',
  },
});

describe('avisoReevaluacion (M-238)', () => {
  it('avisa con éxito cuando el pedido queda liberado', () => {
    expect(avisoReevaluacion(respuesta(ResultadoLiberacion.Liberado))).toMatchObject({ tipo: 'success' });
  });

  it('avisa con advertencia cuando sigue retenido o queda pendiente', () => {
    expect(avisoReevaluacion(respuesta(ResultadoLiberacion.Retenido))).toMatchObject({ tipo: 'warning' });
    expect(avisoReevaluacion(respuesta(ResultadoLiberacion.PendienteAutorizacion))).toMatchObject({ tipo: 'warning' });
  });
});
