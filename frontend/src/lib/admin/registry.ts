import type { LucideIcon } from 'lucide-react';

import { administracionAdminCards } from '@/modules/administracion/admin';
import { catalogosAdminCards } from '@/modules/catalogos/admin';
import { comprasAdminCards } from '@/modules/compras/admin';
import { datosMaestrosAdminCards } from '@/modules/datos-maestros/admin';
import { identidadAdminCards } from '@/modules/identidad/admin';
import { integracionesFiscalAdminCards } from '@/features/integraciones-fiscal/admin';
import { facturacionAdminCards } from '@/features/facturacion/admin';

/**
 * Registry de cards del área de Administración (<c>/admin</c>).
 *
 * <para><b>Patrón</b>: cada módulo del back-office expone un barrel
 * <c>@/modules/&lt;modulo&gt;/admin</c> con un export
 * <c>&lt;modulo&gt;AdminCards: AdminSection[]</c>. El registry consolida
 * todas las cards en una sola lista; el hook
 * <see cref="useAdminRegistry"/> la filtra por
 * <see cref="AdminSection.permisoRequerido"/> contra los permisos del
 * usuario actual.</para>
 *
 * <para><b>UF-Admin-PR1</b> incluye SOLO la card de Compras como exemplar
 * del patrón (Custom mode → linkea a <c>/compras/configuracion</c>). Las
 * cards de Empresas, Sucursales, Departamentos, Usuarios, Roles,
 * Catálogos, Series, etc. llegan con sus respectivos PRs (UF-Admin-PR2+).</para>
 */

/** Grupos visuales del landing <c>/admin</c>. */
export type AdminGrupo =
  | 'identidad'
  | 'organizacion'
  | 'catalogos'
  | 'datos_maestros'
  | 'modulos';

/**
 * Módulos del back-office que pueden registrar cards en <c>/admin</c>
 * y/o exponer su schema declarativo en
 * <c>GET /api/v1/{modulo}/settings/schema</c>. Coincide con la lista de
 * 10 módulos del back-office (CLAUDE.md §Módulos) más <c>admin</c>
 * (área transversal de organización) y <c>aw</c> (integración A+W).
 */
export type AdminModulo =
  | 'admin'
  | 'identidad'
  | 'catalogos'
  | 'datos_maestros'
  | 'almacen'
  | 'compras'
  | 'facturacion'
  | 'cxc'
  | 'cxp'
  | 'activos'
  | 'contabilidad'
  | 'reportes'
  | 'aw'
  | 'integraciones_fiscal';

/**
 * Descriptor de una card del área de Administración.
 *
 * <para><c>displayMode</c> es semánticamente paralelo al
 * <c>DisplayMode</c> del schema de settings: <c>auto</c> implica que la
 * card abre el form genérico <c>/admin/&lt;modulo&gt;/settings</c>;
 * <c>custom</c> implica que la card linkea a una UI dedicada
 * (<c>href</c> apunta directo a esa ruta).</para>
 */
export interface AdminSection {
  /** Identificador estable, formato <c>{modulo}-{recurso}</c>. */
  id: string;
  modulo: AdminModulo;
  titulo: string;
  descripcion: string;
  icon: LucideIcon;
  /** Ruta del frontend al hacer click en la card. */
  href: string;
  /** Permiso canónico requerido para ver la card. */
  permisoRequerido: string;
  /** Orden visual dentro del grupo (asc; menor número = primero). */
  orden: number;
  grupo: AdminGrupo;
  /**
   * <c>auto</c> (default): card linkea al form genérico. <c>custom</c>:
   * card linkea a la UI dedicada del módulo.
   */
  displayMode?: 'auto' | 'custom';
}

/**
 * Lista consolidada de cards del área de Administración. NO filtrar
 * acá — el filtrado por permisos vive en
 * <see cref="useAdminRegistry"/>.
 */
export const adminRegistry: readonly AdminSection[] = [
  ...identidadAdminCards,
  ...administracionAdminCards,
  ...datosMaestrosAdminCards,
  ...catalogosAdminCards,
  ...comprasAdminCards,
  ...facturacionAdminCards,
  ...integracionesFiscalAdminCards,
];
