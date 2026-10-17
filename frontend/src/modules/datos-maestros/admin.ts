import { Factory, FileDiff, Landmark, Layers, Package, Users } from 'lucide-react';
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
    descripcion: 'Proveedores que usan todos los módulos',
    icon: Factory,
    href: '/admin/datos-maestros/proveedores',
    permisoRequerido: PermisosCanonicos.DatosMaestrosProveedoresGestionar,
    orden: 20,
    grupo: 'datos_maestros',
    displayMode: 'custom',
  },
  {
    // 86e3pdmee: el owner pidió las cuentas de Millet en Datos maestros; viven en Tesorería.
    id: 'datos-maestros-cuentas-millet',
    modulo: 'datos_maestros',
    titulo: 'Cuentas bancarias de Millet',
    descripcion: 'Cuentas de egreso e ingreso; solo un administrador las inhabilita',
    icon: Landmark,
    href: '/tesoreria/cuentas',
    permisoRequerido: PermisosCanonicos.TesoreriaCuentasVer,
    orden: 20.5,
    grupo: 'datos_maestros',
    displayMode: 'custom',
  },
  {
    id: 'datos-maestros-articulos',
    modulo: 'datos_maestros',
    titulo: 'Artículos',
    descripcion: 'Artículos y servicios que se compran',
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
    descripcion: 'Clientes a los que se factura',
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
    descripcion: 'Productos de vidrio de A+W y su clave del SAT',
    icon: Layers,
    href: '/admin/datos-maestros/productos-aw',
    permisoRequerido: PermisosCanonicos.DatosMaestrosProductosAwGestionar,
    orden: 23,
    grupo: 'datos_maestros',
    displayMode: 'custom',
  },
  {
    // U1.8b: solo lectura de A+W; la corrección se hace en Clientes / Productos A+W.
    id: 'datos-maestros-diferencias-fiscales-aw',
    modulo: 'datos_maestros',
    titulo: 'Diferencias fiscales A+W',
    descripcion: 'Clientes y productos cuyo dato fiscal no coincide con A+W',
    icon: FileDiff,
    href: '/admin/datos-maestros/diferencias-fiscales-aw',
    permisoRequerido: PermisosCanonicos.DatosMaestrosDiferenciasFiscalesAwLeer,
    orden: 24,
    grupo: 'datos_maestros',
    displayMode: 'custom',
  },
];
