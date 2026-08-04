import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
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
import { CatalogoQueryError } from '@/components/erp/selectors/CatalogoQueryError';
import { apiRequest } from '@/lib/api';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import { cn } from '@/lib/utils';
import { centrosCostoKeys } from '@/features/centros-costo/api/keys';
import { type Dim3BusquedaItem } from '@/features/centros-costo/api/types';
import { etiquetaNivel } from '@/features/centros-costo/lib/etiquetas';
import { formatCcMaquinaLabel } from '@/features/centros-costo/lib/cc-maquina-label';

/**
 * <c>&lt;Dim3Picker/&gt;</c> — combobox server-side (typeahead) del CC-Máquina
 * (Dim3) para los documentos de la Fase E. Molde funcional de
 * <c>ArticuloSelector</c> (05 §11.3): trigger con la etiqueta del
 * seleccionado, caja de búsqueda con debounce 300 ms, <c>Command</c> con
 * <c>shouldFilter=false</c> (el backend filtra), item con contexto de
 * desambiguación (área) para elegir entre las ~361.
 *
 * <para>ENDPOINT-AGNÓSTICO a propósito: el mismo picker sirve al selector
 * FILTRADO (RQ, <c>/dim3/buscar</c> con alcance) y a los ABIERTOS por proxy
 * (vale, línea manual de OC — rutas gateadas por permiso en Almacén/Compras),
 * que llegan en PR3/PR5. Quién FILTRA lo decide el ENDPOINT en el backend, no
 * este componente (ADR-0050 §3): el picker solo busca contra la URL que le
 * pasan. No cablea ningún documento todavía; es infra.</para>
 *
 * <para>Display de la etiqueta = <c>formatCcMaquinaLabel</c> ("clave — nombre",
 * sin jerarquía, CC-G4). <c>initialLabel</c> cubre el valor pre-seleccionado
 * al editar sin depender de que el item caiga en el top-N (ADR-0042 addendum),
 * y como la query solo corre con el popover abierto, es también la etiqueta
 * del trigger en reposo.</para>
 */
export interface Dim3PickerProps {
  /** Id de la Dim3 (máquina) seleccionada, o <c>null</c>/<c>undefined</c>. */
  value: string | null | undefined;
  onChange: (id: string | null) => void;
  /**
   * URL base del selector (sin query string). El picker le agrega
   * <c>?q=&limit=&incluirInactivas=</c>. Ej. filtrado:
   * <c>/api/v1/centros-costo/dim3/buscar</c>; abiertos: los endpoints
   * gateados de Compras/Almacén (PR3/PR5).
   */
  endpoint: string;
  /** Callback aditivo con el item completo al seleccionar (no al deseleccionar). */
  onSelect?: (item: Dim3BusquedaItem) => void;
  /** Etiqueta "clave — nombre" del valor pre-seleccionado al editar (DTO enriquecido). */
  initialLabel?: string;
  /** Incluir inactivas en la búsqueda (default false: solo activas). */
  incluirInactivas?: boolean;
  placeholder?: string;
  ariaLabel?: string;
  /** Marca el trigger como inválido (aria-invalid + anillo) para forms. */
  invalid?: boolean;
  disabled?: boolean;
  className?: string;
}

export function Dim3Picker({
  value,
  onChange,
  endpoint,
  onSelect,
  initialLabel,
  incluirInactivas = false,
  // "Máquina" desde el helper único de etiquetas (documentos), no hardcodeado.
  placeholder = `Buscar ${etiquetaNivel('dim3', 'documentos').toLowerCase()}…`,
  ariaLabel = `Seleccionar ${etiquetaNivel('dim3', 'documentos').toLowerCase()}`,
  invalid,
  disabled,
  className,
}: Dim3PickerProps) {
  const [open, setOpen] = useState(false);
  const [input, setInput] = useState('');
  // Item elegido en esta sesión: conserva "clave — nombre" en el trigger
  // aunque la lista capada se recargue tras seleccionar (ADR-0042 addendum).
  const [selected, setSelected] = useState<Dim3BusquedaItem | null>(null);

  const debQ = useDebouncedValue(input, 300);
  const termino = debQ.trim() || undefined;

  const busqueda = useQuery({
    queryKey: centrosCostoKeys.buscarDim3(endpoint, {
      q: termino,
      incluirInactivas,
    }),
    // Solo busca con el popover abierto: un documento con muchas líneas no
    // dispara N queries en reposo (el trigger vive de initialLabel/selected).
    enabled: open,
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      if (termino) params.set('q', termino);
      params.set('limit', '20');
      if (incluirInactivas) params.set('incluirInactivas', 'true');
      const { data } = await apiRequest<Dim3BusquedaItem[]>(
        `${endpoint}?${params.toString()}`,
        { signal },
      );
      return data;
    },
  });
  const items = busqueda.data ?? [];

  // Etiqueta del trigger, por robustez: objeto guardado → item en página →
  // initialLabel del DTO → id (último recurso).
  const resuelto =
    selected && selected.id === value
      ? selected
      : value
        ? items.find((m) => m.id === value)
        : undefined;
  const triggerLabel = resuelto
    ? formatCcMaquinaLabel(resuelto)
    : value
      ? (initialLabel ?? value)
      : placeholder;

  function handleSelect(item: Dim3BusquedaItem) {
    const esDeseleccion = item.id === value;
    onChange(esDeseleccion ? null : item.id);
    setSelected(esDeseleccion ? null : item);
    if (!esDeseleccion) onSelect?.(item);
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
          aria-label={ariaLabel}
          aria-invalid={invalid || undefined}
          disabled={disabled}
          className={cn(
            'w-full justify-between font-normal',
            !value && 'text-muted-foreground',
            invalid && 'border-destructive ring-1 ring-destructive',
            className,
          )}
        >
          <span className="truncate">{triggerLabel}</span>
          <ChevronsUpDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
        </Button>
      </PopoverTrigger>
      <PopoverContent align="start" className="w-[min(36rem,90vw)] p-0">
        <Command shouldFilter={false}>
          <div className="flex items-center border-b px-3">
            <Search className="mr-2 h-4 w-4 shrink-0 opacity-50" />
            <input
              type="text"
              aria-label={`${ariaLabel} — buscar por clave o nombre`}
              placeholder={placeholder}
              value={input}
              onChange={(e) => setInput(e.target.value)}
              disabled={disabled}
              className="flex h-11 w-full rounded-md bg-transparent py-3 text-sm outline-none placeholder:text-muted-foreground disabled:cursor-not-allowed disabled:opacity-50"
            />
          </div>
          <CommandList>
            {busqueda.isLoading || busqueda.isFetching ? (
              <div
                className="flex items-center justify-center gap-2 py-6 text-sm text-muted-foreground"
                aria-live="polite"
              >
                <Loader2 className="h-4 w-4 animate-spin" />
                Buscando…
              </div>
            ) : busqueda.isError ? (
              <CatalogoQueryError error={busqueda.error} />
            ) : (
              <>
                <CommandEmpty>
                  {termino
                    ? `Ninguna máquina coincide con "${termino}".`
                    : 'Empieza a escribir la clave o el nombre de la máquina.'}
                </CommandEmpty>
                <CommandGroup>
                  {items.map((m) => (
                    <CommandItem
                      key={m.id}
                      value={m.id}
                      onSelect={() => handleSelect(m)}
                      className="flex items-start justify-between gap-3"
                    >
                      <div className="min-w-0 flex-1">
                        {/* L1: clave — nombre, mismo formato que el detalle/impresión
                            (formatCcMaquinaLabel). El picker solo recibe activas +
                            en alcance (BuscarDim3Query), así que no hay estado que pintar. */}
                        <p className="truncate text-sm">
                          {formatCcMaquinaLabel({
                            clave: m.clave,
                            nombre: m.nombre,
                          })}
                        </p>
                        {/* L2 (contexto, gris): Dim1 · Dim2 — desambiguación al elegir,
                            NO jerarquía del documento (CC-G4). Regla A: siempre, aunque
                            Dim2 = nombre (máquina administrativa); el tono lo lee como
                            contexto, no como duplicación. */}
                        <p className="truncate text-xs text-muted-foreground">
                          {m.dim1Nombre} · {m.dim2Nombre}
                        </p>
                      </div>
                      {m.id === value && (
                        <Check
                          className="mt-0.5 h-4 w-4 shrink-0"
                          aria-hidden="true"
                        />
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
