import { useMemo, useState, type ReactNode } from 'react';
import { Check, ChevronsUpDown, Loader2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from '@/components/ui/command';
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '@/components/ui/popover';
import { cn } from '@/lib/utils';
import { CatalogoQueryError } from './CatalogoQueryError';

/**
 * <c>&lt;CatalogoEagerCombobox/&gt;</c> — base reutilizable para los
 * selectores de catálogos chicos (≤200 items, sin búsqueda lazy):
 * Departamento, Sucursal, Almacén, Usuario. cmdk filtra client-side
 * sobre los items pre-cargados.
 *
 * <para>API genérica: el caller pasa la lista (vía hook), el
 * <c>itemToLabel</c> que mapea item → string searchable, y
 * <c>renderItem</c> que controla el JSX de cada fila. Esto evita
 * duplicar el patrón Popover + Command + estados loading/empty en
 * cada selector.</para>
 */
export interface CatalogoEagerComboboxProps<TItem extends { id: string }> {
  /** Items del catálogo. Pasamos la query result; el componente decide. */
  items: TItem[];
  /** <c>true</c> mientras la query está cargando (renderiza spinner). */
  loading?: boolean;
  /**
   * <c>query.error</c> del caller. Sin esto, un fallo de la query
   * (típicamente 403 por permiso faltante) se veía como "Catálogo
   * vacío" y nadie sospechaba del permiso.
   */
  error?: unknown;
  /** Id del seleccionado (controlled). */
  value: string | null | undefined;
  onChange: (id: string | null) => void;
  /**
   * Convierte un item a string searchable (cmdk filtra contra esto).
   * Suele ser <c>`${item.clave} ${item.nombre}`</c>.
   */
  itemToLabel: (item: TItem) => string;
  /** Render del item dentro del popover. */
  renderItem: (item: TItem, isSelected: boolean) => ReactNode;
  /**
   * Render compacto del item seleccionado en el trigger button.
   * Default: usa <c>itemToLabel</c>.
   */
  renderTrigger?: (item: TItem) => ReactNode;
  /**
   * Etiqueta para el trigger cuando el <c>value</c> aún no está en la lista
   * (cold value con id GUID mientras carga el catálogo, o un id fuera del
   * conjunto). Sin esto el trigger cae al value crudo (GUID). Patrón
   * <c>ProveedorSelector</c>.
   */
  initialLabel?: string | null;
  /** Texto cuando no hay selección. */
  placeholder?: string;
  /** Texto del input de búsqueda. */
  searchPlaceholder?: string;
  /** Texto cuando ningún item matchea. */
  emptyText?: string;
  /** Texto cuando la lista está vacía sin búsqueda (catálogo no poblado). */
  emptyListText?: string;
  /** Aria-label accesible del trigger. */
  ariaLabel: string;
  disabled?: boolean;
  className?: string;
  /** Ancho del popover. Default <c>w-[min(28rem,90vw)]</c>. */
  popoverWidthClassName?: string;
}

export function CatalogoEagerCombobox<TItem extends { id: string }>({
  items,
  loading = false,
  error,
  value,
  onChange,
  itemToLabel,
  renderItem,
  renderTrigger,
  initialLabel,
  placeholder = 'Selecciona…',
  searchPlaceholder = 'Buscar…',
  emptyText = 'Sin resultados.',
  emptyListText = 'Catálogo vacío.',
  ariaLabel,
  disabled,
  className,
  popoverWidthClassName = 'w-[min(28rem,90vw)]',
}: CatalogoEagerComboboxProps<TItem>) {
  const [open, setOpen] = useState(false);

  const seleccionado = useMemo(
    () => (value != null ? items.find((i) => i.id === value) : undefined),
    [items, value],
  );

  function handleSelect(item: TItem) {
    onChange(item.id === value ? null : item.id);
    setOpen(false);
  }

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          type="button"
          variant="outline"
          role="combobox"
          aria-expanded={open}
          aria-label={ariaLabel}
          disabled={disabled}
          className={cn(
            'w-full justify-between font-normal',
            !seleccionado && 'text-muted-foreground',
            className,
          )}
        >
          <span className="truncate">
            {seleccionado != null
              ? renderTrigger?.(seleccionado) ?? itemToLabel(seleccionado)
              : (initialLabel ?? value ?? placeholder)}
          </span>
          <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
        </Button>
      </PopoverTrigger>
      <PopoverContent
        align="start"
        className={cn('p-0', popoverWidthClassName)}
      >
        <Command>
          <CommandInput placeholder={searchPlaceholder} />
          <CommandList>
            {loading ? (
              <div className="flex items-center justify-center gap-2 py-6 text-sm text-muted-foreground">
                <Loader2 className="h-4 w-4 animate-spin" />
                Cargando…
              </div>
            ) : error != null ? (
              <CatalogoQueryError error={error} />
            ) : (
              <>
                <CommandEmpty>
                  {items.length === 0 ? emptyListText : emptyText}
                </CommandEmpty>
                <CommandGroup>
                  {items.map((item) => (
                    <CommandItem
                      key={item.id}
                      // cmdk usa `value` para el filtrado interno —
                      // pasamos la label completa para que el filtro
                      // matchee tanto clave como nombre.
                      value={itemToLabel(item)}
                      onSelect={() => handleSelect(item)}
                      className="flex items-start justify-between gap-3"
                    >
                      {renderItem(item, item.id === value)}
                      {item.id === value && (
                        <Check className="h-4 w-4 shrink-0" aria-hidden="true" />
                      )}
                    </CommandItem>
                  ))}
                </CommandGroup>
              </>
            )}
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}
