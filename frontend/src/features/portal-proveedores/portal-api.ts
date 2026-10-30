import { apiBaseUrl } from '@/lib/auth/config';

/**
 * Cliente del portal de proveedores (P8.3). Va aparte del cliente del ERP: el token del portal no es
 * el del ERP y vive en sessionStorage (se pierde al cerrar la pestaña; el token dura 8 h).
 */
const CLAVE = 'millet.portal-proveedores.token';
const PREFIJO = '/api/v1/portal-proveedores';

export interface FacturaPortal {
  id: string;
  folio: string | null;
  uuid: string | null;
  fecha: string;
  total: number;
  moneda: string;
  estado: string;
  detenida: boolean;
  motivo: string | null;
  fechaProgramadaPago: string | null;
}

export interface PagoPortal {
  pagoId: string;
  facturaId: string;
  folio: string | null;
  fechaPago: string;
  importe: number;
  complementoPendiente: number;
}

export class PortalSesionExpirada extends Error {}

export const tokenPortal = () => sessionStorage.getItem(CLAVE);
export const cerrarSesionPortal = () => sessionStorage.removeItem(CLAVE);

export async function solicitarCodigo(email: string): Promise<void> {
  const r = await fetch(`${apiBaseUrl}${PREFIJO}/codigo`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email }),
  });
  if (!r.ok) throw new Error('No se pudo solicitar el código. Intenta de nuevo.');
}

/** Devuelve false si el código no es válido (vencido, incorrecto o sin intentos). */
export async function iniciarSesion(email: string, codigo: string): Promise<boolean> {
  const r = await fetch(`${apiBaseUrl}${PREFIJO}/sesion`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email, codigo }),
  });
  if (r.status === 401) return false;
  if (!r.ok) throw new Error('No se pudo iniciar sesión. Intenta de nuevo.');
  const { token } = (await r.json()) as { token: string };
  sessionStorage.setItem(CLAVE, token);
  return true;
}

async function obtener<T>(ruta: string): Promise<T> {
  const r = await fetch(`${apiBaseUrl}${PREFIJO}${ruta}`, { headers: { Authorization: `Bearer ${tokenPortal() ?? ''}` } });
  if (r.status === 401) {
    cerrarSesionPortal();
    throw new PortalSesionExpirada();
  }
  if (!r.ok) throw new Error('No se pudo cargar la información. Intenta de nuevo.');
  return (await r.json()) as T;
}

export const listarFacturas = () => obtener<FacturaPortal[]>('/facturas');
export const listarPagos = () => obtener<PagoPortal[]>('/pagos');
