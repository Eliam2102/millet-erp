/**
 * 86e3x819w — Content-Security-Policy del frontend en Static Web Apps.
 *
 * Se arma al compilar (plugin de vite.config.ts) porque el dominio de la API
 * cambia por ambiente (VITE_API_BASE_URL). Fuentes permitidas y por qué:
 * - script-src 'self' 'wasm-unsafe-eval': bundle propio y WASM de Yoga/PDF;
 *   no permite scripts inline ni eval de JavaScript.
 * - style-src 'unsafe-inline': React y Radix usan atributos style en línea;
 *   no ejecutan código. Google Fonts sirve la hoja de IBM Plex.
 * - connect-src: la API (HTTPS y WebSocket de SignalR) y Microsoft Entra
 *   (MSAL pide y renueva tokens con fetch a login.microsoftonline.com).
 * - frame-src: Entra (renovación silenciosa por iframe cuando no hay refresh
 *   token) y blob: (visor de proformas en iframe).
 * - object-src blob:: los PDF de adjuntos, OC y CFDI se ven con <embed>.
 * - frame-ancestors 'none': nadie puede incrustar el ERP (clickjacking).
 */

const ENTRA = 'https://login.microsoftonline.com';

/** Origen (esquema + host + puerto) de una URL; null si no es http(s) válida. */
function origen(url: string | undefined): string | null {
  if (!url) return null;
  try {
    const u = new URL(url);
    return u.protocol === 'http:' || u.protocol === 'https:' ? u.origin : null;
  } catch {
    return null;
  }
}

/** Mismo origen con ws(s):, para SignalR. */
function origenWebSocket(origenHttp: string): string {
  return origenHttp.replace(/^http/, 'ws');
}

export function construirCsp(apiBaseUrl: string | undefined): string {
  const api = origen(apiBaseUrl);
  const conectar = ["'self'", ENTRA, ...(api ? [api, origenWebSocket(api)] : [])];

  const directivas: Record<string, string[]> = {
    'default-src': ["'self'"],
    'script-src': ["'self'", "'wasm-unsafe-eval'"],
    'style-src': ["'self'", "'unsafe-inline'", 'https://fonts.googleapis.com'],
    'font-src': ["'self'", 'data:', 'https://fonts.gstatic.com'],
    'img-src': ["'self'", 'data:', 'blob:'],
    'connect-src': conectar,
    'frame-src': ["'self'", 'blob:', ENTRA],
    'object-src': ["'self'", 'blob:'],
    'worker-src': ["'self'", 'blob:'],
    'base-uri': ["'self'"],
    'form-action': ["'self'", ENTRA],
    'frame-ancestors': ["'none'"],
  };

  return Object.entries(directivas)
    .map(([nombre, fuentes]) => `${nombre} ${fuentes.join(' ')}`)
    .join('; ');
}

/** Cabeceras de seguridad que se suman a las de public/staticwebapp.config.json. */
export function cabecerasSeguridad(apiBaseUrl: string | undefined): Record<string, string> {
  return {
    'Content-Security-Policy': construirCsp(apiBaseUrl),
    'Strict-Transport-Security': 'max-age=31536000; includeSubDomains',
    'Permissions-Policy': 'camera=(), microphone=(), geolocation=(), payment=()',
  };
}
