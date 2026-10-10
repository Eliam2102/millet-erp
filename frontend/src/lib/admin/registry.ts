import type { LucideIcon } from 'lucide-react';

import { administracionAdminCards } from '@/modules/administracion/admin';
import { catalogosAdminCards } from '@/modules/catalogos/admin';
import { comprasAdminCards } from '@/modules/compras/admin';
import { modulos } from '@/lib/nav-catalog';
import { datosMaestrosAdminCards } from '@/modules/datos-maestros/admin';
import { cardVisible } from '@/lib/nav-permissions';
import { identidadAdminCards } from '@/modules/identidad/admin';
import { integracionesFiscalAdminCards } from '@/features/integraciones-fiscal/admin';
import { facturacionAdminCards } from '@/features/facturacion/admin';

/** Entradas administrativas organizadas por tarea. Configuración de módulos
 * reutiliza el catálogo de nav y sus gates; sin datos maestros ni estados supuestos.
 */
/** Grupos visuales del landing <c>/admin</c>. */
export type AdminGrupo =
  | 'identidad'
  | 'organizacion'
  | 'catalogos'
  | 'datos_maestros'
  | 'modulos'
  | 'facturacion'
  | 'reglas'
  | 'bitacora';

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
  | 'integraciones_fiscal'
  | 'centros-costo';

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
  permissionsAny?: readonly string[];
  soloConsulta?: boolean;
  nombreModulo?: string;
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
export const gruposAdmin: readonly { id: AdminGrupo; titulo: string }[] = [
  { id: 'organizacion', titulo: 'Empresa y sucursales' },
  { id: 'identidad', titulo: 'Personas y accesos' },
  { id: 'facturacion', titulo: 'Facturación y folios' },
  { id: 'reglas', titulo: 'Reglas y parámetros' },
  { id: 'catalogos', titulo: 'Catálogos' },
  { id: 'modulos', titulo: 'Por módulo' },
  { id: 'bitacora', titulo: 'Bitácora de cambios' },
];

const presentacion: Record<
  string,
  { grupo: AdminGrupo; descripcion: string; orden: number; titulo?: string; soloConsulta?: boolean }
> = {
  'admin-mi-empresa': {
    grupo: 'organizacion',
    descripcion: 'Razón social, RFC, régimen y domicilio fiscal de Millet.',
    orden: 0,
  },
  'admin-sucursales': {
    grupo: 'organizacion',
    descripcion: 'Ubicaciones de Millet y sucursales que puede usar cada persona.',
    orden: 1,
  },
  'admin-departamentos': {
    grupo: 'organizacion',
    descripcion: 'Áreas de trabajo y su asignación a cada sucursal.',
    orden: 2,
  },
  'admin-puestos': {
    grupo: 'organizacion',
    descripcion: 'Puestos de trabajo que se usan en las reglas de viáticos.',
    orden: 3,
  },
  'admin-canales-venta': {
    grupo: 'organizacion',
    descripcion: 'Canales de venta para recibir y facturar los pedidos de A+W.',
    orden: 4,
  },
  'admin-empleados': {
    grupo: 'identidad',
    descripcion: 'Personas de Millet, jefes directos y responsables de gastos.',
    orden: 5,
  },
  'identidad-usuarios': {
    grupo: 'identidad',
    descripcion: 'Cuentas de acceso, empleado asociado, rol y sucursales permitidas.',
    orden: 6,
  },
  'identidad-roles': {
    grupo: 'identidad',
    descripcion: 'Qué puede ver y hacer cada rol y sus grupos de Microsoft.',
    orden: 7,
  },
  'admin-series': {
    grupo: 'facturacion',
    descripcion: 'Series y numeración de documentos por sucursal.',
    orden: 8,
  },
  'integraciones-fiscal': {
    grupo: 'facturacion',
    descripcion: 'Proveedor de timbrado y descarga de comprobantes del SAT.',
    orden: 9,
    titulo: 'Integración fiscal (PAC)',
  },
  'facturacion-carta-porte-catalogos': {
    grupo: 'facturacion',
    descripcion: 'Vehículos y choferes que se usan en Carta Porte.',
    orden: 10,
  },
  'admin-parametros': {
    grupo: 'reglas',
    descripcion: 'Reglas generales, días festivos y valores de operación del ERP.',
    orden: 11,
  },
  'catalogos-monedas': {
    grupo: 'reglas',
    descripcion: 'Monedas e historial de tipos de cambio para las operaciones.',
    orden: 12,
  },
  'catalogos-condiciones-pago': {
    grupo: 'catalogos',
    descripcion: 'Plazos de pago al contado o a crédito para proveedores.',
    orden: 13,
  },
  'catalogos-incoterms': {
    grupo: 'catalogos',
    descripcion: 'Condiciones de entrega y responsabilidades en comercio exterior.',
    orden: 14,
  },
  'catalogos-transportistas': {
    grupo: 'catalogos',
    descripcion: 'Transportistas que realizan fletes y entregas.',
    orden: 15,
  },
  'catalogos-usos-principales': {
    grupo: 'catalogos',
    descripcion: 'Uso de los artículos en mantenimiento, oficina y otras áreas.',
    orden: 16,
  },
  'catalogos-unidades-medida': {
    grupo: 'catalogos',
    descripcion: 'Unidades de compra y consumo, con sus equivalencias.',
    orden: 17,
  },
  'catalogos-categorias-articulo': {
    grupo: 'catalogos',
    descripcion: 'Grupos para organizar los artículos de compra.',
    orden: 18,
  },
  'catalogos-impuestos': {
    grupo: 'catalogos',
    descripcion: 'Impuestos aplicables y sus periodos de vigencia.',
    orden: 19,
  },
  'catalogos-formas-pago': {
    grupo: 'catalogos',
    descripcion: 'Formas de pago publicadas por el SAT.',
    orden: 20,
    soloConsulta: true,
    titulo: 'Formas de pago',
  },
  'catalogos-usos-cfdi': {
    grupo: 'catalogos',
    descripcion: 'Usos de los comprobantes fiscales publicados por el SAT.',
    orden: 21,
    soloConsulta: true,
    titulo: 'Usos CFDI',
  },
  'catalogos-regimenes-fiscales': {
    grupo: 'catalogos',
    descripcion: 'Regímenes fiscales publicados por el SAT.',
    orden: 22,
    soloConsulta: true,
    titulo: 'Regímenes fiscales',
  },
  'compras-configuracion': {
    grupo: 'modulos',
    descripcion: 'Generación de órdenes de compra y reglas de autorización.',
    orden: 23,
  },
  'admin-auditoria': {
    grupo: 'bitacora',
    descripcion: 'Quién cambió cada dato y cuándo se realizó el cambio.',
    orden: 24,
  },
};

const descripcionesModulo: Record<string, string> = {
  '/compras/admin/aprobadores': 'Personas que autorizan las compras de cada departamento.',
  '/almacen/almacenes': 'Almacenes de Millet por sucursal.',
  '/almacen/sub-almacenes': 'Almacenes de cada sucursal y sus responsables.',
  '/almacen/materiales': 'Materiales de almacén y su clasificación.',
  '/almacen/reabasto': 'Cantidades mínimas para reponer los materiales.',
  '/almacen/reorden': 'Cantidades mínimas para solicitar reposición de materiales.',
  '/almacen/asignaciones': 'Ubicación asignada a cada artículo dentro del almacén.',
  '/almacen/ubicaciones': 'Pasillos, estantes y espacios físicos del almacén.',
  '/cxp/admin/retenciones': 'Retenciones de impuestos por concepto de gasto.',
  '/cxp/admin/aprobadores': 'Personas que autorizan gastos y sus montos máximos.',
  '/cxp/admin/politicas-viaticos': 'Límites de viáticos por puesto y destino.',
  '/cxp/admin/reposiciones': 'Reglas y saldos para reponer los fondos de caja chica.',
  '/centros-costo/configuracion': 'Centros de costo y equivalencias de cada departamento.',
  '/centros-costo/asignaciones': 'Centros de costo que puede usar cada persona.',
  '/facturacion/cajas': 'Cajas por sucursal, cajeros y permisos para operar.',
};

/** Gates previos de las rutas genéricas de settings. Mover entradas del menú
 * no habilita ni retira acceso a estas pantallas existentes.
 */
export const adminSettingsRegistry: readonly AdminSection[] = [
  ...identidadAdminCards,
  ...administracionAdminCards,
  ...datosMaestrosAdminCards,
  ...catalogosAdminCards,
  ...comprasAdminCards,
  ...facturacionAdminCards,
  ...integracionesFiscalAdminCards,
];

const base = adminSettingsRegistry
  .filter((entry) => entry.grupo !== 'datos_maestros')
  .map((entry) => ({
    ...entry,
    ...presentacion[entry.id],
    ...(entry.id === 'compras-configuracion' ? { nombreModulo: 'Compras' } : {}),
  }));

const configuracion: AdminSection[] = modulos.flatMap((modulo) =>
  modulo.secciones
    .filter((section) => section.label === 'Configuración')
    .flatMap((section) =>
      section.cards.map((card, index) => ({
        id: `${modulo.moduloId}-${card.to}`,
        modulo: modulo.moduloId as AdminModulo,
        titulo: card.label,
        descripcion:
          descripcionesModulo[card.to] ??
          `Reglas de ${card.label.toLocaleLowerCase('es-MX')} para Millet.`,
        icon: card.icon,
        href: card.to,
        permisoRequerido: card.permission ?? '',
        permissionsAny: card.permissionsAny,
        orden: 100 + index,
        grupo: 'modulos' as const,
        nombreModulo: modulo.label,
        displayMode: 'custom' as const,
      })),
    ),
);

/** Mismas pantallas y gates de nav; los maestros ahora viven en sus módulos. */
export const adminRegistry: readonly AdminSection[] = [...base, ...configuracion];

export function adminEntryVisible(entry: AdminSection, permisos: readonly string[]): boolean {
  return cardVisible(
    { permission: entry.permisoRequerido || undefined, permissionsAny: entry.permissionsAny },
    permisos,
  );
}

export function filtrarConfiguraciones(
  entries: readonly AdminSection[],
  query: string,
): readonly AdminSection[] {
  const needle = normalizarNombre(query.trim());
  return entries.filter((entry) => {
    const palabras = normalizarNombre(
      `${entry.titulo} ${entry.descripcion} ${entry.nombreModulo ?? ''}`,
    ).split(/\s+/);
    return needle.split(/\s+/).every((term) => palabras.some((word) => word.startsWith(term)));
  });
}

export function normalizarNombre(value: string): string {
  return value
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .toLocaleLowerCase('es-MX');
}
