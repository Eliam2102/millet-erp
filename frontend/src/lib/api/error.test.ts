import { describe, expect, it } from 'vitest';
import {
  ApiError,
  esApiError,
  esCodigoEspecifico,
  esConflictoConcurrencia,
  esErrorDeCliente,
  esErrorDeServidor,
  esIdempotencyEnCurso,
} from '@/lib/api/error';

function buildError(
  status: number,
  code?: string,
  retryAfterSeconds: number | null = null,
) {
  return new ApiError(
    {
      type: 'about:blank',
      title: `HTTP ${status}`,
      status,
      ...(code !== undefined ? { code } : {}),
    },
    status,
    retryAfterSeconds,
  );
}

describe('ApiError', () => {
  it('preserva status, code y traceId del problem', () => {
    const error = new ApiError(
      {
        type: 'about:blank',
        title: 'Bad request',
        status: 400,
        code: 'TRANSMITIR_SIN_LINEAS',
        traceId: '00-abc-def-01',
      },
      400,
    );
    expect(error.status).toBe(400);
    expect(error.code).toBe('TRANSMITIR_SIN_LINEAS');
    expect(error.traceId).toBe('00-abc-def-01');
    expect(error.message).toBe('Bad request');
    expect(error.name).toBe('ApiError');
  });

  it('cae a la message "HTTP <status>" cuando el title está vacío', () => {
    const error = new ApiError(
      { type: 'about:blank', title: '', status: 503 },
      503,
    );
    expect(error.message).toBe('HTTP 503');
  });

  it('expone retryAfterSeconds explícitamente', () => {
    const error = buildError(409, 'IDEMPOTENCY_IN_PROGRESS', 2);
    expect(error.retryAfterSeconds).toBe(2);
  });
});

describe('helpers de detección', () => {
  it('esApiError: type guard sobre instancia', () => {
    expect(esApiError(buildError(404))).toBe(true);
    expect(esApiError(new Error('boom'))).toBe(false);
    expect(esApiError(undefined)).toBe(false);
    expect(esApiError({ status: 404 })).toBe(false);
  });

  it('esConflictoConcurrencia: 409 + CONCURRENCY_CONFLICT', () => {
    expect(esConflictoConcurrencia(buildError(409, 'CONCURRENCY_CONFLICT'))).toBe(
      true,
    );
    expect(esConflictoConcurrencia(buildError(409, 'IDEMPOTENCY_IN_PROGRESS'))).toBe(
      false,
    );
    expect(esConflictoConcurrencia(buildError(404, 'CONCURRENCY_CONFLICT'))).toBe(
      false,
    );
  });

  it('esIdempotencyEnCurso: 409 + IDEMPOTENCY_IN_PROGRESS', () => {
    expect(esIdempotencyEnCurso(buildError(409, 'IDEMPOTENCY_IN_PROGRESS'))).toBe(
      true,
    );
    // Otros IDEMPOTENCY_* (e.g. KEY_REUSED_WITH_DIFFERENT_BODY) NO califican
    // como "en curso" — son fallos del cliente.
    expect(
      esIdempotencyEnCurso(
        buildError(422, 'IDEMPOTENCY_KEY_REUSED_WITH_DIFFERENT_BODY'),
      ),
    ).toBe(false);
  });

  it('esErrorDeCliente / esErrorDeServidor', () => {
    expect(esErrorDeCliente(buildError(400))).toBe(true);
    expect(esErrorDeCliente(buildError(499))).toBe(true);
    expect(esErrorDeCliente(buildError(500))).toBe(false);
    expect(esErrorDeServidor(buildError(500))).toBe(true);
    expect(esErrorDeServidor(buildError(503))).toBe(true);
    expect(esErrorDeServidor(buildError(404))).toBe(false);
  });

  it('esCodigoEspecifico matchea code exacto', () => {
    const e = buildError(422, 'TRANSMITIR_SIN_LINEAS');
    expect(esCodigoEspecifico(e, 'TRANSMITIR_SIN_LINEAS')).toBe(true);
    expect(esCodigoEspecifico(e, 'OTRO_CODIGO')).toBe(false);
    expect(esCodigoEspecifico(new Error('boom'), 'TRANSMITIR_SIN_LINEAS')).toBe(
      false,
    );
  });
});
