import {
  CalendarClock,
  Coins,
  Container,
  CreditCard,
  FileText,
  Landmark,
  Percent,
  Ruler,
  Shapes,
  Ship,
  Tags,
} from 'lucide-react';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import type { AdminSection } from '@/lib/admin/registry';

/**
 * Cards del módulo Catálogos en el área <c>/admin</c> (UF-Admin-PR5).
 * Publica las 10 cards del bundle:
 *
 * <list>
 *   <item><b>Grupo 1</b> — Monedas (con detalle master-detail).</item>
 *   <item><b>Grupo 2</b> — Editables: CondicionesPago, Incoterms,
 *   Transportistas, UsosPrincipales.</item>
 *   <item><b>Grupo 3</b> — SAT read-only: FormasPago, UsosCfdi,
 *   RegimenesFiscales.</item>
 * </list>
 *
 * <para>Todas las cards llevan <c>grupo: 'catalogos'</c>,
 * <c>displayMode: 'custom'</c> y un <c>permisoRequerido</c> granular.
 * Los SAT read-only usan <c>compartido.catalogos.leer</c> para
 * visibilidad.</para>
 */
export const catalogosAdminCards: readonly AdminSection[] = [
  // ─── Grupo 1: Monedas + TiposCambio ─────────────────────────────
  {
    id: 'catalogos-monedas',
    modulo: 'catalogos',
    titulo: 'Monedas y tipos de cambio',
    descripcion: 'Monedas y tipo de cambio de cada día',
    icon: Coins,
    href: '/admin/catalogos/monedas',
    permisoRequerido: PermisosCanonicos.CatalogosMonedasGestionar,
    orden: 30,
    grupo: 'catalogos',
    displayMode: 'custom',
  },

  // ─── Grupo 2: Editables sin detalle ─────────────────────────────
  {
    id: 'catalogos-condiciones-pago',
    modulo: 'catalogos',
    titulo: 'Condiciones de pago',
    descripcion: 'Plazos de pago a proveedores: contado, 30, 60 días…',
    icon: CalendarClock,
    href: '/admin/catalogos/condiciones-pago',
    permisoRequerido: PermisosCanonicos.CatalogosCondicionesPagoGestionar,
    orden: 31,
    grupo: 'catalogos',
    displayMode: 'custom',
  },
  {
    id: 'catalogos-incoterms',
    modulo: 'catalogos',
    titulo: 'Incoterms',
    descripcion: 'Términos de comercio exterior (EXW, FOB, DDP…)',
    icon: Ship,
    href: '/admin/catalogos/incoterms',
    permisoRequerido: PermisosCanonicos.CatalogosIncotermsGestionar,
    orden: 32,
    grupo: 'catalogos',
    displayMode: 'custom',
  },
  {
    id: 'catalogos-transportistas',
    modulo: 'catalogos',
    titulo: 'Transportistas',
    descripcion: 'Fleteras para envíos y logística',
    icon: Container,
    href: '/admin/catalogos/transportistas',
    permisoRequerido: PermisosCanonicos.CatalogosTransportistasGestionar,
    orden: 33,
    grupo: 'catalogos',
    displayMode: 'custom',
  },
  {
    id: 'catalogos-usos-principales',
    modulo: 'catalogos',
    titulo: 'Usos principales',
    descripcion: 'Para qué se usa cada artículo: mantenimiento, oficina…',
    icon: Tags,
    href: '/admin/catalogos/usos-principales',
    // El backend reusa el grueso para mutación; aquí también, hasta que
    // se agregue el granular.
    permisoRequerido: PermisosCanonicos.CompartidoCatalogosAdministrar,
    orden: 34,
    grupo: 'catalogos',
    displayMode: 'custom',
  },
  {
    id: 'catalogos-unidades-medida',
    modulo: 'catalogos',
    titulo: 'Unidades de medida',
    descripcion: 'Pieza, kilo, litro y cómo se convierten',
    icon: Ruler,
    href: '/admin/catalogos/unidades-medida',
    permisoRequerido: PermisosCanonicos.CatalogosUnidadesMedidaGestionar,
    orden: 38,
    grupo: 'catalogos',
    displayMode: 'custom',
  },
  {
    id: 'catalogos-categorias-articulo',
    modulo: 'catalogos',
    titulo: 'Categorías de artículo',
    descripcion: 'Grupos en los que se clasifican los artículos',
    icon: Shapes,
    href: '/admin/catalogos/categorias-articulo',
    // El backend reusa el grueso para mutación (molde UsoPrincipal).
    permisoRequerido: PermisosCanonicos.CompartidoCatalogosAdministrar,
    orden: 39,
    grupo: 'catalogos',
    displayMode: 'custom',
  },

  // ─── Grupo 3: SAT read-only ─────────────────────────────────────
  {
    id: 'catalogos-formas-pago',
    modulo: 'catalogos',
    titulo: 'Formas de pago (SAT)',
    descripcion: 'Catálogo del SAT · solo consulta',
    icon: CreditCard,
    href: '/admin/catalogos/formas-pago',
    permisoRequerido: PermisosCanonicos.CompartidoCatalogosLeer,
    orden: 35,
    grupo: 'catalogos',
    displayMode: 'custom',
  },
  {
    id: 'catalogos-impuestos',
    modulo: 'catalogos',
    titulo: 'Impuestos',
    descripcion: 'Tasas e impuestos vigentes',
    icon: Percent,
    href: '/admin/catalogos/impuestos',
    permisoRequerido: PermisosCanonicos.CompartidoCatalogosLeer,
    orden: 35.5,
    grupo: 'catalogos',
    displayMode: 'custom',
  },
  {
    id: 'catalogos-usos-cfdi',
    modulo: 'catalogos',
    titulo: 'Usos CFDI (SAT)',
    descripcion: 'Catálogo del SAT · solo consulta',
    icon: FileText,
    href: '/admin/catalogos/usos-cfdi',
    permisoRequerido: PermisosCanonicos.CompartidoCatalogosLeer,
    orden: 36,
    grupo: 'catalogos',
    displayMode: 'custom',
  },
  {
    id: 'catalogos-regimenes-fiscales',
    modulo: 'catalogos',
    titulo: 'Regímenes fiscales (SAT)',
    descripcion: 'Catálogo del SAT · solo consulta',
    icon: Landmark,
    href: '/admin/catalogos/regimenes-fiscales',
    permisoRequerido: PermisosCanonicos.CompartidoCatalogosLeer,
    orden: 37,
    grupo: 'catalogos',
    displayMode: 'custom',
  },
];
