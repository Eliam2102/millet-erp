import type { ComponentProps, ReactNode } from 'react';
import { useEffect, useState } from 'react';
import { Check, ChevronsUpDown, Loader2, Search } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandItem,
  CommandList,
} from '@/components/ui/command';
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '@/components/ui/popover';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import { cn } from '@/lib/utils';
import { CatalogoQueryError } from './CatalogoQueryError';

/**
 * <c>&lt;BuscadorInteligente/&gt;</c> — combobox con UNA sola caja de
 * búsqueda server-side para catálogos grandes (86e3ufkgd). El backend
 * decide en qué campos busca (clave, nombre, RFC, razón social…) con el
 * mismo término <c>q</c>; aquí solo se captura, se espera 300 ms sin teclear
 * y se avisa al consumidor por <c>onTerminoChange</c>.
 *
 * <para>El consumidor es dueño de la query: guarda el término, llama a su
 * hook con <c>q</c> y pasa <c>items</c>, <c>cargando</c> y <c>error</c>.
 * También decide cómo se pinta cada resultado (<c>renderItem</c>) y la
 * etiqueta del trigger (<c>itemToLabel</c>).</para>
 *
 * <para>Teclado: flechas para moverse, Enter para elegir y Escape para
 * cerrar (cmdk + Popover). Para catálogos chicos precargados usa
 * <c>&lt;CatalogoEagerCombobox/&gt;</c>.</para>
 */
export interface BuscadorInteligenteProps<TItem extends { id: string }> {
  /** Id del seleccionado (controlled). */
  value: string | null | undefined;
  /** Item elegido, o <c>null</c> al deseleccionar (clic en el ya elegido). */
  onChange: (item: TItem | null) => void;
  /** Término ya con debounce y trim; <c>undefined</c> si la caja está vacía. */
  onTerminoChange: (termino: string | undefined) => void;
  /** Página actual de resultados para el término vigente. */
  items: TItem[];
  cargando?: boolean;
  /** <c>query.error</c>; sin él, un 403 se vería como "sin resultados". */
  error?: unknown;
  /** Contenido de cada fila del listado. */
  renderItem: (item: TItem) => ReactNode;
  /** Etiqueta del trigger para el item seleccionado. */
  itemToLabel: (item: TItem) => string;
  /**
   * Etiqueta para un <c>value</c> que no está en la página actual ni se
   * eligió en esta sesión (edición, lookup por id). Sin ella cae al id.
   */
  valueLabel?: string;
  placeholder: string;
  /** aria-label del trigger. */
  ariaLabel: string;
  /** aria-label de la caja de búsqueda. */
  searchAriaLabel: string;
  /** Placeholder de la caja: dice qué datos se pueden buscar. */
  searchPlaceholder: string;
  /** Texto cuando no hay término (sin búsqueda o catálogo vacío). */
  emptyText: string;
  /** Texto cuando el término no coincide con nada. */
  noMatchText: (termino: string) => string;
  disabled?: boolean;
  className?: string;
  /** Props extra del trigger (id y aria-* que entrega `Field`). */
  triggerProps?: ComponentProps<'button'>;
  /** Ancho del popover. Default <c>w-[min(36rem,90vw)]</c>. */
  popoverWidthClassName?: string;
}

export function BuscadorInteligente<TItem extends { id: string }>({
  value,
  onChange,
  onTerminoChange,
  items,
  cargando = false,
  error,
  renderItem,
  itemToLabel,
  valueLabel,
  placeholder,
  ariaLabel,
  searchAriaLabel,
  searchPlaceholder,
  emptyText,
  noMatchText,
  disabled,
  className,
  triggerProps,
  popoverWidthClassName = 'w-[min(36rem,90vw)]',
}: BuscadorInteligenteProps<TItem>) {
  const [open, setOpen] = useState(false);
  const [input, setInput] = useState('');
  // Item elegido en esta sesión: el trigger conserva su etiqueta aunque la
  // lista capada se recargue sin él (ADR-0042 addendum).
  const [selected, setSelected] = useState<TItem | null>(null);

  const termino = useDebouncedValue(input, 300).trim() || undefined;
  useEffect(() => {
    onTerminoChange(termino);
  }, [termino, onTerminoChange]);

  const resuelto =
    selected && selected.id === value
      ? selected
      : value
        ? items.find((i) => i.id === value)
        : undefined;
  const triggerLabel = resuelto
    ? itemToLabel(resuelto)
    : value
      ? (valueLabel ?? value)
      : placeholder;

  function handleSelect(item: TItem) {
    const esDeseleccion = item.id === value;
    onChange(esDeseleccion ? null : item);
    setSelected(esDeseleccion ? null : item);
    setOpen(false);
    setInput('');
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
          {...triggerProps}
          disabled={disabled}
          className={cn(
            'w-full justify-between font-normal',
            !value && 'text-ink-muted',
            className,
          )}
        >
          <span className="truncate">{triggerLabel}</span>
          <ChevronsUpDown
            className="ml-2 h-4 w-4 shrink-0 text-ink-subtle"
            aria-hidden="true"
          />
        </Button>
      </PopoverTrigger>
      <PopoverContent
        align="start"
        className={cn('p-0', popoverWidthClassName)}
      >
        <Command shouldFilter={false}>
          <div className="flex items-center border-b border-line-divider px-3">
            <Search
              className="mr-2 h-4 w-4 shrink-0 text-ink-subtle"
              aria-hidden="true"
            />
            <input
              type="text"
              aria-label={searchAriaLabel}
              placeholder={searchPlaceholder}
              value={input}
              onChange={(e) => setInput(e.target.value)}
              disabled={disabled}
              className="flex h-11 w-full rounded-md bg-transparent py-3 text-sm text-ink outline-none placeholder:text-ink-muted disabled:cursor-not-allowed disabled:opacity-50"
            />
          </div>
          <CommandList aria-busy={cargando}>
            {cargando ? (
              <div
                className="flex items-center justify-center gap-2 py-6 text-sm text-ink-muted"
                role="status"
                aria-live="polite"
              >
                <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
                Buscando…
              </div>
            ) : error != null ? (
              <CatalogoQueryError error={error} />
            ) : (
              <>
                <CommandEmpty className="py-6 text-center text-sm text-ink-muted">
                  {termino ? noMatchText(termino) : emptyText}
                </CommandEmpty>
                <CommandGroup>
                  {items.map((item) => (
                    <CommandItem
                      key={item.id}
                      value={item.id}
                      onSelect={() => handleSelect(item)}
                      className="flex items-start justify-between gap-3"
                    >
                      {renderItem(item)}
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
