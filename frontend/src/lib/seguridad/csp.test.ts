import { describe, expect, it } from 'vitest';
import { cabecerasSeguridad, construirCsp } from '@/lib/seguridad/csp';

const directiva = (csp: string, nombre: string) =>
  csp.split('; ').find((d) => d.startsWith(`${nombre} `)) ?? '';

describe('construirCsp (86e3x819w)', () => {
  const csp = construirCsp('https://api.ejemplo.test/');

  it('permite la API por HTTPS y por WebSocket (SignalR), además de Entra', () => {
    const conectar = directiva(csp, 'connect-src');
    expect(conectar).toContain('https://api.ejemplo.test');
    expect(conectar).toContain('wss://api.ejemplo.test');
    expect(conectar).toContain('https://login.microsoftonline.com');
    expect(conectar).not.toContain('/ '); // origen sin ruta
  });

  it('no permite scripts en línea ni eval', () => {
    expect(directiva(csp, 'script-src')).toBe("script-src 'self'");
  });

  it('impide que otro sitio incruste el ERP', () => {
    expect(directiva(csp, 'frame-ancestors')).toBe("frame-ancestors 'none'");
  });

  it('deja ver PDF en blob (embed e iframe) y las fuentes de Google', () => {
    expect(directiva(csp, 'object-src')).toContain('blob:');
    expect(directiva(csp, 'frame-src')).toContain('blob:');
    expect(directiva(csp, 'font-src')).toContain('https://fonts.gstatic.com');
    expect(directiva(csp, 'style-src')).toContain('https://fonts.googleapis.com');
  });

  it('sin URL de API válida sólo deja el propio origen y Entra', () => {
    for (const malo of [undefined, '', 'no es url', 'javascript:alert(1)']) {
      expect(directiva(construirCsp(malo), 'connect-src')).toBe(
        "connect-src 'self' https://login.microsoftonline.com",
      );
    }
  });

  it('local por http usa ws:', () => {
    expect(directiva(construirCsp('http://localhost:5000'), 'connect-src')).toContain('ws://localhost:5000');
  });

  it('agrega HSTS y Permissions-Policy junto con la CSP', () => {
    const h = cabecerasSeguridad('https://api.example.com');
    expect(Object.keys(h)).toEqual(['Content-Security-Policy', 'Strict-Transport-Security', 'Permissions-Policy']);
  });
});
