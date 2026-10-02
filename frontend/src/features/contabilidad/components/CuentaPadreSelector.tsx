import { useState } from 'react';
import { Check, ChevronsUpDown, Loader2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Command, CommandGroup, CommandInput, CommandItem, CommandList } from '@/components/ui/command';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { CatalogoQueryError } from '@/components/erp/selectors/CatalogoQueryError';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import { cn } from '@/lib/utils';
import { useCuentas } from '../api/hooks';
import type { Cuenta } from '../api/types';

type Opcion = Pick<Cuenta, 'id' | 'codigo' | 'nombre'>;

const SIN_PADRE = '(Sin padre — cuenta raíz)';

interface Props {
  /** Id del trigger: lo enlaza con el `<Label htmlFor>` del formulario. */
  id?: string;
  /** Id de la cuenta padre; '' = raíz. */
  value: string;
  onChange: (id: string) => void;
  /** Cuenta en edición: no puede ser su propio padre. */
  excluirId?: string;
  /** Padre actual al editar, para mostrar su etiqueta aunque no esté en la página de resultados. */
  padreActual?: Opcion | null;
  disabled?: boolean;
  title?: string;
}

/**
 * Combobox de cuenta padre: una sola caja de búsqueda (código o nombre) dentro del popover y la lista
 * de títulos activos. Mismo patrón que los selectores de `components/erp/selectors` (Popover + Command
 * con `shouldFilter=false`: el servidor filtra; el cliente solo pinta la página). El servidor revalida
 * padre, nivel y ciclos al guardar.
 */
export function CuentaPadreSelector({ id, value, onChange, excluirId, padreActual, disabled, title }: Props) {
  const [open, setOpen] = useState(false);
  const [busqueda, setBusqueda] = useState('');
  const [elegida, setElegida] = useState<Opcion | null>(null);
  const q = useDebouncedValue(busqueda.trim(), 200);

  const padres = useCuentas({ tipo: 'Titulo', estatus: 'Activo', q, limit: 50 });
  const items = (padres.data?.items ?? []).filter((p) => p.id !== excluirId);

  const resuelta =
    (elegida?.id === value && elegida) ||
    items.find((p) => p.id === value) ||
    (padreActual?.id === value ? padreActual : undefined);
  const etiqueta = !value ? SIN_PADRE : resuelta ? `${resuelta.codigo} — ${resuelta.nombre}` : 'Cuenta seleccionada';

  function elegir(opcion: Opcion | null) {
    onChange(opcion?.id ?? '');
    setElegida(opcion);
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
          title={title}
          className={cn('w-full justify-between font-normal', !value && 'text-ink-muted')}
        >
          <span className="truncate">{etiqueta}</span>
          <ChevronsUpDown className="ml-2 size-4 shrink-0 opacity-50" aria-hidden="true" />
        </Button>
      </PopoverTrigger>
      <PopoverContent align="start" className="w-[min(36rem,90vw)] p-0">
        <Command shouldFilter={false}>
          <CommandInput
            aria-label="Buscar cuenta padre"
            placeholder="Buscar título por código o nombre…"
            value={busqueda}
            onValueChange={setBusqueda}
          />
          <CommandList>
            <CommandGroup>
              <CommandItem value="__raiz__" onSelect={() => elegir(null)} className="justify-between">
                <span className="text-ink-secondary">{SIN_PADRE}</span>
                {!value && <Check className="size-4" aria-hidden="true" />}
              </CommandItem>
            </CommandGroup>
            {padres.isFetching ? (
              <div className="flex items-center justify-center gap-2 py-6 text-sm text-ink-muted" aria-live="polite">
                <Loader2 className="size-4 animate-spin" aria-hidden="true" />
                Buscando…
              </div>
            ) : padres.isError ? (
              <CatalogoQueryError error={padres.error} />
            ) : items.length === 0 ? (
              <p className="py-6 text-center text-sm text-ink-muted">
                {q ? `Ningún título activo coincide con «${q}».` : 'No hay cuentas de tipo título activas.'}
              </p>
            ) : (
              <CommandGroup heading="Títulos activos">
                {items.map((p) => (
                  <CommandItem key={p.id} value={p.id} onSelect={() => elegir(p)} className="justify-between gap-3">
                    <span className="min-w-0 truncate">
                      <span className="font-mono text-xs">{p.codigo}</span>
                      <span className="ml-2 text-sm">{p.nombre}</span>
                    </span>
                    {p.id === value && <Check className="size-4 shrink-0" aria-hidden="true" />}
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
