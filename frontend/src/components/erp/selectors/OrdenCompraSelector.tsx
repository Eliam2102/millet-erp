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
import { useOrdenesCompra } from '@/features/compras/ordenes/api/useOrdenesCompra';
import {
  EstadoOrdenCompra,
  type OrdenCompraResumen,
} from '@/features/compras/ordenes/api/types';
import { cn } from '@/lib/utils';
import { CatalogoQueryError } from './CatalogoQueryError';

/**
 * <c>&lt;OrdenCompraSelector/&gt;</c> — combobox para seleccionar una
 * Orden de Compra. Patrón <b>eager</b> con filtro client-side: el
 * backend no expone filtro por <c>folio</c>, así que cargamos los
 * últimos 200 OCs en el estado pedido y filtramos por
 * folio/referencia/proveedor desde el cliente.
 *
 * <para>El filtro por <c>estado</c> es importante: por default
 * mostramos solo <c>Autorizada</c> (las que aceptan recepción).
 * Caller puede pasar <c>estado=null</c> para mostrar todas.</para>
 *
 * <para>Reusable cross-módulo: Almacén (recepción contra OC),
 * CxP (factura contra OC), etc. El filtro
 * <c>soloConPendienteRecepcion</c> es <b>opt-in</b> (default off): solo
 * Nueva recepción lo activa para ocultar OCs ya recibidas al 100%;
 * Devolución a proveedor y CxP NO lo usan (necesitan otro recorte).</para>
 */
export interface OrdenCompraSelectorProps {
  value: string | null | undefined;
  onChange: (id: string | null) => void;
  /**
   * Callback aditivo: se dispara con la OC completa al SELECCIONARLA (NO al
   * deseleccionar). Permite al caller heredar atributos del resumen —ej.
   * <c>proveedorId</c>/<c>sucursalId</c> en la captura de factura— sin
   * re-fetch. Opcional: los consumidores que solo necesitan el id (vía
   * <c>onChange</c>) no se ven afectados. Molde de <c>ArticuloSelector</c>.
   */
  onSelect?: (oc: OrdenCompraResumen) => void;
  /**
   * Estado de OC a filtrar. Default: <c>Autorizada</c> (las que
   * aceptan recepción/facturación). Pasa <c>null</c> para no filtrar.
   */
  estado?: EstadoOrdenCompra | null;
  /** Pre-filtrar por proveedor (opcional). */
  proveedorId?: string | null;
  /**
   * Si <c>true</c>, lista solo OCs con recepción pendiente
   * (SubEstadoRecepcion != Completa). Opt-in: lo activa Nueva recepción.
   * Default <c>false</c> (no recorta) para no afectar a otros
   * consumidores (Devolución a proveedor, CxP).
   */
  soloConPendienteRecepcion?: boolean;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
}

export function OrdenCompraSelector({
  value,
  onChange,
  onSelect,
  estado = EstadoOrdenCompra.Autorizada,
  proveedorId,
  soloConPendienteRecepcion = false,
  placeholder = 'Buscar OC por folio o referencia…',
  disabled,
  className,
}: OrdenCompraSelectorProps) {
  const [open, setOpen] = useState(false);
  const [input, setInput] = useState('');

  const ocQuery = useOrdenesCompra({
    estado: estado ?? undefined,
    proveedorId: proveedorId ?? undefined,
    soloConPendienteRecepcion: soloConPendienteRecepcion || undefined,
    pageSize: 200,
  });
  const items = ocQuery.data?.items ?? [];

  const seleccionada = value ? items.find((o) => o.id === value) : undefined;
  const triggerLabel = seleccionada
    ? `${seleccionada.folio} · ${seleccionada.proveedorNombre ?? seleccionada.proveedorId}`
    : value
      ? value.length > 18
        ? `${value.slice(0, 8)}…`
        : value
      : placeholder;

  function handleSelect(item: OrdenCompraResumen) {
    const esDeseleccion = item.id === value;
    onChange(esDeseleccion ? null : item.id);
    // onSelect solo al SELECCIONAR (no al limpiar): el caller hereda el
    // resumen recién elegido. Molde de ArticuloSelector.
    if (!esDeseleccion) {
      onSelect?.(item);
    }
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
          aria-label="Seleccionar orden de compra"
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
          <CommandInput
            placeholder="Folio, referencia o proveedor…"
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
                Cargando órdenes…
              </div>
            ) : ocQuery.isError ? (
              <CatalogoQueryError error={ocQuery.error} />
            ) : (
              <>
                <CommandEmpty>
                  {items.length === 0
                    ? 'No hay órdenes de compra en este estado.'
                    : 'No hay coincidencias.'}
                </CommandEmpty>
                <CommandGroup>
                  {items.map((o) => {
                    // proveedorNombre viene resuelto del resumen (server-side,
                    // ADR-0042); fallback al id solo si el backend no resolvió.
                    const proveedor = o.proveedorNombre ?? o.proveedorId;
                    // Concatenamos los campos buscables en el value para
                    // que el filtro client-side de Command los matchee.
                    const valueParaFilter = `${o.folio} ${o.id} ${proveedor}`;
                    return (
                      <CommandItem
                        key={o.id}
                        value={valueParaFilter}
                        onSelect={() => handleSelect(o)}
                        className="flex items-start justify-between gap-3"
                      >
                        <div className="min-w-0 flex-1">
                          <div className="flex items-center gap-2">
                            <span className="truncate font-mono text-xs">
                              {o.folio}
                            </span>
                            <span className="rounded bg-muted px-1.5 py-0.5 text-[10px] font-medium text-muted-foreground">
                              {o.moneda}
                            </span>
                          </div>
                          <p className="truncate text-sm">{proveedor}</p>
                        </div>
                        {o.id === value && (
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
