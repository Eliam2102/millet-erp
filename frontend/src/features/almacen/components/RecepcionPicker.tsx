import { useState } from 'react';
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
import { useRecepciones } from '@/features/almacen/api/useRecepciones';
import {
  EstadoMovimiento,
  type RecepcionListItem,
} from '@/features/almacen/api/types';
import { cn } from '@/lib/utils';
import { CatalogoQueryError } from '@/components/erp/selectors/CatalogoQueryError';

/**
 * <c>&lt;RecepcionPicker/&gt;</c> — combobox de recepciones registradas
 * para referencias cruzadas (recepción origen de una devolución a
 * proveedor 8.B). Reemplaza los inputs de GUID crudo, mismo patrón que
 * <c>FacturaPicker</c> de CxP.
 *
 * <para>Con <c>ordenCompraId</c> pre-filtra server-side a la OC (caso
 * devolución: la recepción origen es de la OC seleccionada); sin él
 * lista las últimas 200 registradas y cmdk filtra client-side por
 * folio, folio de OC, fecha o monto.</para>
 */
export interface RecepcionPickerProps {
  value: string | null;
  onChange: (id: string | null) => void;
  /** Pre-filtro server-side por OC. Null → sin filtro. */
  ordenCompraId?: string | null;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
}

export function RecepcionPicker({
  value,
  onChange,
  ordenCompraId = null,
  placeholder = 'Selecciona recepción…',
  disabled,
  className,
}: RecepcionPickerProps) {
  const [open, setOpen] = useState(false);

  const query = useRecepciones({
    ordenCompraId: ordenCompraId ?? undefined,
    estado: EstadoMovimiento.Registrado,
    limit: 200,
  });

  const items = query.data?.items ?? [];
  const seleccionada = value ? items.find((r) => r.id === value) : undefined;

  function ocDe(r: RecepcionListItem) {
    return (
      r.ordenCompraFolio ??
      (r.ordenCompraId ? `${r.ordenCompraId.slice(0, 8)}…` : null)
    );
  }

  const triggerLabel = seleccionada
    ? `${seleccionada.folio} · ${seleccionada.fechaMovimiento}`
    : value
      ? `${value.slice(0, 8)}…`
      : placeholder;

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          type="button"
          variant="outline"
          role="combobox"
          aria-expanded={open}
          aria-label="Seleccionar recepción"
          disabled={disabled}
          className={cn(
            'w-full justify-between font-normal',
            !seleccionada && 'text-muted-foreground',
            className,
          )}
        >
          <span className="truncate">{triggerLabel}</span>
          <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
        </Button>
      </PopoverTrigger>
      <PopoverContent align="start" className="w-[min(40rem,90vw)] p-0">
        <Command>
          <CommandInput placeholder="Folio, OC, fecha o monto…" />
          <CommandList>
            {query.isLoading ? (
              <div
                className="flex items-center justify-center gap-2 py-6 text-sm text-muted-foreground"
                aria-live="polite"
              >
                <Loader2 className="h-4 w-4 animate-spin" />
                Cargando recepciones…
              </div>
            ) : query.isError ? (
              <CatalogoQueryError error={query.error} />
            ) : (
              <>
                <CommandEmpty>
                  {items.length === 0
                    ? ordenCompraId
                      ? 'La OC no tiene recepciones registradas.'
                      : 'No hay recepciones registradas.'
                    : 'No hay coincidencias.'}
                </CommandEmpty>
                <CommandGroup>
                  {items.map((r) => {
                    const oc = ocDe(r);
                    const valueParaFilter = `${r.folio} ${oc ?? ''} ${r.fechaMovimiento} ${r.montoTotalMxn}`;
                    return (
                      <CommandItem
                        key={r.id}
                        value={valueParaFilter}
                        onSelect={() => {
                          onChange(r.id === value ? null : r.id);
                          setOpen(false);
                        }}
                        className="flex items-start justify-between gap-3"
                      >
                        <div className="min-w-0 flex-1">
                          <div className="flex items-center gap-2">
                            <span className="truncate font-mono text-xs">
                              {r.folio}
                            </span>
                            <span className="rounded bg-muted px-1.5 py-0.5 text-[10px] font-medium text-muted-foreground">
                              MXN {r.montoTotalMxn.toFixed(2)}
                            </span>
                          </div>
                          <p className="truncate text-xs text-muted-foreground">
                            {oc ? `OC ${oc} · ` : ''}
                            {r.fechaMovimiento}
                          </p>
                        </div>
                        {r.id === value && (
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
