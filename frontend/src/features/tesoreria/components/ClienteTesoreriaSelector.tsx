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
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { CatalogoQueryError } from '@/components/erp/selectors/CatalogoQueryError';
import { useClientesTesoreria } from '@/features/tesoreria/api/useTesoreria';
import type { ClienteRefDto } from '@/features/tesoreria/api/types';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import { cn } from '@/lib/utils';

export interface ClienteTesoreriaSelectorProps {
  value: string | null;
  onChange: (cliente: ClienteRefDto | null) => void;
  /** Etiqueta para un cliente prellenado (id sin item conocido), evita mostrar el GUID. */
  etiquetaInicial?: string;
  disabled?: boolean;
  id?: string;
}

/**
 * Combobox de cliente de Tesorería (T9.6): una sola caja de texto libre; el
 * backend busca en razón social, clave y RFC (mínimo 2 caracteres).
 */
export function ClienteTesoreriaSelector({
  value,
  onChange,
  etiquetaInicial,
  disabled,
  id,
}: ClienteTesoreriaSelectorProps) {
  const [open, setOpen] = useState(false);
  const [texto, setTexto] = useState('');
  const [seleccionado, setSeleccionado] = useState<ClienteRefDto | null>(null);
  const debounced = useDebouncedValue(texto, 300);
  const query = useClientesTesoreria(open ? debounced : '');
  const items = query.data ?? [];

  const resuelto = seleccionado && seleccionado.id === value ? seleccionado : null;
  const etiqueta = resuelto
    ? `${resuelto.clave} · ${resuelto.razonSocial}`
    : value
      ? (etiquetaInicial ?? 'Cliente seleccionado')
      : 'Buscar cliente…';

  function elegir(c: ClienteRefDto) {
    const deseleccion = c.id === value;
    setSeleccionado(deseleccion ? null : c);
    onChange(deseleccion ? null : c);
    setOpen(false);
    setTexto('');
  }

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          id={id}
          type="button"
          variant="outline"
          role="combobox"
          aria-expanded={open}
          aria-label="Seleccionar cliente"
          disabled={disabled}
          className={cn('w-full justify-between font-normal', !value && 'text-muted-foreground')}
        >
          <span className="truncate">{etiqueta}</span>
          <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
        </Button>
      </PopoverTrigger>
      <PopoverContent align="start" className="w-[min(32rem,90vw)] p-0">
        <Command shouldFilter={false}>
          <div className="flex items-center border-b px-3">
            <Search className="mr-2 h-4 w-4 shrink-0 opacity-50" />
            <input
              type="text"
              aria-label="Buscar cliente por razón social, clave o RFC"
              placeholder="Razón social, clave o RFC…"
              value={texto}
              onChange={(e) => setTexto(e.target.value)}
              className="flex h-11 w-full rounded-md bg-transparent py-3 text-sm outline-none placeholder:text-muted-foreground"
            />
          </div>
          <CommandList>
            {query.isFetching ? (
              <div className="flex items-center justify-center gap-2 py-6 text-sm text-muted-foreground" aria-live="polite">
                <Loader2 className="h-4 w-4 animate-spin" />
                Buscando…
              </div>
            ) : query.isError ? (
              <CatalogoQueryError error={query.error} />
            ) : (
              <>
                <CommandEmpty>
                  {debounced.trim().length >= 2
                    ? `Ningún cliente coincide con "${debounced.trim()}".`
                    : 'Escribe al menos 2 caracteres.'}
                </CommandEmpty>
                <CommandGroup>
                  {items.map((c) => (
                    <CommandItem key={c.id} value={c.id} onSelect={() => elegir(c)} className="flex items-start justify-between gap-3">
                      <div className="min-w-0 flex-1">
                        <span className="font-mono text-xs">{c.clave}</span>
                        <p className="truncate text-sm">{c.razonSocial}</p>
                      </div>
                      {c.id === value && <Check className="h-4 w-4 shrink-0" aria-hidden="true" />}
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
