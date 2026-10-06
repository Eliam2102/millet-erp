import { useState } from 'react';
import { Check, ChevronsUpDown, Loader2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Command, CommandGroup, CommandInput, CommandItem, CommandList } from '@/components/ui/command';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { CatalogoQueryError } from '@/components/erp/selectors/CatalogoQueryError';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import { cn } from '@/lib/utils';
import { useCuentas } from '../../api/hooks';
import type { Cuenta } from '../../api/types';

export type CuentaOpcion = Pick<Cuenta, 'id' | 'codigo' | 'nombre'>;

interface Props {
  id?: string;
  value: CuentaOpcion | null;
  onChange: (cuenta: CuentaOpcion | null) => void;
  /** Movimientos: solo cuentas afectables. Reglas: también ramas (cuentas que acumulan), que heredan a sus hijas. */
  soloAfectables?: boolean;
  /** Texto de la opción vacía; sin él la opción vacía no se ofrece. */
  vacio?: string;
  disabled?: boolean;
}

/** Combobox de cuenta activa (sin rubros). Mismo patrón que `CuentaPadreSelector`: el servidor filtra, el cliente pinta. */
export function CuentaSelector({ id, value, onChange, soloAfectables, vacio, disabled }: Props) {
  const [open, setOpen] = useState(false);
  const [busqueda, setBusqueda] = useState('');
  const q = useDebouncedValue(busqueda.trim(), 200);
  const cuentas = useCuentas({ clase: 'Cuenta', estatus: 'Activo', tipo: soloAfectables ? 'Afectable' : '', q, limit: 50 }, open);
  const items = cuentas.data?.items ?? [];

  function elegir(c: CuentaOpcion | null) {
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
          disabled={disabled}
          className={cn('w-full justify-between font-normal', !value && 'text-ink-muted')}
        >
          <span className="truncate">
            {value ? <><span className="font-mono text-xs">{value.codigo}</span> {value.nombre}</> : (vacio ?? 'Elegir cuenta')}
          </span>
          <ChevronsUpDown className="ml-2 size-4 shrink-0 opacity-50" aria-hidden="true" />
        </Button>
      </PopoverTrigger>
      <PopoverContent align="start" className="w-[min(36rem,90vw)] p-0">
        <Command shouldFilter={false}>
          <CommandInput aria-label="Buscar cuenta" placeholder="Buscar cuenta por código o nombre…" value={busqueda} onValueChange={setBusqueda} />
          <CommandList>
            {vacio && (
              <CommandGroup>
                <CommandItem value="__vacio__" onSelect={() => elegir(null)} className="justify-between">
                  <span className="text-ink-secondary">{vacio}</span>
                  {!value && <Check className="size-4" aria-hidden="true" />}
                </CommandItem>
              </CommandGroup>
            )}
            {cuentas.isFetching ? (
              <div className="flex items-center justify-center gap-2 py-6 text-sm text-ink-muted" aria-live="polite">
                <Loader2 className="size-4 animate-spin" aria-hidden="true" />Buscando…
              </div>
            ) : cuentas.isError ? (
              <CatalogoQueryError error={cuentas.error} />
            ) : items.length === 0 ? (
              <p className="py-6 text-center text-sm text-ink-muted">
                {q ? `Ninguna cuenta activa coincide con «${q}».` : 'No hay cuentas activas.'}
              </p>
            ) : (
              <CommandGroup heading={soloAfectables ? 'Cuentas que reciben movimientos' : 'Cuentas y ramas activas'}>
                {items.map((c) => (
                  <CommandItem key={c.id} value={c.id} onSelect={() => elegir(c)} className="justify-between gap-3">
                    <span className="min-w-0 truncate">
                      <span className="font-mono text-xs">{c.codigo}</span>
                      <span className="ml-2 text-sm">{c.nombre}</span>
                      {c.tipo === 'Titulo' && <span className="ml-2 text-xs text-ink-muted">(rama)</span>}
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
