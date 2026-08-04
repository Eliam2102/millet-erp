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
import { NaturalezaBadge } from '@/components/erp/display/NaturalezaBadge';
import { useArticulos } from '@/features/catalogos/api';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import type { ArticuloListItem } from '@/features/catalogos/api/types';
import { cn } from '@/lib/utils';
import { CatalogoQueryError } from './CatalogoQueryError';

/**
 * <c>&lt;ArticuloSelector/&gt;</c> — combobox lazy contra
 * <c>GET /api/v1/catalogos/articulos?...&estatus=Activo</c>.
 * Doc 05 §11.3.
 *
 * <para>Patrón:</para>
 * <list>
 *   <item>Trigger button con la clave + nombre del seleccionado, o
 *   placeholder.</item>
 *   <item>DOS cajas de búsqueda EXCLUYENTES dentro del Popover (ADR-0045):
 *   "Buscar por clave…" y "Buscar por nombre…", cada una con debounce
 *   300 ms (doc 05). Al escribir en una, la otra se deshabilita y se
 *   limpia — el hook recibe SOLO clave o SOLO nombre, nunca ambos.</item>
 *   <item><c>Command</c> con <c>shouldFilter=false</c>: el backend filtra
 *   server-side (clave por substring; nombre insensible a acentos y
 *   mayúsculas); el cliente solo renderiza la página actual.</item>
 *   <item>Cada item muestra <c>clave</c> + <c>nombre</c> +
 *   <c>&lt;NaturalezaBadge/&gt;</c> + UM default.</item>
 * </list>
 *
 * <para>Las cajas viven en el Popover (portal, ancho fijo
 * <c>min(36rem,90vw)</c>), no en el contenedor del trigger — el layout de
 * dos cajas es independiente de dónde se monte (líneas inline, sheets,
 * barra de filtro).</para>
 *
 * <para>Filtra <c>estatus=Activo</c> automáticamente; pasamos <c>limit=50</c>
 * (default del backend) — si el usuario tipea algo específico, suele
 * caber. Para listados grandes, el caller puede subir <c>limit</c>.</para>
 *
 * <para>API agnóstica de <c>react-hook-form</c>: recibe <c>value</c> +
 * <c>onChange</c> tipo controlled. El <c>&lt;Controller/&gt;</c> de
 * RHF lo wirea en formularios.</para>
 */
export interface ArticuloSelectorProps {
  /** Id del artículo seleccionado, o <c>null</c>/<c>undefined</c>. */
  value: string | null | undefined;
  onChange: (id: string | null) => void;
  /**
   * Callback aditivo: se dispara con el artículo completo al
   * seleccionarlo (NO al deseleccionar). Permite al caller heredar
   * atributos del catálogo —ej. <c>unidadMedidaDefault</c>— sin re-fetch.
   * Opcional: los consumidores que solo necesitan el id (vía
   * <c>onChange</c>) no se ven afectados.
   */
  onSelect?: (articulo: ArticuloListItem) => void;
  /** Texto placeholder cuando no hay selección. */
  placeholder?: string;
  /** Deshabilita el trigger (read-only mode). */
  disabled?: boolean;
  /** Clase del trigger. */
  className?: string;
  /**
   * Etiqueta inicial ("clave · nombre") del artículo pre-seleccionado al
   * EDITAR, cuando <c>value</c> viene del DTO enriquecido. Permite mostrar
   * la etiqueta sin depender de que el item esté en la lista capada del
   * typeahead (ADR-0042 addendum). En alta no se pasa.
   */
  initialLabel?: string;
  /**
   * Muestra la segunda caja de búsqueda por nombre (ADR-0045). Default
   * <c>true</c>: la búsqueda por nombre es el comportamiento por defecto en
   * los 6 consumidores. Escape hatch para forzar solo-clave si algún
   * contexto futuro lo necesita; ningún consumidor actual lo pasa en
   * <c>false</c>.
   */
  permitirBusquedaPorNombre?: boolean;
}

export function ArticuloSelector({
  value,
  onChange,
  onSelect,
  placeholder = 'Buscar artículo…',
  disabled,
  className,
  initialLabel,
  permitirBusquedaPorNombre = true,
}: ArticuloSelectorProps) {
  const [open, setOpen] = useState(false);
  const [inputClave, setInputClave] = useState('');
  const [inputNombre, setInputNombre] = useState('');
  // Artículo elegido en esta sesión del selector. Se conserva para que el
  // trigger muestre clave·nombre aunque la lista capada se recargue tras
  // seleccionar (bug id→UUID a escala). ADR-0042 addendum.
  const [selected, setSelected] = useState<ArticuloListItem | null>(null);

  const debClave = useDebouncedValue(inputClave, 300);
  const debNombre = useDebouncedValue(inputNombre, 300);

  // Exclusividad: clave tiene precedencia. Solo uno de los dos viaja al
  // backend, incluso a mitad del debounce tras cambiar de caja.
  const claveQuery = debClave.trim() || undefined;
  const nombreQuery =
    permitirBusquedaPorNombre && !claveQuery
      ? debNombre.trim() || undefined
      : undefined;
  const terminoActivo = claveQuery ?? nombreQuery;

  // Estado de cada caja para deshabilitar/limpiar la otra mientras se
  // escribe (sobre el valor inmediato, no el debounced, para feedback ágil).
  const modoClave = inputClave.trim().length > 0;
  const modoNombre =
    permitirBusquedaPorNombre && inputNombre.trim().length > 0;

  const articulosQuery = useArticulos({
    clave: claveQuery,
    nombre: nombreQuery,
    limit: 50,
  });
  const items = articulosQuery.data?.items ?? [];

  // Resolución de la etiqueta del valor seleccionado, en orden de robustez:
  // 1) el objeto guardado al seleccionar (no depende de la lista capada),
  // 2) buscarlo en la página actual del typeahead,
  // 3) la etiqueta inicial del DTO enriquecido (edición),
  // 4) fallback al id (último recurso).
  const resuelto =
    selected && selected.id === value
      ? selected
      : value
        ? items.find((a) => a.id === value)
        : undefined;
  const triggerLabel = resuelto
    ? `${resuelto.clave} · ${resuelto.nombre}`
    : value
      ? initialLabel ?? value
      : placeholder;

  function handleClaveChange(v: string) {
    setInputClave(v);
    if (v) setInputNombre('');
  }

  function handleNombreChange(v: string) {
    setInputNombre(v);
    if (v) setInputClave('');
  }

  function handleSelect(item: ArticuloListItem) {
    const esDeseleccion = item.id === value;
    onChange(esDeseleccion ? null : item.id);
    setSelected(esDeseleccion ? null : item);
    // onSelect solo al seleccionar en firme (no al deseleccionar), para
    // que el caller herede atributos del artículo recién elegido.
    if (!esDeseleccion) onSelect?.(item);
    setOpen(false);
    setInputClave('');
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
          aria-label="Seleccionar artículo"
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
      <PopoverContent
        align="start"
        className="w-[min(36rem,90vw)] p-0"
      >
        <Command shouldFilter={false}>
          {/* Caja 1: clave. Se deshabilita y limpia si se busca por nombre. */}
          <div className="flex items-center border-b px-3">
            <Search className="mr-2 h-4 w-4 shrink-0 opacity-50" />
            <input
              type="text"
              aria-label="Buscar artículo por clave"
              placeholder="Buscar por clave…"
              value={inputClave}
              onChange={(e) => handleClaveChange(e.target.value)}
              disabled={disabled || modoNombre}
              className="flex h-11 w-full rounded-md bg-transparent py-3 text-sm outline-none placeholder:text-muted-foreground disabled:cursor-not-allowed disabled:opacity-50"
            />
          </div>
          {/* Caja 2: nombre (insensible a acentos/mayúsculas). Excluyente. */}
          {permitirBusquedaPorNombre && (
            <div className="flex items-center border-b px-3">
              <Search className="mr-2 h-4 w-4 shrink-0 opacity-50" />
              <input
                type="text"
                aria-label="Buscar artículo por nombre"
                placeholder="Buscar por nombre…"
                value={inputNombre}
                onChange={(e) => handleNombreChange(e.target.value)}
                disabled={disabled || modoClave}
                className="flex h-11 w-full rounded-md bg-transparent py-3 text-sm outline-none placeholder:text-muted-foreground disabled:cursor-not-allowed disabled:opacity-50"
              />
            </div>
          )}
          <CommandList>
            {articulosQuery.isLoading || articulosQuery.isFetching ? (
              <div
                className="flex items-center justify-center gap-2 py-6 text-sm text-muted-foreground"
                aria-live="polite"
              >
                <Loader2 className="h-4 w-4 animate-spin" />
                Buscando…
              </div>
            ) : articulosQuery.isError ? (
              <CatalogoQueryError error={articulosQuery.error} />
            ) : (
              <>
                <CommandEmpty>
                  {terminoActivo
                    ? `Ningún artículo activo coincide con "${terminoActivo}".`
                    : permitirBusquedaPorNombre
                      ? 'Empieza a escribir la clave o el nombre del artículo.'
                      : 'Empieza a escribir la clave del artículo.'}
                </CommandEmpty>
                <CommandGroup>
                  {items.map((a) => (
                    <CommandItem
                      key={a.id}
                      value={a.id}
                      onSelect={() => handleSelect(a)}
                      className="flex items-start justify-between gap-3"
                    >
                      <div className="min-w-0 flex-1">
                        <div className="flex items-center gap-2">
                          <span className="truncate font-mono text-xs">
                            {a.clave}
                          </span>
                          <span className="text-xs text-muted-foreground">
                            {a.unidadMedidaDefault}
                          </span>
                        </div>
                        <p className="truncate text-sm">{a.nombre}</p>
                      </div>
                      <div className="flex shrink-0 items-center gap-2">
                        <NaturalezaBadge naturaleza={a.naturaleza} />
                        {a.id === value && (
                          <Check className="h-4 w-4" aria-hidden="true" />
                        )}
                      </div>
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
