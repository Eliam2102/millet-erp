import {
  Briefcase,
  Building2,
  ClipboardList,
  FolderTree,
  Hash,
  Settings2,
  Share2,
  Users,
} from 'lucide-react';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import type { AdminSection } from '@/lib/admin/registry';

/**
 * Cards del área transversal de Administración (organización) que se
 * exponen en <c>/admin</c>. UF-Admin-PR6 publica la card de Series y
 * folios, UF-Admin-PR7 cierra el módulo con Parámetros y Auditoría.
 * Todas linkean a UIs dedicadas (<c>displayMode: 'custom'</c>).
 *
 * <para>Millet opera con una sola razón social (ADR-0051) — "Empresas"
 * ya no es un eje de navegación propio; Sucursales y Departamentos lo
 * reemplazan como las cards de "Organización". El detalle de la
 * empresa (RFC, régimen fiscal) sigue existiendo en
 * <c>/admin/empresas/$id</c> pero solo alcanzable desde el link
 * "avanzado" de esas dos páginas — no tiene card propia.</para>
 */
export const administracionAdminCards: readonly AdminSection[] = [
  {
    id: 'admin-sucursales',
    modulo: 'admin',
    titulo: 'Sucursales',
    descripcion:
      'Ubicaciones operativas — alcance de datos por sucursal (ADR-0051).',
    icon: Building2,
    href: '/admin/sucursales',
    permisoRequerido: PermisosCanonicos.AdminEmpresasSucursalesGestionar,
    orden: 10,
    grupo: 'organizacion',
    displayMode: 'custom',
  },
  {
    id: 'admin-departamentos',
    modulo: 'admin',
    titulo: 'Departamentos',
    descripcion: 'Catálogo organizacional — asignación por sucursal.',
    icon: FolderTree,
    href: '/admin/departamentos',
    permisoRequerido: PermisosCanonicos.AdminDepartamentosGestionar,
    orden: 11,
    grupo: 'organizacion',
    displayMode: 'custom',
  },
  {
    id: 'admin-canales-venta',
    modulo: 'admin',
    titulo: 'Canales de venta',
    descripcion:
      'Catálogo del eje organizacional de facturación; clave A+W (GRUPPE) para la ingesta de pedidos.',
    icon: Share2,
    href: '/admin/canales-venta',
    permisoRequerido: PermisosCanonicos.AdminEmpresasSucursalesGestionar,
    orden: 15,
    grupo: 'organizacion',
    displayMode: 'custom',
  },
  {
    id: 'admin-puestos',
    modulo: 'admin',
    titulo: 'Puestos',
    descripcion:
      'Catálogo de puestos — llave de las políticas de viáticos por puesto y destino.',
    icon: Briefcase,
    href: '/admin/puestos',
    permisoRequerido: PermisosCanonicos.AdminPuestosGestionar,
    orden: 16,
    grupo: 'organizacion',
    displayMode: 'custom',
  },
  {
    id: 'admin-empleados',
    modulo: 'admin',
    titulo: 'Empleados',
    descripcion:
      'Master de personas: solicitantes de viáticos, jefe directo (autorizador N1), responsables de comprobaciones.',
    icon: Users,
    href: '/admin/empleados',
    permisoRequerido: PermisosCanonicos.AdminEmpleadosGestionar,
    orden: 17,
    grupo: 'organizacion',
    displayMode: 'custom',
  },
  {
    id: 'admin-series',
    modulo: 'admin',
    titulo: 'Series y folios',
    descripcion:
      'Configuración de series y reinicio de folios por empresa y tipo de documento.',
    icon: Hash,
    href: '/admin/series',
    permisoRequerido: PermisosCanonicos.AdminSeriesGestionar,
    orden: 20,
    grupo: 'organizacion',
    displayMode: 'custom',
  },
  {
    id: 'admin-parametros',
    modulo: 'admin',
    titulo: 'Parámetros del sistema',
    descripcion:
      'Configuración global del ERP — valores tipados (texto, número, booleano, JSON).',
    icon: Settings2,
    href: '/admin/parametros',
    permisoRequerido: PermisosCanonicos.AdminParametrosLeer,
    orden: 30,
    grupo: 'organizacion',
    displayMode: 'custom',
  },
  {
    id: 'admin-auditoria',
    modulo: 'admin',
    titulo: 'Auditoría',
    descripcion:
      'Historial consolidado de cambios; rango obligatorio (máx. 90 días).',
    icon: ClipboardList,
    href: '/admin/auditoria',
    permisoRequerido: PermisosCanonicos.AdminAuditoriaLeer,
    orden: 40,
    grupo: 'organizacion',
    displayMode: 'custom',
  },
];
