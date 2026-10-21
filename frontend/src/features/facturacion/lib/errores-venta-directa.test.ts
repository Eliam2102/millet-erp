import { describe, expect, it, vi } from 'vitest';
import { ApiError } from '@/lib/api/error';
import {
  aplicarErroresPedidoManual,
  errorEmisionVentaDirecta,
} from '@/features/facturacion/lib/errores-venta-directa';

function apiError(status: number, problem: Record<string, unknown>): ApiError {
  return new ApiError(
    { type: 'about:blank', title: 'Error', status, ...problem } as ConstructorParameters<
      typeof ApiError
    >[0],
    status,
  );
}

describe('aplicarErroresPedidoManual (F6.1)', () => {
  it('mapea errores[] por código aunque el campo venga en PascalCase', () => {
    const setError = vi.fn();
    const ok = aplicarErroresPedidoManual(
      { setError },
      apiError(400, {
        code: 'VALIDATION_ERROR',
        errores: [
          { campo: 'SubtipoVentaDirecta', codigo: 'PEDIDO_ACTIVO_COMPORTAMIENTO', mensaje: 'm1' },
          { campo: 'MotivoEmergencia', codigo: 'PEDIDO_EMERGENCIA_MOTIVO_REQUERIDO', mensaje: 'm2' },
          { campo: 'ClienteNombre', codigo: 'NotEmptyValidator', mensaje: 'm3' },
        ],
      }),
    );
    expect(ok).toBe(true);
    expect(setError.mock.calls.map((c) => c[0])).toEqual([
      'comportamientoFiscal',
      'motivoEmergencia',
      'clienteNombre',
    ]);
  });

  it('mapea el 403 de emergencia sin permiso al campo de clase', () => {
    const setError = vi.fn();
    const ok = aplicarErroresPedidoManual(
      { setError },
      apiError(403, { code: 'PEDIDO_EMERGENCIA_SIN_PERMISO' }),
    );
    expect(ok).toBe(true);
    expect(setError).toHaveBeenCalledWith(
      'claseManual',
      expect.objectContaining({ type: 'PEDIDO_EMERGENCIA_SIN_PERMISO' }),
      { shouldFocus: true },
    );
  });

  it('no maneja errores ajenos sin errores[]', () => {
    const setError = vi.fn();
    expect(aplicarErroresPedidoManual({ setError }, apiError(500, {}))).toBe(false);
    expect(setError).not.toHaveBeenCalled();
  });
});

describe('errorEmisionVentaDirecta (F6.1)', () => {
  it.each([
    ['SERIE_VENTA_DIRECTA_NO_CONFIGURADA', 'series'],
    ['SERIE_VD_REQUIERE_SUCURSAL', 'series'],
    ['PEDIDO_MANUAL_SIN_CLASIFICAR', 'editar-pedido'],
    ['FAC_PEDIDO_ACTIVO_COMPORTAMIENTO', 'editar-pedido'],
  ])('%s → destino %s', (code, destino) => {
    const r = errorEmisionVentaDirecta(apiError(422, { code }));
    expect(r?.destino).toBe(destino);
    expect(r?.mensaje.length).toBeGreaterThan(0);
  });

  it('null para errores de otros flujos', () => {
    expect(errorEmisionVentaDirecta(apiError(422, { code: 'OTRO' }))).toBeNull();
    expect(errorEmisionVentaDirecta(null)).toBeNull();
  });
});
