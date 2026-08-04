import { ShoppingCart } from 'lucide-react';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import type { AdminSection } from '@/lib/admin/registry';

/**
 * Cards del módulo Compras que se exponen en el área de Administración
 * (<c>/admin</c>). UF-Admin-PR1 publica una sola card en modo
 * <c>custom</c>: linkea al form dedicado de configuración de Compras
 * (<c>/compras/configuracion</c>) que vive con el resto del módulo. El
 * endpoint genérico <c>GET /api/v1/compras/settings/schema</c> sigue
 * disponible y se expondrá cuando el form genérico se necesite.
 *
 * <para><b>¿Por qué el módulo Compras es el exemplar?</b> Es el módulo
 * de back-office más maduro y tiene el primer setting persistido
 * (<c>AutoGenerarOcAlAutorizar</c>). Sirve como referencia visual de
 * cómo replicar el patrón en CxC, OC, CxP, Activos, Contabilidad, BI.</para>
 */
export const comprasAdminCards: readonly AdminSection[] = [
  {
    id: 'compras-configuracion',
    modulo: 'compras',
    titulo: 'Compras',
    descripcion:
      'Configuración del módulo Compras (auto-generación de OC, umbrales, etc.).',
    icon: ShoppingCart,
    href: '/compras/configuracion',
    permisoRequerido: PermisosCanonicos.ComprasConfiguracionLeer,
    orden: 10,
    grupo: 'modulos',
    displayMode: 'custom',
  },
];
