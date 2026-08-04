import { ArrowDown, ArrowUp, ArrowUpDown } from 'lucide-react';
import type { SortState } from '@/components/erp/display/sort';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;SortableHeader/&gt;</c> — encabezado de columna clickable que
 * cicla entre <c>asc</c> → <c>desc</c> → <c>none</c>. Diseño polish
 * (design/frontend-polish): permite ordenar tablas client-side sin
 * acoplar a un sort backend.
 *
 * <para>El caller maneja el state (típicamente <c>{ key, dir }</c> en
 * search params o useState) y aplica el sort al array de items antes
 * de renderizar via <see cref="compareItemsBy"/>. Este componente es
 * solo el chrome del header.</para>
 *
 * <para><b>Accesibilidad</b>: <c>&lt;button&gt;</c> dentro del
 * <c>&lt;th&gt;</c>, <c>aria-sort</c> con valor estándar
 * (<c>"ascending"</c> / <c>"descending"</c> / <c>"none"</c>), icono
 * con <c>aria-hidden</c>.</para>
 */
export interface SortableHeaderProps<K extends string> {
  /** Identificador estable de la columna. */
  columnKey: K;
  /** Label visible. */
  label: string;
  /** Estado actual del sort, o <c>null</c> si ninguna columna está
   * ordenada. */
  current: SortState<K> | null;
  /** Callback con el siguiente estado (cicla asc/desc/null). */
  onSortChange: (next: SortState<K> | null) => void;
  className?: string;
}

export function SortableHeader<K extends string>({
  columnKey,
  label,
  current,
  onSortChange,
  className,
}: SortableHeaderProps<K>) {
  const active = current?.key === columnKey;
  const dir = active ? current.dir : null;

  function handleClick() {
    if (!active) {
      onSortChange({ key: columnKey, dir: 'asc' });
      return;
    }
    if (dir === 'asc') {
      onSortChange({ key: columnKey, dir: 'desc' });
      return;
    }
    onSortChange(null);
  }

  // Nota a11y: `aria-sort` es un atributo de `<th>`, no de `<button>`
  // (axe-core lo flagea como aria-allowed-attr critical). El estado del
  // sort se comunica al lector vía el `aria-label` dinámico del botón;
  // si un consumidor quiere anunciarlo como tabla ordenable, debe
  // poner `aria-sort` en el `<th>` que envuelve este componente.
  const ariaLabel = active
    ? dir === 'asc'
      ? `Ordenar ${label} descendente (actualmente ascendente)`
      : `Quitar orden de ${label} (actualmente descendente)`
    : `Ordenar por ${label}`;

  const Icon = !active ? ArrowUpDown : dir === 'asc' ? ArrowUp : ArrowDown;

  return (
    <button
      type="button"
      onClick={handleClick}
      aria-label={ariaLabel}
      className={cn(
        'inline-flex items-center gap-1 text-left font-medium transition-colors hover:text-foreground',
        active ? 'text-foreground' : 'text-muted-foreground',
        className,
      )}
    >
      <span>{label}</span>
      <Icon className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
    </button>
  );
}
