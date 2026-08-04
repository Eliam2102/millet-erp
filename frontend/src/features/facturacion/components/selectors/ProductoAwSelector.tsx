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
import { useProductosAwLookup } from '@/features/facturacion/api/useCatalogosFacturacion';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import type { ProductoAwLookupItem } from '@/features/facturacion/api/types';
import { cn } from '@/lib/utils';
import { CatalogoQueryError } from '@/components/erp/selectors/CatalogoQueryError';

/**
 * <c>&lt;ProductoAwSelector/&gt;</c> — combobox lazy contra
 * <c>GET /api/v1/facturacion/catalogos/productos-aw</c> (FAC-UX-PR4,
 * cierra PLATFORM-TODO(&lt;ProductoSelector&gt;)). Patrón
 * <c>ArticuloSelector</c> (ADR-0045): búsqueda por referencia A+W o por
 * descripción (excluyentes, debounce 300 ms; referencia con precedencia).
 *
 * <para><c>onChange</c> entrega el ITEM COMPLETO para autollenar el
 * concepto (descripción, claves SAT, objetoImp, tasas).</para>
 */
export interface ProductoAwSelectorProps {
  value: string | null | undefined;
  onChange: (item: ProductoAwLookupItem | null) => void;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
  initialLabel?: string;
}

export function ProductoAwSelector({
  value,
  onChange,
  placeholder = 'Buscar producto…',
  disabled,
  className,
  initialLabel,
}: ProductoAwSelectorProps) {
  const [open, setOpen] = useState(false);
  const [inputRef, setInputRef] = useState('');
  const [inputDesc, setInputDesc] = useState('');
  const [selected, setSelected] = useState<ProductoAwLookupItem | null>(null);

  const debRef = useDebouncedValue(inputRef, 300);
  const debDesc = useDebouncedValue(inputDesc, 300);

  const refQuery = debRef.trim() || undefined;
  const descQuery = !refQuery ? debDesc.trim() || undefined : undefined;
  const terminoActivo = refQuery ?? descQuery;

  const modoRef = inputRef.trim().length > 0;
  const modoDesc = inputDesc.trim().length > 0;

  const productosQuery = useProductosAwLookup(
    { referencia: refQuery, descripcion: descQuery, limit: 20 },
    { enabled: open },
  );
  const items = productosQuery.data ?? [];

  const resuelto =
    selected && selected.id === value
      ? selected
      : value
        ? items.find((p) => p.id === value)
        : undefined;

  const triggerLabel = resuelto
    ? `${resuelto.referenciaExterna} · ${resuelto.descripcion}`
    : value
      ? initialLabel ?? 'Producto seleccionado'
      : placeholder;

  function handleRefChange(v: string) {
    setInputRef(v);
    if (v) setInputDesc('');
  }

  function handleDescChange(v: string) {
    setInputDesc(v);
    if (v) setInputRef('');
  }

  function handleSelect(item: ProductoAwLookupItem) {
    const esDeseleccion = item.id === value;
    onChange(esDeseleccion ? null : item);
    setSelected(esDeseleccion ? null : item);
    setOpen(false);
    setInputRef('');
    setInputDesc('');
  }

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          type="button"
          variant="outline"
          role="combobox"
          aria-expanded={open}
          aria-label="Seleccionar producto A+W"
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
              aria-label="Buscar producto por referencia"
              placeholder="Buscar por referencia A+W…"
              value={inputRef}
              onChange={(e) => handleRefChange(e.target.value)}
              disabled={disabled || modoDesc}
              className="flex h-11 w-full rounded-md bg-transparent py-3 text-sm outline-none placeholder:text-muted-foreground disabled:cursor-not-allowed disabled:opacity-50"
            />
          </div>
          <div className="flex items-center border-b px-3">
            <Search className="mr-2 h-4 w-4 shrink-0 opacity-50" />
            <input
              type="text"
              aria-label="Buscar producto por descripción"
              placeholder="Buscar por descripción…"
              value={inputDesc}
              onChange={(e) => handleDescChange(e.target.value)}
              disabled={disabled || modoRef}
              className="flex h-11 w-full rounded-md bg-transparent py-3 text-sm outline-none placeholder:text-muted-foreground disabled:cursor-not-allowed disabled:opacity-50"
            />
          </div>
          <CommandList>
            {productosQuery.isLoading || productosQuery.isFetching ? (
              <div
                className="flex items-center justify-center gap-2 py-6 text-sm text-muted-foreground"
                aria-live="polite"
              >
                <Loader2 className="h-4 w-4 animate-spin" />
                Buscando…
              </div>
            ) : productosQuery.isError ? (
              <CatalogoQueryError error={productosQuery.error} />
            ) : (
              <>
                <CommandEmpty>
                  {terminoActivo
                    ? `Ningún producto activo coincide con "${terminoActivo}".`
                    : 'Empieza a escribir la referencia o la descripción.'}
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
                            {p.referenciaExterna}
                          </span>
                          <span className="text-xs text-muted-foreground">
                            {p.claveProdServSat ?? 'Sin clave SAT'} ·{' '}
                            {p.unidadMedida}
                          </span>
                          {!p.datosFiscalesCompletos && (
                            <span className="rounded-full bg-amber-100 px-1.5 py-0.5 text-[10px] font-medium text-amber-800">
                              Fiscales incompletos
                            </span>
                          )}
                        </div>
                        <p className="truncate text-sm">{p.descripcion}</p>
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
