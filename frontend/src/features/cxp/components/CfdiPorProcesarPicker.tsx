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
import { useCfdis } from '@/features/cxp/api/useCfdis';
import {
  EstadoCfdiRecibido,
  TipoCfdi,
  type CfdiListItem,
} from '@/features/cxp/api/types';
import { esApiError } from '@/lib/api';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;CfdiPorProcesarPicker/&gt;</c> — combobox para vincular un CFDI
 * recibido en estado <c>PorProcesar</c> a una captura. Por defecto filtra
 * tipo Ingreso (facturas, anticipos); la captura de NC pasa
 * <c>tipo=Egreso</c>. Es la contraparte de la acción "Capturar factura"
 * de la bandeja de CFDIs: permite iniciar la conciliación desde el lado
 * de Facturas.
 *
 * <para>A diferencia de <c>CfdiOriginalPicker</c> (duplicados, filtra
 * <c>ConvertidoEnPasivo</c> por emisor), aquí el universo son los CFDIs
 * pendientes de procesar — típicamente decenas — y el caller necesita el
 * item completo (<c>onSelect</c>) para pre-llenar la factura, no solo el
 * id.</para>
 */
export interface CfdiPorProcesarPickerProps {
  /** Id del CFDI vinculado (controlled). */
  value: string | null;
  /** Recibe el CFDI completo al seleccionar, o <c>null</c> al quitar. */
  onSelect: (cfdi: CfdiListItem | null) => void;
  /**
   * Pre-filtro server-side por RFC emisor (mismo parámetro que usa
   * <c>CfdiOriginalPicker</c>). Lo pasa la recepción de Almacén con el
   * RFC del proveedor de la OC para acotar el universo al proveedor.
   */
  rfcEmisor?: string;
  /** Tipo de comprobante SAT a filtrar. Default: Ingreso. */
  tipo?: TipoCfdi;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
}

export function CfdiPorProcesarPicker({
  value,
  onSelect,
  rfcEmisor,
  tipo = TipoCfdi.Ingreso,
  placeholder = 'Vincular CFDI recibido…',
  disabled,
  className,
}: CfdiPorProcesarPickerProps) {
  const [open, setOpen] = useState(false);

  const query = useCfdis({
    estado: EstadoCfdiRecibido.PorProcesar,
    tipo,
    rfcEmisor,
    limit: 200,
  });

  const items = query.data?.items ?? [];
  const seleccionado = value ? items.find((c) => c.id === value) : undefined;

  function handleSelect(item: CfdiListItem) {
    onSelect(item.id === value ? null : item);
    setOpen(false);
  }

  function formatTrigger(c: CfdiListItem) {
    const folio = c.serie || c.folio ? `${c.serie ?? ''}${c.folio ?? ''}` : '—';
    return `${folio} · ${c.uuidCfdi.slice(0, 8)}… · ${c.rfcEmisor}`;
  }

  const triggerLabel = seleccionado
    ? formatTrigger(seleccionado)
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
          aria-label="Vincular CFDI recibido"
          disabled={disabled}
          className={cn(
            'w-full justify-between font-normal',
            !seleccionado && 'text-muted-foreground',
            className,
          )}
        >
          <span className="truncate">{triggerLabel}</span>
          <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
        </Button>
      </PopoverTrigger>
      <PopoverContent align="start" className="w-[min(40rem,90vw)] p-0">
        <Command>
          <CommandInput placeholder="Folio, UUID SAT, RFC o fecha…" />
          <CommandList>
            {query.isLoading ? (
              <div
                className="flex items-center justify-center gap-2 py-6 text-sm text-muted-foreground"
                aria-live="polite"
              >
                <Loader2 className="h-4 w-4 animate-spin" />
                Cargando CFDIs por procesar…
              </div>
            ) : query.isError ? (
              /* Sin este estado, un 403 se veía como lista vacía ("no hay
                 CFDIs") y nadie sospechaba del permiso faltante. */
              <div className="px-4 py-6 text-sm text-muted-foreground" role="alert">
                {esApiError(query.error) && query.error.status === 403
                  ? 'Sin permiso para consultar el repositorio de CFDIs ' +
                    '(cuentas_por_pagar.cfdis.leer). Pídelo al administrador.'
                  : 'No se pudo consultar el repositorio de CFDIs. Reintenta más tarde.'}
              </div>
            ) : (
              <>
                <CommandEmpty>
                  {items.length === 0
                    ? rfcEmisor
                      ? `No hay CFDIs por procesar del RFC ${rfcEmisor}.`
                      : tipo === TipoCfdi.Egreso
                        ? 'No hay CFDIs de egreso por procesar.'
                        : 'No hay CFDIs de ingreso por procesar.'
                    : 'No hay coincidencias.'}
                </CommandEmpty>
                <CommandGroup>
                  {items.map((c) => {
                    const folio =
                      c.serie || c.folio
                        ? `${c.serie ?? ''}${c.folio ?? ''}`
                        : '—';
                    const valueParaFilter = `${folio} ${c.uuidCfdi} ${c.rfcEmisor} ${c.fechaCfdi} ${c.total}`;
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
                            <span className="truncate font-mono text-[10px] text-muted-foreground">
                              {c.rfcEmisor}
                            </span>
                            <span className="rounded bg-muted px-1.5 py-0.5 text-[10px] font-medium text-muted-foreground">
                              {c.moneda} {c.total.toFixed(2)}
                            </span>
                          </div>
                          <p className="truncate text-xs text-muted-foreground">
                            UUID {c.uuidCfdi.slice(0, 18)}… ·{' '}
                            {c.fechaCfdi.slice(0, 10)}
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
