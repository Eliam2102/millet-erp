import { useState } from 'react';
import { Check, ChevronsUpDown, Loader2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Command, CommandGroup, CommandInput, CommandItem, CommandList } from '@/components/ui/command';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { CatalogoQueryError } from '@/components/erp/selectors/CatalogoQueryError';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import { cn } from '@/lib/utils';
import { useAuxiliares } from '../../api/dimensiones';
import type { Auxiliar, TipoAuxiliar } from '../../api/dimensiones-types';

const TEXTO: Record<TipoAuxiliar, { vacio: string; buscar: string; grupo: string }> = {
  Cliente: { vacio: 'Sin cliente', buscar: 'Clave, razón social o RFC…', grupo: 'Clientes activos' },
  Proveedor: { vacio: 'Sin proveedor', buscar: 'Clave, razón social o RFC…', grupo: 'Proveedores activos' },
  Banco: { vacio: 'Sin cuenta bancaria', buscar: 'Banco o número de cuenta…', grupo: 'Cuentas bancarias activas' },
};

interface Props {
  id: string;
  tipo: TipoAuxiliar;
  value: Auxiliar | null;
  onChange: (a: Auxiliar | null) => void;
  invalido?: boolean;
  describedBy?: string;
}

/** Cliente, proveedor o cuenta bancaria de la partida (dimensiones auxiliares de K10.2). La API valida al confirmar. */
export function AuxiliarSelector({ id, tipo, value, onChange, invalido, describedBy }: Props) {
  const [open, setOpen] = useState(false);
  const [busqueda, setBusqueda] = useState('');
  const q = useDebouncedValue(busqueda.trim(), 200);
  const lista = useAuxiliares(tipo, q, open);
  const items = lista.data ?? [];
  const t = TEXTO[tipo];

  function elegir(a: Auxiliar | null) {
    onChange(a);
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
          className={cn('w-full justify-between font-normal', !value && 'text-ink-muted', invalido && 'border-danger')}
        >
          <span className="truncate">{value ? <><span className="font-mono text-xs">{value.clave}</span> {value.nombre}</> : t.vacio}</span>
          <ChevronsUpDown className="ml-2 size-4 shrink-0 opacity-50" aria-hidden="true" />
        </Button>
      </PopoverTrigger>
      <PopoverContent align="start" className="w-[min(32rem,90vw)] p-0">
        <Command shouldFilter={false}>
          <CommandInput aria-label={`Buscar ${tipo.toLowerCase()}`} placeholder={t.buscar} value={busqueda} onValueChange={setBusqueda} />
          <CommandList>
            <CommandGroup>
              <CommandItem value="__ninguno__" onSelect={() => elegir(null)} className="justify-between">
                <span className="text-ink-secondary">{t.vacio}</span>
                {!value && <Check className="size-4" aria-hidden="true" />}
              </CommandItem>
            </CommandGroup>
            {lista.isFetching ? (
              <div className="flex items-center justify-center gap-2 py-6 text-sm text-ink-muted" aria-live="polite">
                <Loader2 className="size-4 animate-spin" aria-hidden="true" />Buscando…
              </div>
            ) : lista.isError ? (
              <CatalogoQueryError error={lista.error} />
            ) : items.length === 0 ? (
              <p className="px-3 py-6 text-center text-sm text-ink-muted">{q ? `Nada coincide con «${q}».` : 'No hay registros activos.'}</p>
            ) : (
              <CommandGroup heading={t.grupo}>
                {items.map((a) => (
                  <CommandItem key={a.id} value={a.id} onSelect={() => elegir(a)} className="justify-between gap-3">
                    <span className="min-w-0 truncate"><span className="font-mono text-xs">{a.clave}</span><span className="ml-2 text-sm">{a.nombre}</span></span>
                    {a.id === value?.id && <Check className="size-4 shrink-0" aria-hidden="true" />}
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
