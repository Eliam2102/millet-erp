import { ShieldCheck, UserRound } from 'lucide-react';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import type { AdminSection } from '@/lib/admin/registry';

/**
 * Cards del módulo Identidad en el área <c>/admin</c>. UF-Admin-PR3
 * publica la card de "Roles y permisos", que linkea al master-detail
 * dedicado <c>/admin/roles</c> donde se gestiona el rol, la matriz de
 * permisos y la asociación con grupos de Microsoft Entra ID.
 *
 * <para>UF-Admin-PR4 agrega la card de "Usuarios", master-detail
 * dedicado <c>/admin/usuarios</c> con asignación rol×empresa.</para>
 */
export const identidadAdminCards: readonly AdminSection[] = [
  {
    id: 'identidad-usuarios',
    modulo: 'identidad',
    titulo: 'Cuentas de acceso',
    descripcion: 'Cuentas de Microsoft, su perfil y su estado',
    icon: UserRound,
    href: '/admin/usuarios',
    permisoRequerido: PermisosCanonicos.IdentidadUsuariosLeer,
    orden: 10,
    grupo: 'identidad',
    displayMode: 'custom',
  },
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
