import { useMemo, useState } from 'react';
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
import { useCfdis } from '@/features/cxp/api/useCfdis';
import {
  EstadoCfdiRecibido,
  type CfdiListItem,
} from '@/features/cxp/api/types';
import { cn } from '@/lib/utils';
import { CatalogoQueryError } from './CatalogoQueryError';

/**
 * <c>&lt;CfdiOriginalPicker/&gt;</c> — combobox para seleccionar el
 * CFDI <i>original</i> del que un CFDI nuevo es duplicado.
 *
 * <para>Pre-filtra server-side por <c>rfcEmisor</c> (mismo emisor del
 * duplicado) + <c>estado = ConvertidoEnPasivo</c> (sólo CFDIs ya
 * convertidos en pasivo califican como originales). Excluye el propio
 * <c>currentCfdiId</c> de los candidatos client-side.</para>
 *
 * <para>Patrón <b>eager con pre-filter del backend</b>: el subconjunto
 * por emisor suele ser pequeño (decenas/centenas a lo sumo) → no
 * justifica búsqueda lazy con debounce. cmdk filtra client-side por
 * UUID SAT, folio o fecha.</para>
 *
 * <para>Reemplaza el input UUID a mano que vivía en
 * <c>MarcarDuplicadoSheet</c> (PLATFORM-TODO &lt;CfdiOriginalPicker&gt;).</para>
 */
export interface CfdiOriginalPickerProps {
  value: string | null | undefined;
  onChange: (id: string | null) => void;
  /** RFC del emisor del duplicado — filtra el universo de candidatos. */
  rfcEmisor: string | null | undefined;
  /** ID del CFDI duplicado actual — se excluye de los candidatos. */
  currentCfdiId: string | null | undefined;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
}

export function CfdiOriginalPicker({
  value,
  onChange,
  rfcEmisor,
  currentCfdiId,
  placeholder = 'Selecciona CFDI original…',
  disabled,
  className,
}: CfdiOriginalPickerProps) {
  const [open, setOpen] = useState(false);

  const query = useCfdis({
    estado: EstadoCfdiRecibido.ConvertidoEnPasivo,
    rfcEmisor: rfcEmisor ?? undefined,
    limit: 100,
  });

  const items = useMemo(() => {
    const all = query.data?.items ?? [];
    return currentCfdiId ? all.filter((c) => c.id !== currentCfdiId) : all;
  }, [query.data, currentCfdiId]);

  const seleccionado = value ? items.find((c) => c.id === value) : undefined;

  function handleSelect(item: CfdiListItem) {
    onChange(item.id === value ? null : item.id);
    setOpen(false);
  }

  function formatTrigger(c: CfdiListItem) {
    const folio = c.serie || c.folio ? `${c.serie ?? ''}${c.folio ?? ''}` : '—';
    const uuidShort = c.uuidCfdi ? `${c.uuidCfdi.slice(0, 8)}…` : '';
    return `${folio} · ${uuidShort} · ${c.fechaCfdi}`;
  }

  const triggerLabel = seleccionado
    ? formatTrigger(seleccionado)
    : value
      ? value.length > 18
        ? `${value.slice(0, 8)}…`
        : value
      : placeholder;

  const noEmisor = !rfcEmisor;

  return (
    <Popover open={open && !noEmisor} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          type="button"
          variant="outline"
          role="combobox"
          aria-expanded={open}
          aria-label="Seleccionar CFDI original"
          disabled={disabled || noEmisor}
          className={cn(
            'w-full justify-between font-normal',
            !seleccionado && 'text-muted-foreground',
            className,
          )}
        >
          <span className="truncate">
            {noEmisor ? 'Sin emisor — selecciona CFDI primero' : triggerLabel}
          </span>
          <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
        </Button>
      </PopoverTrigger>
      <PopoverContent align="start" className="w-[min(40rem,90vw)] p-0">
        <Command>
          <CommandInput placeholder="Folio, UUID SAT o fecha…" />
          <CommandList>
            {query.isLoading ? (
              <div
                className="flex items-center justify-center gap-2 py-6 text-sm text-muted-foreground"
                aria-live="polite"
              >
                <Loader2 className="h-4 w-4 animate-spin" />
                Cargando CFDIs del emisor…
              </div>
            ) : query.isError ? (
              <CatalogoQueryError error={query.error} />
            ) : (
              <>
                <CommandEmpty>
                  {items.length === 0
                    ? `No hay CFDIs convertidos en pasivo para ${rfcEmisor}.`
                    : 'No hay coincidencias.'}
                </CommandEmpty>
                <CommandGroup>
                  {items.map((c) => {
                    const folio =
                      c.serie || c.folio
                        ? `${c.serie ?? ''}${c.folio ?? ''}`
                        : '—';
                    // cmdk filtra contra `value`: concatenamos los campos
                    // searchables para que la búsqueda matchee por UUID
                    // SAT, folio, serie o fecha.
                    const valueParaFilter = `${folio} ${c.uuidCfdi} ${c.fechaCfdi} ${c.total}`;
                    return (
                      <CommandItem
                        key={c.id}
                        value={valueParaFilter}
                        onSelect={() => handleSelect(c)}
                        className="flex items-start justify-between gap-3"
                      >
                        <div className="min-w-0 flex-1">
                          <div className="flex items-center gap-2">
                            <span className="truncate font-mono text-xs">
                              {folio}
                            </span>
                            <span className="rounded bg-muted px-1.5 py-0.5 text-[10px] font-medium text-muted-foreground">
                              {c.moneda} {c.total.toFixed(2)}
                            </span>
                          </div>
                          <p className="truncate text-xs text-muted-foreground">
                            UUID {c.uuidCfdi.slice(0, 18)}… · {c.fechaCfdi}
                          </p>
                        </div>
                        {c.id === value && (
                          <Check
                            className="h-4 w-4 shrink-0"
                            aria-hidden="true"
                          />
                        )}
                      </CommandItem>
                    );
                  })}
                </CommandGroup>
              </>
            )}
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}
