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
import { useOrdenCompra } from '@/features/compras/ordenes/api/useOrdenCompra';
import type { LineaOrdenCompraResponse } from '@/features/compras/ordenes/api/types';
import { cn } from '@/lib/utils';
import { CatalogoQueryError } from './CatalogoQueryError';

/**
 * <c>&lt;LineaOcSelector/&gt;</c> — selector <b>dependiente</b> de una línea
 * de la OC ya elegida. A diferencia de los selectores de catálogo (proveedor,
 * sucursal, artículo), está parametrizado por <c>ordenCompraId</c>: lista las
 * líneas de ESA OC (vía <c>useOrdenCompra(id).lineas</c>) para vincular una
 * línea de factura con su línea de OC (atribución del three-way match).
 *
 * <para>Sin <c>ordenCompraId</c> el trigger queda deshabilitado (no hay de
 * dónde elegir; el hook tampoco fetchea, <c>enabled: id != null</c>). Cada
 * opción muestra posición + artículo + cantidad + pendiente
 * (<c>cantidad − cantidadFacturada</c>), nunca el GUID. Opcional: permite
 * limpiar con <c>onChange(null)</c>. Molde: <c>OrdenCompraSelector</c>.</para>
 */
export interface LineaOcSelectorProps {
  /** OC de la que se listan las líneas. <c>null</c>/<c>undefined</c> → disabled. */
  ordenCompraId: string | null | undefined;
  value: string | null | undefined;
  onChange: (lineaOcId: string | null) => void;
  disabled?: boolean;
  placeholder?: string;
  className?: string;
}

function etiquetaArticulo(l: LineaOrdenCompraResponse): string {
  const partes = [l.articuloClave, l.articuloNombre].filter(Boolean);
  return partes.length > 0 ? partes.join(' · ') : l.articuloId;
}

export function LineaOcSelector({
  ordenCompraId,
  value,
  onChange,
  disabled,
  placeholder = 'Vincular línea de la OC…',
  className,
}: LineaOcSelectorProps) {
  const [open, setOpen] = useState(false);
  const [input, setInput] = useState('');

  const ocQuery = useOrdenCompra(ordenCompraId ?? null);
  const lineas = ocQuery.data?.lineas ?? [];

  const seleccionada = value ? lineas.find((l) => l.id === value) : undefined;
  // Etiqueta del valor elegido desde .lineas. Fallback al id corto si el value
  // no está en la lista (p. ej. OC recién cambiada antes del reset de C3) — no
  // crashea; C3 previene este estado limpiando los lineaOcId al cambiar de OC.
  const triggerLabel = seleccionada
    ? `Pos. ${seleccionada.posicion} · ${etiquetaArticulo(seleccionada)}`
    : value
      ? value.length > 18
        ? `${value.slice(0, 8)}…`
        : value
      : placeholder;

  const deshabilitado = disabled || !ordenCompraId;

  function handleSelect(item: LineaOrdenCompraResponse) {
    onChange(item.id === value ? null : item.id);
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
          aria-label="Seleccionar línea de OC"
          disabled={deshabilitado}
          className={cn(
            'w-full justify-between font-normal',
            !seleccionada && 'text-muted-foreground',
            className,
          )}
        >
          <span className="truncate">
            {ordenCompraId ? triggerLabel : 'Elige primero una OC'}
          </span>
          <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
        </Button>
      </PopoverTrigger>
      <PopoverContent align="start" className="w-[min(40rem,90vw)] p-0">
        <Command>
          <CommandInput
            placeholder="Posición o artículo…"
            value={input}
            onValueChange={setInput}
          />
          <CommandList>
            {ocQuery.isLoading ? (
              <div
                className="flex items-center justify-center gap-2 py-6 text-sm text-muted-foreground"
                aria-live="polite"
              >
                <Loader2 className="h-4 w-4 animate-spin" />
                Cargando líneas…
              </div>
            ) : ocQuery.isError ? (
              <CatalogoQueryError error={ocQuery.error} />
            ) : (
              <>
                <CommandEmpty>
                  {lineas.length === 0
                    ? 'La OC no tiene líneas.'
                    : 'No hay coincidencias.'}
                </CommandEmpty>
                <CommandGroup>
                  {lineas.map((l) => {
                    const pendiente = l.cantidad - l.cantidadFacturada;
                    // value searchable para el filtro client-side de cmdk
                    // (posición + artículo + id); el id no se renderiza.
                    const valueParaFilter = `${l.posicion} ${etiquetaArticulo(l)} ${l.id}`;
                    return (
                      <CommandItem
                        key={l.id}
                        value={valueParaFilter}
                        onSelect={() => handleSelect(l)}
                        className="flex items-start justify-between gap-3"
                      >
                        <div className="min-w-0 flex-1">
                          <div className="flex items-center gap-2">
                            <span className="rounded bg-muted px-1.5 py-0.5 text-[10px] font-medium text-muted-foreground">
                              Pos. {l.posicion}
                            </span>
                            <span className="truncate text-sm">
                              {etiquetaArticulo(l)}
                            </span>
                          </div>
                          <p className="truncate text-xs text-muted-foreground">
                            Cant. {l.cantidad} {l.unidadMedida} · pendiente{' '}
                            {pendiente}
                          </p>
                        </div>
                        {l.id === value && (
                          <Check className="h-4 w-4 shrink-0" aria-hidden="true" />
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
