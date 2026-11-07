import { useEffect, useId, useRef, useState } from 'react';
import { Check, ChevronsUpDown, Loader2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  Command,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from '@/components/ui/command';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import { Field, type FieldControlProps } from '@/components/erp';
import { CatalogoQueryError } from '@/components/erp/selectors/CatalogoQueryError';
import { useDebouncedValue } from '@/lib/hooks/useDebouncedValue';
import { cn } from '@/lib/utils';
import {
  useCentrosGasto,
  type CentroGastoOpcion,
  type DimensionesGasto,
  type NivelCentroGasto,
} from '@/features/cxp/api/useDimensionesGasto';

/**
 * H1 · Área (Dim2) y Equipo (Dim3) de un gasto de CxP (caja chica, viáticos, tarjeta). El formulario guarda
 * un solo id, `centroCostoId`: el equipo si se eligió; si no, el área. El área y el equipo viven en estado
 * local; elegir un equipo directo infiere su área (`dim2Id`).
 *
 * <para>`dimensiones` lo resuelve el formulario una sola vez con `useDimensionesGasto` (la sucursal sale de la
 * comprobación o del empleado) y se comparte entre las líneas.</para>
 */
export interface AreaEquipoGastoProps {
  /** Identificador de la línea (los ids de los controles los genera `Field`, que liga cada label). */
  id: string;
  /** Respuesta de `useDimensionesGasto`; undefined mientras no hay sucursal/empleado o está cargando. */
  dimensiones: DimensionesGasto | undefined;
  /** Texto cuando aún no hay de dónde sacar la sucursal (p. ej. «Elige primero la sucursal»). */
  sinOrigenMensaje?: string;
  value: string | null | undefined;
  onChange: (id: string | null, nivel: NivelCentroGasto | null) => void;
  error?: string;
}

export function AreaEquipoGasto({
  dimensiones,
  sinOrigenMensaje = 'Elige primero la sucursal o el empleado.',
  value,
  onChange,
  error,
}: AreaEquipoGastoProps) {
  const [area, setArea] = useState<CentroGastoOpcion | null>(null);
  const [equipo, setEquipo] = useState<CentroGastoOpcion | null>(null);
  const sucursalId = dimensiones?.sucursalId ?? '';
  const exigeArea = !!dimensiones?.exigeArea;
  const exigeEquipo = !!dimensiones?.exigeEquipo;

  // Si cambia la sucursal (otra comprobación/empleado), los centros elegidos ya no aplican.
  const sucursalPrevia = useRef(sucursalId);
  useEffect(() => {
    const previa = sucursalPrevia.current;
    sucursalPrevia.current = sucursalId;
    if (previa && previa !== sucursalId) {
      setArea(null);
      setEquipo(null);
      onChange(null, null);
    }
  }, [sucursalId, onChange]);

  if (dimensiones && !dimensiones.sucursalId) {
    return (
      <p className="text-xs text-ink-muted">
        El empleado no tiene sucursal: el gasto no se puede dimensionar (área/equipo).
      </p>
    );
  }

  // Id precargado (p. ej. al reeditar) sin saber si es área o equipo: se muestra como capturado.
  const precargado = !!value && !area && !equipo;

  function elegirArea(c: CentroGastoOpcion | null) {
    setArea(c);
    const conservaEquipo = equipo && c && equipo.dim2Id === c.id ? equipo : null;
    setEquipo(conservaEquipo);
    if (conservaEquipo) onChange(conservaEquipo.id, 3);
    else onChange(c?.id ?? null, c ? 2 : null);
  }

  function elegirEquipo(c: CentroGastoOpcion | null) {
    setEquipo(c);
    if (c) {
      if (c.dim2Id && area?.id !== c.dim2Id) {
        setArea({
          id: c.dim2Id,
          nivel: 2,
          clave: c.dim2Clave ?? '',
          nombre: '',
          dim2Id: null,
          dim2Clave: null,
        });
      }
      onChange(c.id, 3);
    } else {
      onChange(area?.id ?? null, area ? 2 : null);
    }
  }

  const cuenta = dimensiones?.cuentaCodigo ? `La cuenta ${dimensiones.cuentaCodigo}` : 'La cuenta del gasto';
  const exige = exigeEquipo ? 'área y equipo' : exigeArea ? 'área' : null;
  const sinOrigen = !dimensiones;

  return (
    <div className="space-y-1.5">
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <Field label="Área" required={exigeArea || exigeEquipo} optional={!exigeArea && !exigeEquipo}>
          {(control) => (
            <CentroGastoSelector
              control={control}
              nivel={2}
              sucursalId={sucursalId}
              value={area}
              placeholderValor={precargado ? 'Centro capturado' : null}
              onChange={elegirArea}
              invalido={!!error && !value}
              deshabilitadoMotivo={sinOrigen ? sinOrigenMensaje : null}
            />
          )}
        </Field>
        <Field label="Equipo" required={exigeEquipo} optional={!exigeEquipo}>
          {(control) => (
            <CentroGastoSelector
              control={control}
              nivel={3}
              sucursalId={sucursalId}
              dim2Id={area?.id}
              value={equipo}
              placeholderValor={null}
              onChange={elegirEquipo}
              invalido={!!error && exigeEquipo && !equipo}
              deshabilitadoMotivo={sinOrigen ? sinOrigenMensaje : null}
            />
          )}
        </Field>
      </div>
      {exige && (
        <p className="text-xs text-ink-muted">
          {cuenta} exige {exige} (regla de dimensiones C-03).
        </p>
      )}
      {error && (
        <p role="alert" className="text-xs text-danger-fg">
          {error}
        </p>
      )}
    </div>
  );
}

interface CentroGastoSelectorProps {
  control: FieldControlProps;
  nivel: NivelCentroGasto;
  sucursalId: string;
  dim2Id?: string;
  value: CentroGastoOpcion | null;
  /** Texto a mostrar cuando hay un id precargado sin etiqueta. */
  placeholderValor: string | null;
  onChange: (c: CentroGastoOpcion | null) => void;
  invalido?: boolean;
  deshabilitadoMotivo: string | null;
}

function CentroGastoSelector({
  control,
  nivel,
  sucursalId,
  dim2Id,
  value,
  placeholderValor,
  onChange,
  invalido,
  deshabilitadoMotivo,
}: CentroGastoSelectorProps) {
  const [open, setOpen] = useState(false);
  const [busqueda, setBusqueda] = useState('');
  const q = useDebouncedValue(busqueda.trim(), 200);
  const centros = useCentrosGasto(sucursalId, nivel, dim2Id ?? '', q, open);
  const items = centros.data ?? [];
  const etiqueta = nivel === 2 ? 'área' : 'equipo';
  const listaId = useId();

  function elegir(c: CentroGastoOpcion | null) {
    onChange(c);
    setOpen(false);
    setBusqueda('');
  }

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          {...control}
          type="button"
          variant="outline"
          role="combobox"
          aria-expanded={open}
          aria-controls={listaId}
          aria-invalid={invalido || control['aria-invalid'] || undefined}
          disabled={!!deshabilitadoMotivo}
          title={deshabilitadoMotivo ?? undefined}
          className={cn(
            'w-full justify-between font-normal',
            !value && 'text-ink-muted',
            invalido && 'border-danger',
          )}
        >
          <span className="truncate">
            {value ? (
              <>
                <span className="font-mono text-xs">{value.clave}</span>
                {value.nombre && <> {value.nombre}</>}
              </>
            ) : (
              (placeholderValor ?? `Sin ${etiqueta}`)
            )}
          </span>
          <ChevronsUpDown className="ml-2 size-4 shrink-0 opacity-50" aria-hidden="true" />
        </Button>
      </PopoverTrigger>
      <PopoverContent align="start" className="w-[min(32rem,90vw)] p-0">
        <Command shouldFilter={false}>
          <CommandInput
            aria-label={`Buscar ${etiqueta}`}
            placeholder="Buscar por clave o nombre…"
            value={busqueda}
            onValueChange={setBusqueda}
          />
          <CommandList id={listaId}>
            <CommandGroup>
              <CommandItem value="__ninguno__" onSelect={() => elegir(null)} className="justify-between">
                <span className="text-ink-secondary">Sin {etiqueta}</span>
                {!value && <Check className="size-4" aria-hidden="true" />}
              </CommandItem>
            </CommandGroup>
            {centros.isFetching ? (
              <div
                className="flex items-center justify-center gap-2 py-6 text-sm text-ink-muted"
                aria-live="polite"
              >
                <Loader2 className="size-4 animate-spin" aria-hidden="true" />
                Buscando…
              </div>
            ) : centros.isError ? (
              <CatalogoQueryError error={centros.error} />
            ) : items.length === 0 ? (
              <p className="px-3 py-6 text-center text-sm text-ink-muted">
                {q
                  ? `Ningún ${etiqueta} de la sucursal coincide con «${q}».`
                  : nivel === 3 && dim2Id
                    ? 'El área elegida no tiene equipos en esta sucursal.'
                    : `La sucursal no tiene ${nivel === 2 ? 'áreas' : 'equipos'} asignados.`}
              </p>
            ) : (
              <CommandGroup heading={nivel === 2 ? 'Áreas de la sucursal' : 'Equipos de la sucursal'}>
                {items.map((c) => (
                  <CommandItem
                    key={c.id}
                    value={c.id}
                    onSelect={() => elegir(c)}
                    className="justify-between gap-3"
                  >
                    <span className="min-w-0 truncate">
                      <span className="font-mono text-xs">{c.clave}</span>
                      <span className="ml-2 text-sm">{c.nombre}</span>
                      {c.nivel === 3 && c.dim2Clave && (
                        <span className="ml-2 text-xs text-ink-muted">Área {c.dim2Clave}</span>
                      )}
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
