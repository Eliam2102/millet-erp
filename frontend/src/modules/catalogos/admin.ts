import {
  Boxes,
  Building,
  Coins,
  FileText,
  Receipt,
  Ruler,
  Scale,
  Tag,
  Truck,
  Wallet,
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
    descripcion:
      'Catálogo cross-empresa de monedas e histórico de tipos de cambio.',
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
    descripcion: 'Esquemas de crédito a proveedores (CONTADO, 30D, 60D, …).',
    icon: Wallet,
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
    descripcion: 'Términos de comercio internacional (EXW, FOB, DDP, …).',
    icon: Building,
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
    descripcion: 'Catálogo de transportistas para fletes y logística.',
    icon: Truck,
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
    descripcion:
      'Categorías de uso para artículos no-producción (mantenimiento, oficina, …).',
    icon: Tag,
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
    descripcion:
      'Catálogo de unidades (pieza, kg, litro, …) con dimensión y factor de conversión (ADR-0046).',
    icon: Scale,
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
    descripcion:
      'Catálogo de categorías (grupos) de artículo no-producción; reemplaza el texto libre (patrón ADR-0046).',
    icon: Boxes,
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
    descripcion: 'Catálogo SAT c_FormaPago (read-only).',
    icon: Receipt,
    href: '/admin/catalogos/formas-pago',
    permisoRequerido: PermisosCanonicos.CompartidoCatalogosLeer,
    orden: 35,
    grupo: 'catalogos',
    displayMode: 'custom',
  },
  {
    id: 'catalogos-usos-cfdi',
    modulo: 'catalogos',
    titulo: 'Usos CFDI (SAT)',
    descripcion: 'Catálogo SAT c_UsoCFDI (read-only).',
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
    descripcion: 'Catálogo SAT de regímenes fiscales (read-only).',
    icon: Ruler,
    href: '/admin/catalogos/regimenes-fiscales',
    permisoRequerido: PermisosCanonicos.CompartidoCatalogosLeer,
    orden: 37,
    grupo: 'catalogos',
    displayMode: 'custom',
  },
];
