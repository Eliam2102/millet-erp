import type { NavCard } from './nav-catalog';

export function cardVisible(
  card: Pick<NavCard, 'permission' | 'permissionsAny'>,
  permisos: readonly string[],
): boolean {
  if (card.permission != null && !permisos.includes(card.permission)) {
    return false;
  }
  if (card.permissionsAny != null && !card.permissionsAny.some((p) => permisos.includes(p))) {
    return false;
  }
  return true;
}
