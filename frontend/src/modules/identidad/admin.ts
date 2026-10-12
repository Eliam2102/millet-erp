import { ShieldCheck } from 'lucide-react';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import type { AdminSection } from '@/lib/admin/registry';

/**
 * Cards del módulo Identidad en el área <c>/admin</c>. UF-Admin-PR3
 * publica la card de "Roles y permisos", que linkea al master-detail
 * dedicado <c>/admin/roles</c> donde se gestiona el rol, la matriz de
 * permisos y la asociación con grupos de Microsoft Entra ID.
 *
 * <para>UF-Admin-PR4 ya no publica la card de "Cuentas de acceso": la
 * gestión de usuarios vive en Empleados → Roles y accesos. La ruta
 * <c>/admin/usuarios</c> sigue existiendo por URL directa.</para>
 */
export const identidadAdminCards: readonly AdminSection[] = [
  {
    id: 'identidad-roles',
    modulo: 'identidad',
    titulo: 'Roles y permisos',
    descripcion: 'Qué puede ver y hacer cada perfil',
    icon: ShieldCheck,
    href: '/admin/roles',
    permisoRequerido: PermisosCanonicos.IdentidadRolesLeer,
    orden: 20,
    grupo: 'identidad',
    displayMode: 'custom',
  },
];
