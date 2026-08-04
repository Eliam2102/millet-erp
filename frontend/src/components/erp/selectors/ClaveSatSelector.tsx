import { useState } from 'react';
import { Check, ChevronsUpDown, Loader2, Search, TriangleAlert } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
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
import {
  esCatalogoSatNoDisponible,
  useCatalogoSatSearch,
  type CatalogoSatItem,
  type CatalogoSatVivo,
} from '@/modules/catalogos/api/sat-fiscalapi';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import { cn } from '@/lib/utils';
import { CatalogoQueryError } from './CatalogoQueryError';

/**
 * <c>&lt;ClaveSatSelector/&gt;</c> — combobox genérico de catálogos SAT en
 * vivo vía FiscalAPI (FAC-DET-PR3): c_ClaveProdServ, c_ClaveUnidad y
 * c_ObjetoImp. Lazy single-search (patrón <c>ProductoAwSelector</c>,
 * ADR-0045), debounce 300 ms, render código mono + descripción.
 * Reusable cross-módulo — el value es el CÓDIGO SAT (string).
 *
 * <para><b>Modo degradado (503)</b>: si el PAC no responde
 * (<c>CATALOGO_SAT_NO_DISPONIBLE</c>) el popover ofrece captura manual del
 * código (+ <c>fallbackItems</c> si el catálogo chico se conoce local,
 * p.ej. c_ObjetoImp 01–03) — el flujo de captura nunca se bloquea.</para>
 */
export interface ClaveSatSelectorProps {
  catalogo: CatalogoSatVivo;
  /** Código SAT seleccionado (p.ej. '43211701', 'MTK', '02') o null. */
  value: string | null | undefined;
  onChange: (item: CatalogoSatItem | null) => void;
  /** Etiqueta para el cold value (código ya guardado sin descripción). */
  initialLabel?: string | null;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
  /** Opciones locales ofrecidas junto a la captura manual en modo 503. */
  fallbackItems?: CatalogoSatItem[];
}

export function ClaveSatSelector({
  catalogo,
  value,
  onChange,
  initialLabel,
  placeholder = 'Buscar clave SAT…',
  disabled,
  className,
  fallbackItems,
}: ClaveSatSelectorProps) {
  const [open, setOpen] = useState(false);
  const [input, setInput] = useState('');
  const [manual, setManual] = useState('');
  const [selected, setSelected] = useState<CatalogoSatItem | null>(null);

  const buscar = useDebouncedValue(input, 300);
  const query = useCatalogoSatSearch(catalogo, buscar, { enabled: open });
  const items = query.data ?? [];
  const degradado = query.isError && esCatalogoSatNoDisponible(query.error);

  const resuelto =
    selected && selected.codigo === value
      ? selected
      : value
        ? items.find((i) => i.codigo === value)
        : undefined;

  const triggerLabel = resuelto
    ? `${resuelto.codigo} · ${resuelto.descripcion}`
    : value
      ? initialLabel ?? value
      : placeholder;

  function handleSelect(item: CatalogoSatItem) {
    const esDeseleccion = item.codigo === value;
    onChange(esDeseleccion ? null : item);
    setSelected(esDeseleccion ? null : item);
    setOpen(false);
    setInput('');
    setManual('');
  }

  function handleManual() {
    const codigo = manual.trim().toUpperCase();
    if (codigo.length === 0) return;
    handleSelect({ codigo, descripcion: '' });
  }

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          type="button"
          variant="outline"
          role="combobox"
          aria-expanded={open}
          aria-label={`Seleccionar clave SAT (${catalogo})`}
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
      <PopoverContent align="start" className="w-[min(34rem,90vw)] p-0">
        <Command shouldFilter={false}>
          <div className="flex items-center border-b px-3">
            <Search className="mr-2 h-4 w-4 shrink-0 opacity-50" />
            <input
              type="text"
              aria-label="Buscar clave SAT por código o descripción"
              placeholder="Código exacto (1–3 chars) o descripción (4+)…"
              value={input}
              onChange={(e) => setInput(e.target.value)}
              disabled={disabled}
              className="flex h-11 w-full rounded-md bg-transparent py-3 text-sm outline-none placeholder:text-muted-foreground disabled:cursor-not-allowed disabled:opacity-50"
            />
          </div>
          <CommandList>
            {degradado ? (
              <div className="space-y-2 p-3">
                <p
                  role="alert"
                  className="flex items-start gap-2 text-xs text-amber-700"
                >
                  <TriangleAlert className="mt-0.5 h-4 w-4 shrink-0" />
                  Catálogo SAT no disponible (el servicio del PAC no
                  responde). Captura el código manualmente.
                </p>
                {fallbackItems && fallbackItems.length > 0 && (
                  <div className="space-y-1">
                    {fallbackItems.map((f) => (
                      <button
                        key={f.codigo}
                        type="button"
                        onClick={() => handleSelect(f)}
                        className="flex w-full items-center gap-2 rounded px-2 py-1.5 text-left text-sm hover:bg-accent"
                      >
                        <span className="font-mono text-xs">{f.codigo}</span>
                        <span className="truncate text-muted-foreground">
                          {f.descripcion}
                        </span>
                        {f.codigo === value && (
                          <Check className="ml-auto h-4 w-4 shrink-0" />
                        )}
                      </button>
                    ))}
                  </div>
                )}
                <div className="flex items-center gap-2">
                  <Input
                    aria-label="Código SAT manual"
                    placeholder="Código SAT"
                    className="h-8 font-mono uppercase"
                    value={manual}
                    onChange={(e) => setManual(e.target.value)}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter') {
                        e.preventDefault();
                        handleManual();
                      }
                    }}
                  />
                  <Button type="button" size="sm" onClick={handleManual}>
                    Usar código
                  </Button>
                </div>
              </div>
            ) : query.isLoading || query.isFetching ? (
              <div
                className="flex items-center justify-center gap-2 py-6 text-sm text-muted-foreground"
                aria-live="polite"
              >
                <Loader2 className="h-4 w-4 animate-spin" />
                Buscando…
              </div>
            ) : query.isError ? (
              <CatalogoQueryError error={query.error} />
            ) : (
              <>
                <CommandEmpty>
                  {buscar.trim().length === 0
                    ? catalogo === 'objeto-imp'
                      ? 'Sin entradas.'
                      : 'Escribe el código exacto o parte de la descripción.'
                    : buscar.trim().length < 4
                      ? catalogo === 'clave-prod-serv'
                        ? 'Sigue escribiendo — la búsqueda por descripción necesita al menos 4 caracteres (las claves son de 8 dígitos).'
                        : `"${buscar.trim()}" no coincide con un código exacto — para buscar por descripción escribe al menos 4 caracteres.`
                      : `Sin coincidencias para "${buscar.trim()}".`}
                </CommandEmpty>
                <CommandGroup>
                  {items.map((i) => (
                    <CommandItem
                      key={i.codigo}
                      value={i.codigo}
                      onSelect={() => handleSelect(i)}
                      className="flex items-center justify-between gap-3"
                    >
                      <div className="flex min-w-0 items-center gap-2">
                        <span className="shrink-0 font-mono text-xs">
                          {i.codigo}
                        </span>
                        <span className="truncate text-sm">{i.descripcion}</span>
                      </div>
                      {i.codigo === value && (
                        <Check className="h-4 w-4 shrink-0" aria-hidden="true" />
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
