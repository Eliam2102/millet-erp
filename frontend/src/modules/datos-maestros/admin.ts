import { Boxes, Package, Truck, Users } from 'lucide-react';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import type { AdminSection } from '@/lib/admin/registry';

/**
 * Cards del módulo Datos Maestros en el área <c>/admin</c>
 * (UF-Admin-PR4.5 + ADR-0048). Publica las cards de catálogos
 * cross-empresa (Proveedores + Artículos) y de los masters
 * auto-provisionables de la ingesta A+W (Clientes + Productos A+W),
 * que linkean a los master-detail dedicados bajo
 * <c>/admin/datos-maestros/...</c>.
 *
 * <para>El permiso de visibilidad de cada card es el granular de
 * Datos Maestros (<c>datos_maestros.*.gestionar</c>). Para
 * Proveedores/Artículos el permiso de mutación dentro de las pantallas
 * sigue siendo el grueso <c>compartido.catalogos.administrar</c>;
 * para Clientes/Productos A+W la mutación usa el mismo granular
 * (así lo exige el backend, ADR-0048).</para>
 */
export const datosMaestrosAdminCards: readonly AdminSection[] = [
  {
    id: 'datos-maestros-proveedores',
    modulo: 'datos_maestros',
    titulo: 'Proveedores',
    descripcion:
      'Catálogo maestro de proveedores compartido entre módulos.',
    icon: Truck,
    href: '/admin/datos-maestros/proveedores',
    permisoRequerido: PermisosCanonicos.DatosMaestrosProveedoresGestionar,
    orden: 20,
    grupo: 'datos_maestros',
    displayMode: 'custom',
  },
  {
    id: 'datos-maestros-articulos',
    modulo: 'datos_maestros',
    titulo: 'Artículos',
    descripcion: 'Catálogo maestro de artículos / SKUs / servicios.',
    icon: Package,
    href: '/admin/datos-maestros/articulos',
    permisoRequerido: PermisosCanonicos.DatosMaestrosArticulosGestionar,
    orden: 21,
    grupo: 'datos_maestros',
    displayMode: 'custom',
  },
  {
    id: 'datos-maestros-clientes',
    modulo: 'datos_maestros',
    titulo: 'Clientes',
    descripcion:
      'Master de clientes de facturación; completa los datos fiscales de los auto-provisionados por A+W.',
    icon: Users,
    href: '/admin/datos-maestros/clientes',
    permisoRequerido: PermisosCanonicos.DatosMaestrosClientesGestionar,
    orden: 22,
    grupo: 'datos_maestros',
    displayMode: 'custom',
  },
  {
    id: 'datos-maestros-productos-aw',
    modulo: 'datos_maestros',
    titulo: 'Productos A+W',
    descripcion:
      'Master de productos de venta A+W; completa las claves SAT antes de timbrar.',
    icon: Boxes,
    href: '/admin/datos-maestros/productos-aw',
    permisoRequerido: PermisosCanonicos.DatosMaestrosProductosAwGestionar,
    orden: 23,
    grupo: 'datos_maestros',
    displayMode: 'custom',
  },
];
