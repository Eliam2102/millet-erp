import { adminRegistry } from '@/lib/admin/registry';
import { rutaPermitida } from '@/lib/nav';

function contieneRuta(base: string, pathname: string): boolean {
  return pathname === base || pathname.startsWith(`${base}/`);
}

/**
 * U-4 · ¿Puede el usuario abrir la pantalla donde se retoma la incidencia? El responsable puede no tener permiso del
 * paso (p. ej. quien subió un documento del proveedor no gestiona Proveedores): en ese caso «Retomar en el paso» lo
 * llevaría a una pantalla que lo regresa a Inicio. Aplica la guarda del menú (`rutaPermitida`) y, para la
 * configuración de `/admin`, el permiso de la card más específica que contiene la ruta (mismo que exige su pantalla).
 */
export function puedeRetomar(ruta: string, permisos: readonly string[]): boolean {
  const pathname = ruta.split(/[?#]/)[0] ?? ruta;
  if (!rutaPermitida(pathname, permisos)) return false;
  const cards = adminRegistry.filter((section) => contieneRuta(section.href, pathname));
  if (cards.length === 0) return true;
  const masEspecifica = cards.reduce((a, b) => (b.href.length > a.href.length ? b : a));
  return permisos.includes(masEspecifica.permisoRequerido);
}
