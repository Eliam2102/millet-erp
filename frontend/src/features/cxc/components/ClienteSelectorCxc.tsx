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
import { useClientesLookupCxc } from '@/features/cxc/api/useLineasCredito';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import type { ClienteLookupCxcItem } from '@/features/cxc/api/types';
import { cn } from '@/lib/utils';
import { CatalogoQueryError } from '@/components/erp/selectors/CatalogoQueryError';

/**
 * <c>&lt;ClienteSelectorCxc/&gt;</c> — combobox lazy contra
 * <c>GET /api/v1/cuentas-por-cobrar/clientes-lookup</c> (CXC-FE-PR2).
 * Réplica del patrón <c>ClienteSelector</c> de Facturación (ADR-0045):
 * DOS cajas de búsqueda EXCLUYENTES — RFC y razón social (insensible a
 * acentos) — con debounce 300 ms; RFC tiene precedencia. Gate del
 * endpoint: <c>lineas-credito.leer</c>.
 *
 * <para><c>onChange</c> entrega el ITEM COMPLETO (no solo el id) para
 * que el caller muestre razón social/RFC sin otra query.</para>
 */
export interface ClienteSelectorCxcProps {
  value: string | null | undefined;
  onChange: (item: ClienteLookupCxcItem | null) => void;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
  /** Etiqueta para value "frío" (id sin item conocido). Evita mostrar el
   * GUID crudo en el trigger. */
  initialLabel?: string;
}

export function ClienteSelectorCxc({
  value,
  onChange,
  placeholder = 'Buscar cliente…',
  disabled,
  className,
  initialLabel,
}: ClienteSelectorCxcProps) {
  const [open, setOpen] = useState(false);
  const [inputRfc, setInputRfc] = useState('');
  const [inputNombre, setInputNombre] = useState('');
  const [selected, setSelected] = useState<ClienteLookupCxcItem | null>(null);

  const debRfc = useDebouncedValue(inputRfc, 300);
  const debNombre = useDebouncedValue(inputNombre, 300);

  // Exclusividad ADR-0045: RFC tiene precedencia; solo uno viaja al backend.
  const rfcQuery = debRfc.trim() || undefined;
  const nombreQuery = !rfcQuery ? debNombre.trim() || undefined : undefined;
  const terminoActivo = rfcQuery ?? nombreQuery;

  const modoRfc = inputRfc.trim().length > 0;
  const modoNombre = inputNombre.trim().length > 0;

  const clientesQuery = useClientesLookupCxc(
    { rfc: rfcQuery, razonSocial: nombreQuery, limit: 20 },
    { enabled: open },
  );
  const items = clientesQuery.data ?? [];

  const resuelto =
    selected && selected.id === value
      ? selected
      : value
        ? items.find((c) => c.id === value)
        : undefined;

  const triggerLabel = resuelto
    ? `${resuelto.clave} · ${resuelto.razonSocial}`
    : value
      ? initialLabel ?? 'Cliente seleccionado'
      : placeholder;

  function handleRfcChange(v: string) {
    setInputRfc(v);
    if (v) setInputNombre('');
  }

  function handleNombreChange(v: string) {
    setInputNombre(v);
    if (v) setInputRfc('');
  }

  function handleSelect(item: ClienteLookupCxcItem) {
    const esDeseleccion = item.id === value;
    onChange(esDeseleccion ? null : item);
    setSelected(esDeseleccion ? null : item);
    setOpen(false);
    setInputRfc('');
    setInputNombre('');
  }

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          type="button"
          variant="outline"
          role="combobox"
          aria-expanded={open}
          aria-label="Seleccionar cliente"
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
              aria-label="Buscar cliente por RFC"
              placeholder="Buscar por RFC…"
              value={inputRfc}
              onChange={(e) => handleRfcChange(e.target.value)}
              disabled={disabled || modoNombre}
              className="flex h-11 w-full rounded-md bg-transparent py-3 text-sm outline-none placeholder:text-muted-foreground disabled:cursor-not-allowed disabled:opacity-50"
            />
          </div>
          <div className="flex items-center border-b px-3">
            <Search className="mr-2 h-4 w-4 shrink-0 opacity-50" />
            <input
              type="text"
              aria-label="Buscar cliente por razón social"
              placeholder="Buscar por razón social…"
              value={inputNombre}
              onChange={(e) => handleNombreChange(e.target.value)}
              disabled={disabled || modoRfc}
              className="flex h-11 w-full rounded-md bg-transparent py-3 text-sm outline-none placeholder:text-muted-foreground disabled:cursor-not-allowed disabled:opacity-50"
            />
          </div>
          <CommandList>
            {clientesQuery.isLoading || clientesQuery.isFetching ? (
              <div
                className="flex items-center justify-center gap-2 py-6 text-sm text-muted-foreground"
                aria-live="polite"
              >
                <Loader2 className="h-4 w-4 animate-spin" />
                Buscando…
              </div>
            ) : clientesQuery.isError ? (
              <CatalogoQueryError error={clientesQuery.error} />
            ) : (
              <>
                <CommandEmpty>
                  {terminoActivo
                    ? `Ningún cliente activo coincide con "${terminoActivo}".`
                    : 'Empieza a escribir el RFC o la razón social.'}
                </CommandEmpty>
                <CommandGroup>
                  {items.map((c) => (
                    <CommandItem
                      key={c.id}
                      value={c.id}
                      onSelect={() => handleSelect(c)}
                      className="flex items-start justify-between gap-3"
                    >
                      <div className="min-w-0 flex-1">
                        <div className="flex items-center gap-2">
                          <span className="truncate font-mono text-xs">
                            {c.clave}
                          </span>
                          <span className="text-xs text-muted-foreground">
                            {c.rfc ?? 'Sin RFC'}
                          </span>
                        </div>
                        <p className="truncate text-sm">{c.razonSocial}</p>
                      </div>
                      {c.id === value && (
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
