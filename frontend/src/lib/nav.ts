import { Home } from 'lucide-react';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { adminRegistry, adminSettingsRegistry, adminEntryVisible } from '@/lib/admin/registry';
import { modulos, type NavModulo, type NavSidebarItem, type NavCard } from './nav-catalog';
import { cardVisible } from './nav-permissions';
export type { NavModulo, NavSidebarItem, NavCard, NavSeccion } from './nav-catalog';

/**
 * Items del sidebar — Inicio (link directo) seguido de los módulos
 * del back-office en orden de uso típico.
 */
export const navSidebarItems: readonly NavSidebarItem[] = [
  { kind: 'link', label: 'Inicio', to: '/', icon: Home },
  ...modulos.map((m): NavSidebarItem => ({ kind: 'modulo', ...m })),
];

/**
 * Helper que aplica los gates de permiso (<c>permission</c> /
 * <c>permissionsAny</c>) sobre las cards de un módulo y descarta
 * secciones que quedan vacías. Si el módulo no tiene secciones con
 * cards visibles, el caller decide qué mostrar (mensaje neutro).
 */
export function filtrarModuloPorPermisos(
  modulo: NavModulo,
  permisos: readonly string[],
): NavModulo {
  const seccionesFiltradas = modulo.secciones
    .map((seccion) => ({
      ...seccion,
      cards: seccion.cards.filter((card) => cardVisible(card, permisos)),
    }))
    .filter((seccion) => seccion.cards.length > 0);
  return { ...modulo, secciones: seccionesFiltradas };
}

// ============================================================================
// Visibilidad por permisos (sidebar y URLs)
// ============================================================================

/**
 * Items del sidebar que el usuario puede usar: Inicio más los módulos con
 * al menos una card permitida. Los módulos sin acceso y los placeholders
 * (<c>disabled</c>) no se muestran — el usuario solo ve lo que puede abrir.
 */
export function sidebarItemsVisibles(permisos: readonly string[]): readonly NavSidebarItem[] {
  return navSidebarItems.filter(
    (item) =>
      item.kind === 'link' ||
      (!item.disabled && filtrarModuloPorPermisos(item, permisos).secciones.length > 0),
  );
}

/** Contexto visual del shell, siempre limitado a las pantallas permitidas. */
export function contextoNavegacion(pathname: string, permisos: readonly string[]) {
  return sidebarItemsVisibles(permisos)
    .filter((item): item is NavSidebarItem & NavModulo => item.kind === 'modulo')
    .flatMap((item) => {
      const modulo = filtrarModuloPorPermisos(item, permisos);
      return modulo.secciones.flatMap((seccion) =>
        seccion.cards
          .filter((card) => contieneRuta(card.to, pathname))
          .map((card) => ({ modulo, seccion, card })),
      );
    })
    .sort((a, b) => b.card.to.length - a.card.to.length)[0];
}

/**
 * Rutas que no tienen card en el menú (se llega desde otra pantalla) pero
 * leen datos protegidos. Mismo permiso que exige su endpoint en el backend.
 */
const rutasFueraDelMenu: readonly Pick<NavCard, 'to' | 'permission' | 'permissionsAny'>[] = [
  { to: '/admin/mi-empresa', permission: PermisosCanonicos.AdminEmpresasLeer },
  { to: '/compras/trazabilidad', permission: PermisosCanonicos.ComprasOrdenesLeer },
  { to: '/compras/articulos', permission: PermisosCanonicos.ComprasOrdenesLeer },
];

function contieneRuta(base: string, pathname: string): boolean {
  return pathname === base || pathname.startsWith(`${base}/`);
}

/** Accesos del buscador: mismo catálogo y permisos que los menús. */
export function accesosNavegacion(permisos: readonly string[]) {
  const accesos = sidebarItemsVisibles(permisos).flatMap((item) =>
    item.kind === 'link'
      ? [{ ...item, description: 'Página de inicio', modulo: 'General' }]
      : filtrarModuloPorPermisos(item, permisos).secciones.flatMap((seccion) =>
          seccion.cards.map((card) => ({ ...card, modulo: item.label })),
        ),
  );
  accesos.push(
    ...adminRegistry
      .filter((section) => adminEntryVisible(section, permisos))
      .map((section) => ({
        to: section.href,
        label: section.titulo,
        description: section.descripcion,
        modulo: 'Administración',
        icon: section.icon,
      })),
  );
  return [
    ...new Map(
      accesos
        .filter((acceso) => rutaPermitida(acceso.to, permisos))
        .map((acceso) => [acceso.to, acceso]),
    ).values(),
  ];
}

/**
 * ¿Puede el usuario abrir esta URL? Aplica el permiso de la card más
 * específica que contiene la ruta (<c>/compras/ordenes/123</c> → card
 * <c>/compras/ordenes</c>), así el sidebar y la URL directa usan la misma
 * regla. Sin card: la raíz de un módulo (<c>/tesoreria</c>, su ayuda) exige
 * ver ese módulo. La configuración administrativa exige un permiso de
 * su módulo en el registry; Inicio queda disponible para la sesión.
 *
 * <para>Es una guarda de navegación, no de seguridad: el backend valida
 * cada endpoint.</para>
 */
export function rutaPermitida(pathname: string, permisos: readonly string[]): boolean {
  const settingsModulo = pathname.match(/^\/admin\/([^/]+)\/settings(?:\/|$)/)?.[1];
  if (settingsModulo !== undefined) {
    return adminSettingsRegistry.some(
      (section) => section.modulo === settingsModulo && adminEntryVisible(section, permisos),
    );
  }

  const candidatas = [
    ...modulos.flatMap((m) => m.secciones.flatMap((s) => s.cards)),
    ...rutasFueraDelMenu,
  ].filter((card) => contieneRuta(card.to, pathname));

  if (candidatas.length > 0) {
    const masEspecifica = candidatas.reduce((a, b) => (b.to.length > a.to.length ? b : a));
    return cardVisible(masEspecifica, permisos);
  }

  const segmento = `/${pathname.split('/')[1] ?? ''}`;
  const modulo = modulos.find((m) =>
    m.secciones.some((s) => s.cards.some((c) => contieneRuta(segmento, c.to))),
  );
  return modulo === undefined || filtrarModuloPorPermisos(modulo, permisos).secciones.length > 0;
}

export const gruposMenu = [
  { titulo: 'Abastecimiento', modulos: ['compras', 'almacen', 'cxp'] },
  { titulo: 'Ventas', modulos: ['facturacion', 'cxc'] },
  { titulo: 'Finanzas', modulos: ['tesoreria', 'contabilidad', 'centros-costo', 'reportes'] },
] as const;

/** Proyección del catálogo: operación, consultas y reportes siempre visibles. */
export function menuModulos(permisos: readonly string[], filtro = '') {
  const normalize = (value: string) =>
    value
      .normalize('NFD')
      .replace(/\p{Diacritic}/gu, '')
      .toLocaleLowerCase('es-MX');
  const query = normalize(filtro.trim());
  return sidebarItemsVisibles(permisos)
    .filter((item): item is NavSidebarItem & NavModulo => item.kind === 'modulo')
    .map((item) => {
      const permitido = filtrarModuloPorPermisos(item, permisos);
      const operacion = permitido.secciones
        .filter((section) => section.label !== 'Configuración')
        .flatMap((section) => section.cards);
      return {
        ...permitido,
        to: operacion[0]?.to ?? `/${item.moduloId}`,
        cards: operacion.filter(
          (card) => normalize(card.label).includes(query) || normalize(item.label).includes(query),
        ),
      };
    })
    .filter((item) => item.cards.length > 0 || normalize(item.label).includes(query));
}
