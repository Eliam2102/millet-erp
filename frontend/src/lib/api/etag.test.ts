import { describe, expect, it } from 'vitest';
import { extractEtag, ifMatch } from '@/lib/api/etag';

function responseWithEtag(value: string | null): Response {
  const headers = new Headers();
  if (value !== null) headers.set('ETag', value);
  return new Response(null, { headers });
}

describe('extractEtag', () => {
  it('quita comillas dobles', () => {
    expect(extractEtag(responseWithEtag('"42"'))).toBe('42');
  });

  it('quita prefijo W/ de weak ETag', () => {
    expect(extractEtag(responseWithEtag('W/"42"'))).toBe('42');
  });

  it('devuelve undefined cuando el header no existe', () => {
    expect(extractEtag(responseWithEtag(null))).toBeUndefined();
  });

  it('soporta ETag sin comillas (servidores no estrictos)', () => {
    expect(extractEtag(responseWithEtag('42'))).toBe('42');
  });
});

describe('ifMatch', () => {
  it('emite el header con comillas (RFC 7232)', () => {
    expect(ifMatch('42')).toEqual({ 'If-Match': '"42"' });
  });

  it('devuelve {} con etag vacío, null o undefined', () => {
    expect(ifMatch('')).toEqual({});
    expect(ifMatch(null)).toEqual({});
    expect(ifMatch(undefined)).toEqual({});
  });
});
