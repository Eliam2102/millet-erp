import { KeyRound } from 'lucide-react';
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
  // ELIMINADO/OCULTO: El módulo "Usuarios" ahora se gestionará
  // 100% mediante el Wizard de Empleados (Aprovisionamiento Identidad).
  /*
  {
    id: 'identidad-usuarios',
    modulo: 'identidad',
    titulo: 'Usuarios',
    descripcion:
      'Alta, edición y asignación de roles por empresa.',
    icon: Users,
    href: '/admin/usuarios',
    permisoRequerido: PermisosCanonicos.IdentidadUsuariosLeer,
    orden: 10,
    grupo: 'identidad',
    displayMode: 'custom',
  },
  */
  {
    id: 'identidad-roles',
    modulo: 'identidad',
    titulo: 'Roles y permisos',
    descripcion:
      'Roles del sistema, matriz de permisos, asociación con grupos Entra ID.',
    icon: KeyRound,
    href: '/admin/roles',
    permisoRequerido: PermisosCanonicos.IdentidadRolesLeer,
    orden: 20,
    grupo: 'identidad',
    displayMode: 'custom',
  },
];
