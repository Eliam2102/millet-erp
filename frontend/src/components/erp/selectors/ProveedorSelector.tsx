import type { ComponentProps } from 'react';
import { useState } from 'react';
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
import { useProveedor, useProveedores } from '@/features/catalogos/api';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import type { ProveedorListItem } from '@/features/catalogos/api/types';
import { cn } from '@/lib/utils';
import { CatalogoQueryError } from './CatalogoQueryError';

/**
 * <c>&lt;ProveedorSelector/&gt;</c> — combobox lazy contra
 * <c>GET /api/v1/catalogos/proveedores?...&estatus=Activo</c>.
 * Doc 05 §11.3. Análogo a <c>&lt;ArticuloSelector/&gt;</c>: el item
 * muestra clave + razón social + nombre comercial (si difiere) + RFC.
 *
 * <para>UNA sola caja de búsqueda dentro del Popover: coincidencia parcial
 * en clave, razón social o nombre comercial (insensible a acentos y
 * mayúsculas), con debounce 300 ms y param <c>q</c>.</para>
 *
 * <para>Filtra <c>estatus=Activo</c> automáticamente.</para>
 */
export interface ProveedorSelectorProps {
  value: string | null | undefined;
  onChange: (id: string | null) => void;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
  /** Props extra del trigger (id y aria-* que entrega `Field`). */
  triggerProps?: ComponentProps<'button'>;
  /**
   * Etiqueta inicial ("clave · razón social") del proveedor pre-seleccionado
   * al EDITAR, cuando <c>value</c> viene del DTO enriquecido. Permite mostrar
   * la etiqueta sin depender de la lista capada del typeahead (ADR-0042
   * addendum). En alta no se pasa.
   */
  initialLabel?: string;
}

export function ProveedorSelector({
  value,
  onChange,
  placeholder = 'Buscar proveedor…',
  disabled,
  className,
  triggerProps,
  initialLabel,
}: ProveedorSelectorProps) {
  const [open, setOpen] = useState(false);
  const [input, setInput] = useState('');
  // Proveedor elegido en esta sesión; se conserva para que el trigger muestre
  // clave·razónSocial aunque la lista capada se recargue tras seleccionar.
  const [selected, setSelected] = useState<ProveedorListItem | null>(null);

  const debInput = useDebouncedValue(input, 300);
  const terminoActivo = debInput.trim() || undefined;

  const proveedoresQuery = useProveedores({ q: terminoActivo, limit: 50 });
  const items = proveedoresQuery.data?.items ?? [];

  // Etiqueta del valor seleccionado, en orden de robustez: objeto guardado →
  // página actual del typeahead → lookup por id → etiqueta inicial del DTO →
  // fallback al id.
  const resuelto =
    selected && selected.id === value
      ? selected
      : value
        ? items.find((p) => p.id === value)
        : undefined;
  // Valor "frío": hay value pero no lo resolvimos por selected ni por la página
  // actual, y el caller no pasó initialLabel (típico: recargar con
  // ?proveedorId= en la URL). Lo resolvemos con un lookup puntual por id
  // (cacheado); enabled solo cuando hace falta — sin sobre-fetchear en alta ni
  // en edición con etiqueta. Aditivo: no cambia el resto de los consumidores.
  const necesitaLookup = !!value && !resuelto && !initialLabel;
  const lookup = useProveedor(necesitaLookup ? value : null);

  const triggerLabel = resuelto
    ? `${resuelto.clave} · ${resuelto.razonSocial}`
    : lookup.data
      ? `${lookup.data.clave} · ${lookup.data.razonSocial}`
      : value
        ? initialLabel ?? value
        : placeholder;

  function handleSelect(item: ProveedorListItem) {
    const esDeseleccion = item.id === value;
    onChange(esDeseleccion ? null : item.id);
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
          aria-label="Seleccionar proveedor"
          {...triggerProps}
          disabled={disabled}
          className={cn(
            'w-full justify-between font-normal',
            !value && 'text-muted-foreground',
            className,
          )}
        >
          <span className="truncate">{triggerLabel}</span>
          <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
        </Button>
      </PopoverTrigger>
      <PopoverContent align="start" className="w-[min(40rem,90vw)] p-0">
        <Command shouldFilter={false}>
          <div className="flex items-center border-b px-3">
            <Search className="mr-2 h-4 w-4 shrink-0 opacity-50" />
            <input
              type="text"
              aria-label="Buscar proveedor"
              placeholder="Buscar por clave o nombre…"
              value={input}
              onChange={(e) => setInput(e.target.value)}
              disabled={disabled}
              className="flex h-11 w-full rounded-md bg-transparent py-3 text-sm outline-none placeholder:text-muted-foreground disabled:cursor-not-allowed disabled:opacity-50"
            />
          </div>
          <CommandList>
            {proveedoresQuery.isLoading || proveedoresQuery.isFetching ? (
              <div
                className="flex items-center justify-center gap-2 py-6 text-sm text-muted-foreground"
                aria-live="polite"
              >
                <Loader2 className="h-4 w-4 animate-spin" />
                Buscando…
              </div>
            ) : proveedoresQuery.isError ? (
              <CatalogoQueryError error={proveedoresQuery.error} />
            ) : (
              <>
                <CommandEmpty>
                  {terminoActivo
                    ? `Ningún proveedor activo coincide con "${terminoActivo}".`
                    : 'Empieza a escribir la clave o el nombre del proveedor.'}
                </CommandEmpty>
                <CommandGroup>
                  {items.map((p) => (
                    <CommandItem
                      key={p.id}
                      value={p.id}
                      onSelect={() => handleSelect(p)}
                      className="flex items-start justify-between gap-3"
                    >
                      <div className="min-w-0 flex-1">
                        <div className="flex items-center gap-2">
                          <span className="truncate font-mono text-xs">
                            {p.clave}
                          </span>
                          <span className="text-xs text-muted-foreground">
                            {p.rfc}
                          </span>
                        </div>
                        <p className="truncate text-sm">{p.razonSocial}</p>
                        {p.nombreComercial &&
                          p.nombreComercial !== p.razonSocial && (
                            <p className="truncate text-xs text-muted-foreground">
                              {p.nombreComercial}
                            </p>
                          )}
                      </div>
                      {p.id === value && (
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
