import { useState } from 'react';
import { Check, ChevronsUpDown, Loader2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Command, CommandGroup, CommandInput, CommandItem, CommandList } from '@/components/ui/command';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { CatalogoQueryError } from '@/components/erp/selectors/CatalogoQueryError';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import { cn } from '@/lib/utils';
import { useCentrosParaMovimiento } from '../../api/dimensiones';
import type { CentroOpcion, Dimension } from '../../api/dimensiones-types';
import { ETIQUETA_DIMENSION } from '../../lib/dimensiones';

interface Props {
  id: string;
  nivel: Dimension;
  sucursalId: string;
  /** Restringe los equipos (Dim3) al CeCo elegido. */
  dim2Id?: string;
  value: CentroOpcion | null;
  onChange: (c: CentroOpcion | null) => void;
  invalido?: boolean;
  describedBy?: string;
}

/**
 * Selector de centro de costo para capturar un movimiento: solo ofrece centros activos asignados a la sucursal elegida
 * (un centro de otra sucursal no aparece). La API vuelve a validar al confirmar.
 */
export function CentroSelector({ id, nivel, sucursalId, dim2Id, value, onChange, invalido, describedBy }: Props) {
  const [open, setOpen] = useState(false);
  const [busqueda, setBusqueda] = useState('');
  const q = useDebouncedValue(busqueda.trim(), 200);
  const centros = useCentrosParaMovimiento(sucursalId, nivel, dim2Id ?? '', q, open);
  const items = centros.data ?? [];
  const sinSucursal = !sucursalId;

  function elegir(c: CentroOpcion | null) {
    onChange(c);
    setOpen(false);
    setBusqueda('');
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
          aria-invalid={invalido || undefined}
          aria-describedby={describedBy}
          disabled={sinSucursal}
          title={sinSucursal ? 'Elija primero la sucursal: solo se ofrecen sus centros' : undefined}
          className={cn('w-full justify-between font-normal', !value && 'text-ink-muted', invalido && 'border-danger')}
        >
          <span className="truncate">
            {value ? <><span className="font-mono text-xs">{value.clave}</span> {value.nombre}</> : `Sin ${ETIQUETA_DIMENSION[nivel].toLowerCase()}`}
          </span>
          <ChevronsUpDown className="ml-2 size-4 shrink-0 opacity-50" aria-hidden="true" />
        </Button>
      </PopoverTrigger>
      <PopoverContent align="start" className="w-[min(32rem,90vw)] p-0">
        <Command shouldFilter={false}>
          <CommandInput aria-label={`Buscar ${ETIQUETA_DIMENSION[nivel]}`} placeholder="Buscar por clave o nombre…" value={busqueda} onValueChange={setBusqueda} />
          <CommandList>
            <CommandGroup>
              <CommandItem value="__ninguno__" onSelect={() => elegir(null)} className="justify-between">
                <span className="text-ink-secondary">Sin {ETIQUETA_DIMENSION[nivel].toLowerCase()}</span>
                {!value && <Check className="size-4" aria-hidden="true" />}
              </CommandItem>
            </CommandGroup>
            {centros.isFetching ? (
              <div className="flex items-center justify-center gap-2 py-6 text-sm text-ink-muted" aria-live="polite">
                <Loader2 className="size-4 animate-spin" aria-hidden="true" />Buscando…
              </div>
            ) : centros.isError ? (
              <CatalogoQueryError error={centros.error} />
            ) : items.length === 0 ? (
              <p className="px-3 py-6 text-center text-sm text-ink-muted">
                {q ? `Ningún centro de esta sucursal coincide con «${q}».` : 'Esta sucursal no tiene centros de este nivel asignados.'}
              </p>
            ) : (
              <CommandGroup heading="Centros de la sucursal">
                {items.map((c) => (
                  <CommandItem key={c.id} value={c.id} onSelect={() => elegir(c)} className="justify-between gap-3">
                    <span className="min-w-0 truncate">
                      <span className="font-mono text-xs">{c.clave}</span>
                      <span className="ml-2 text-sm">{c.nombre}</span>
                      {c.nivel === 'Dim3' && c.dim2Clave && <span className="ml-2 text-xs text-ink-muted">CeCo {c.dim2Clave}</span>}
                    </span>
                    {c.id === value?.id && <Check className="size-4 shrink-0" aria-hidden="true" />}
                  </CommandItem>
                ))}
              </CommandGroup>
            )}
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}
