import { describe, expect, it } from 'vitest';
import { codigoSoporte } from '@/lib/api/error';

describe('codigoSoporte', () => {
  it('devuelve el código solo en 5xx con traceId', () => {
    expect(codigoSoporte({ status: 500, traceId: 'abc' })).toBe('Código: abc');
    expect(codigoSoporte({ status: 503, traceId: 'abc' })).toBe('Código: abc');
  });
  it('devuelve undefined en 4xx, sin traceId o sin error', () => {
    expect(codigoSoporte({ status: 422, traceId: 'abc' })).toBeUndefined();
    expect(codigoSoporte({ status: 404, traceId: 'abc' })).toBeUndefined();
    expect(codigoSoporte({ status: 500 })).toBeUndefined();
    expect(codigoSoporte(undefined)).toBeUndefined();
  });
});
