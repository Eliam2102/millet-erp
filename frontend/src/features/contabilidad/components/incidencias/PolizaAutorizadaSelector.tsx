import { useState } from 'react';
import { Check, ChevronsUpDown, Loader2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from '@/components/ui/command';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { CatalogoQueryError } from '@/components/erp/selectors/CatalogoQueryError';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import { cn } from '@/lib/utils';
import { usePoliza, usePolizas } from '../../api/polizas';
import { etiquetaPoliza } from '../../lib/etiquetas-incidencia';

/**
 * H-U29: combobox de pólizas AUTORIZADAS con búsqueda en el servidor por folio o concepto (`q`, debounce 200 ms, páginas de 20).
 * Sustituye a pegar el identificador. La póliza elegida fuera de la página actual se resuelve con `usePoliza(value)`.
 */
export function PolizaAutorizadaSelector({
  id,
  value,
  onChange,
  excluirId,
  placeholder = 'Buscar póliza por folio…',
}: {
  id?: string;
  value: string | null;
  onChange: (id: string | null) => void;
  /** Póliza que no puede elegirse (p. ej. la misma de la incidencia como su ajuste). */
  excluirId?: string | null;
  placeholder?: string;
}) {
  const [open, setOpen] = useState(false);
  const [input, setInput] = useState('');
  const q = useDebouncedValue(input, 200).trim();
  const query = usePolizas({ estado: 'Autorizada', q: q || undefined, offset: 0, limit: 20 });
  const items = (query.data?.items ?? []).filter((p) => p.id !== excluirId);
  const total = query.data?.total ?? items.length;

  const enPagina = value ? items.find((p) => p.id === value) : undefined;
  const detalle = usePoliza(value && !enPagina ? value : null);
  const etiqueta = enPagina
    ? etiquetaPoliza(enPagina)
    : value
      ? detalle.data
        ? etiquetaPoliza(detalle.data)
        : 'Cargando póliza…'
      : placeholder;

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          id={id}
          type="button"
          variant="outline"
          role="combobox"
          aria-expanded={open}
          aria-label="Seleccionar póliza"
          className={cn('w-full justify-between font-normal', !value && 'text-muted-foreground')}
        >
          <span className="truncate">{etiqueta}</span>
          <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
        </Button>
      </PopoverTrigger>
      <PopoverContent align="start" className="w-[min(36rem,90vw)] p-0">
        <Command shouldFilter={false}>
          <CommandInput placeholder="Folio (p. ej. AS-000123) o concepto…" value={input} onValueChange={setInput} />
          <CommandList>
            {query.isLoading ? (
              <div className="flex items-center justify-center gap-2 py-6 text-sm text-muted-foreground" aria-live="polite">
                <Loader2 className="h-4 w-4 animate-spin" />
                Buscando pólizas…
              </div>
            ) : query.isError ? (
              <CatalogoQueryError error={query.error} />
            ) : (
              <>
                <CommandEmpty>{q ? 'No hay pólizas autorizadas con ese folio.' : 'No hay pólizas autorizadas.'}</CommandEmpty>
                <CommandGroup>
                  {items.map((p) => (
                    <CommandItem
                      key={p.id}
                      value={p.id}
                      onSelect={() => {
                        onChange(p.id === value ? null : p.id);
                        setOpen(false);
                        setInput('');
                      }}
                      className="flex items-start justify-between gap-3"
                    >
                      <div className="min-w-0 flex-1">
                        <div className="flex items-center gap-2">
                          <span className="font-mono text-xs">{p.folio ?? 'Sin folio'}</span>
                          <span className="text-[11px] text-ink-muted">
                            {`${p.periodoAnio}-${String(p.periodoNumero).padStart(2, '0')}`}
                          </span>
                        </div>
                        <p className="truncate text-sm">{p.concepto}</p>
                      </div>
                      {p.id === value && <Check className="h-4 w-4 shrink-0" aria-hidden="true" />}
                    </CommandItem>
                  ))}
                </CommandGroup>
                {total > items.length && (
                  <p className="border-t px-3 py-2 text-xs text-ink-muted">
                    Mostrando {items.length} de {total}. Escribe el folio para acotar.
                  </p>
                )}
              </>
            )}
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}
