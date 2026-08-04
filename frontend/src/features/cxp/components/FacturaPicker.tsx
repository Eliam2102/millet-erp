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
import { useFacturas } from '@/features/cxp/api/useFacturas';
import {
  EstadoPasivoLabels,
  type FacturaListItem,
} from '@/features/cxp/api/types';
import { cn } from '@/lib/utils';
import { CatalogoQueryError } from '@/components/erp/selectors/CatalogoQueryError';

/**
 * <c>&lt;FacturaPicker/&gt;</c> — combobox de facturas de proveedor para
 * referencias cruzadas (factura origen de una nota de cargo, facturas
 * agrupadas en comprobación aduanal). Reemplaza los inputs de GUID
 * crudo.
 *
 * <para>Con <c>proveedorId</c> pre-filtra server-side al proveedor (caso
 * nota de cargo: la factura origen es del mismo proveedor); sin él lista
 * las últimas 200 y cmdk filtra client-side por folio, nombre del
 * proveedor o monto (caso aduanales, donde las facturas del pedimento
 * pueden ser de varios proveedores).</para>
 */
export interface FacturaPickerProps {
  value: string | null;
  onChange: (id: string | null) => void;
  /** Pre-filtro server-side por proveedor. Null → sin filtro. */
  proveedorId?: string | null;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
}

export function FacturaPicker({
  value,
  onChange,
  proveedorId = null,
  placeholder = 'Selecciona factura…',
  disabled,
  className,
}: FacturaPickerProps) {
  const [open, setOpen] = useState(false);

  const query = useFacturas({
    proveedorId: proveedorId ?? undefined,
    limit: 200,
  });

  const items = query.data?.items ?? [];
  const seleccionada = value ? items.find((f) => f.id === value) : undefined;

  function folioDe(f: FacturaListItem) {
    return f.serieProveedor
      ? `${f.serieProveedor}-${f.folioProveedor ?? ''}`
      : (f.folioProveedor ?? '—');
  }

  const triggerLabel = seleccionada
    ? `${folioDe(seleccionada)} · ${seleccionada.moneda} ${seleccionada.total.toFixed(2)}`
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
          aria-label="Seleccionar factura"
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
          <CommandInput placeholder="Folio, proveedor o monto…" />
          <CommandList>
            {query.isLoading ? (
              <div
                className="flex items-center justify-center gap-2 py-6 text-sm text-muted-foreground"
                aria-live="polite"
              >
                <Loader2 className="h-4 w-4 animate-spin" />
                Cargando facturas…
              </div>
            ) : query.isError ? (
              <CatalogoQueryError error={query.error} />
            ) : (
              <>
                <CommandEmpty>
                  {items.length === 0
                    ? proveedorId
                      ? 'El proveedor no tiene facturas capturadas.'
                      : 'No hay facturas capturadas.'
                    : 'No hay coincidencias.'}
                </CommandEmpty>
                <CommandGroup>
                  {items.map((f) => {
                    const folio = folioDe(f);
                    const valueParaFilter = `${folio} ${f.proveedorNombre ?? ''} ${f.fechaDocumento} ${f.total}`;
                    return (
                      <CommandItem
                        key={f.id}
                        value={valueParaFilter}
                        onSelect={() => {
                          onChange(f.id === value ? null : f.id);
                          setOpen(false);
                        }}
                        className="flex items-start justify-between gap-3"
                      >
                        <div className="min-w-0 flex-1">
                          <div className="flex items-center gap-2">
                            <span className="truncate font-mono text-xs">
                              {folio}
                            </span>
                            <span className="rounded bg-muted px-1.5 py-0.5 text-[10px] font-medium text-muted-foreground">
                              {f.moneda} {f.total.toFixed(2)}
                            </span>
                            <span className="text-[10px] text-muted-foreground">
                              {EstadoPasivoLabels[f.estado]}
                            </span>
                          </div>
                          <p className="truncate text-xs text-muted-foreground">
                            {f.proveedorNombre ?? f.proveedorId} ·{' '}
                            {f.fechaDocumento.slice(0, 10)}
                          </p>
                        </div>
                        {f.id === value && (
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
