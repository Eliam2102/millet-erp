import { Truck } from 'lucide-react';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import type { AdminSection } from '@/lib/admin/registry';

/**
 * Cards del módulo Facturación expuestas en <c>/admin</c>. Una sola
 * card en modo <c>custom</c> que linkea a los catálogos administrables
 * de Carta Porte (<c>/admin/carta-porte-catalogos</c>): vehículos
 * (autotransporte) y operadores (choferes).
 *
 * <para>Sigue el patrón de <c>integracionesFiscalAdminCards</c> /
 * <c>comprasAdminCards</c> (ver registry.ts §UF-Admin-PR1). Visibilidad
 * con <c>facturacion.carta-porte.leer</c>; las mutaciones dentro de la
 * página se gatean con <c>facturacion.carta-porte.emitir</c>.</para>
 */
export const facturacionAdminCards: readonly AdminSection[] = [
  {
    id: 'facturacion-carta-porte-catalogos',
    modulo: 'facturacion',
    titulo: 'Vehículos y operadores',
    descripcion: 'Unidades y choferes para la Carta Porte',
    icon: Truck,
    href: '/admin/carta-porte-catalogos',
    permisoRequerido: PermisosCanonicos.FacturacionCartaPorteLeer,
    orden: 50,
    grupo: 'facturacion_reglas',
    displayMode: 'custom',
  },
];
