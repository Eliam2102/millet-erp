import { PlugZap } from 'lucide-react';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import type { AdminSection } from '@/lib/admin/registry';

/**
 * Cards del módulo Integraciones.Fiscal expuestas en
 * <c>/admin</c>. Una sola card en modo <c>custom</c> que linkea al
 * master-detail dedicado (<c>/admin/integraciones/fiscal</c>):
 * configuración del PAC (FiscalAPI) + RFCs receptores por empresa.
 *
 * <para>Sigue el patrón de <c>comprasAdminCards</c> /
 * <c>administracionAdminCards</c> (ver registry.ts §UF-Admin-PR1).</para>
 */
export const integracionesFiscalAdminCards: readonly AdminSection[] = [
  {
    id: 'integraciones-fiscal',
    modulo: 'integraciones_fiscal',
    titulo: 'Integraciones fiscales',
    descripcion: 'Timbrado con el PAC y descarga de facturas del SAT',
    icon: PlugZap,
    href: '/admin/integraciones/fiscal',
    permisoRequerido: PermisosCanonicos.IntegracionesFiscalLeer,
    orden: 60,
    grupo: 'facturacion_reglas',
    displayMode: 'custom',
  },
];
