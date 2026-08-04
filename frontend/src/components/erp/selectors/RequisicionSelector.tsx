import { useMemo, useState } from 'react';
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
import { useRequisiciones } from '@/features/compras/api/useRequisiciones';
import {
  EstadoRequisicion,
  estadoToString,
  type RequisicionListItemResponse,
} from '@/features/compras/api/types';
import { cn } from '@/lib/utils';
import { CatalogoQueryError } from './CatalogoQueryError';

/**
 * <c>&lt;RequisicionSelector/&gt;</c> — combobox para seleccionar una
 * Requisición. Mismo patrón que <c>&lt;OrdenCompraSelector/&gt;</c>:
 * carga las últimas N RQs en los estados pedidos y filtra
 * client-side por folio/descripción.
 *
 * <para>Default: estados <c>Autorizada</c> y <c>EnSurtido</c> — los
 * únicos con líneas pendientes de <b>entregar</b> al solicitante:</para>
 * <list>
 *   <item><b>Autorizada</b>: recién aprobada, caso transitorio antes
 *     del cubrimiento (degenerado — el cubrimiento mueve a EnSurtido).</item>
 *   <item><b>EnSurtido</b>: estado de surtido; hay stock reservado
 *     (CantDeAlmacen) y/o material recibido por OC listo para entregar.
 *     Es el estado donde viven las entregas al solicitante.</item>
 * </list>
 *
 * <para><b>ADR-0043 #3 (conmutación):</b> <c>Cerrada</c> ya NO se
 * ofrece por default. Con el cierre por entrega encendido, <c>Cerrada</c>
 * significa "todo entregado al solicitante" (R1) — terminal, sin pendiente.
 * Una RQ en surtido permanece <c>EnSurtido</c> hasta que se entrega
 * completa. (La maquinaria por estado sigue siendo prop-driven: un caller
 * puede pasar <c>[Cerrada]</c> explícito si necesita listarlas.)</para>
 *
 * <para>Pasa <c>estados=null</c> para no filtrar.</para>
 *
 * <para>Reusable cross-módulo: Almacén lo consume al registrar salidas
 * con RQ; potencialmente Compras OC al consolidar RQs (futuro).</para>
 */
export interface RequisicionSelectorProps {
  value: string | null | undefined;
  onChange: (id: string | null) => void;
  /**
   * Estados de RQ a filtrar (OR). Default: <c>[Autorizada, EnSurtido]</c>.
   * Pasa <c>null</c> para no filtrar.
   */
  estados?: EstadoRequisicion[] | null;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
}

const DEFAULT_ESTADOS = [
  EstadoRequisicion.Autorizada,
  EstadoRequisicion.EnSurtido,
];

export function RequisicionSelector({
  value,
  onChange,
  estados = DEFAULT_ESTADOS,
  placeholder = 'Buscar RQ por folio…',
  disabled,
  className,
}: RequisicionSelectorProps) {
  const [open, setOpen] = useState(false);
  const [input, setInput] = useState('');

  // Backend solo acepta UN estado a la vez. Si se pasa array, hacemos
  // queries paralelas y mergeamos. Para el caso default (3 estados) son
  // 3 round-trips livianos.
  const estadosArr = useMemo(() => estados ?? [], [estados]);

  const queryAutorizada = useRequisiciones(
    estadosArr.includes(EstadoRequisicion.Autorizada)
      ? { estado: EstadoRequisicion.Autorizada, limit: 200 }
      : { limit: 0 },
  );
  const queryEnSurtido = useRequisiciones(
    estadosArr.includes(EstadoRequisicion.EnSurtido)
      ? { estado: EstadoRequisicion.EnSurtido, limit: 200 }
      : { limit: 0 },
  );
  const queryCerrada = useRequisiciones(
    estadosArr.includes(EstadoRequisicion.Cerrada)
      ? { estado: EstadoRequisicion.Cerrada, limit: 200 }
      : { limit: 0 },
  );

  const items = useMemo(() => {
    const merged: RequisicionListItemResponse[] = [];
    if (estadosArr.includes(EstadoRequisicion.Autorizada)) {
      merged.push(...(queryAutorizada.data?.items ?? []));
    }
    if (estadosArr.includes(EstadoRequisicion.EnSurtido)) {
      merged.push(...(queryEnSurtido.data?.items ?? []));
    }
    if (estadosArr.includes(EstadoRequisicion.Cerrada)) {
      merged.push(...(queryCerrada.data?.items ?? []));
    }
    return merged;
  }, [
    estadosArr,
    queryAutorizada.data,
    queryEnSurtido.data,
    queryCerrada.data,
  ]);

  const isLoading =
    queryAutorizada.isLoading ||
    queryEnSurtido.isLoading ||
    queryCerrada.isLoading;

  const queryError =
    queryAutorizada.error ?? queryEnSurtido.error ?? queryCerrada.error;

  const seleccionada = value ? items.find((r) => r.id === value) : undefined;
  const triggerLabel = seleccionada
    ? `${seleccionada.folio} · ${estadoToString(seleccionada.estado)}`
    : value
      ? value.length > 18
        ? `${value.slice(0, 8)}…`
        : value
      : placeholder;

  function handleSelect(item: RequisicionListItemResponse) {
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
          aria-label="Seleccionar requisición"
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
      <PopoverContent align="start" className="w-[min(36rem,90vw)] p-0">
        <Command>
          <CommandInput
            placeholder="Folio o ID…"
            value={input}
            onValueChange={setInput}
          />
          <CommandList>
            {isLoading ? (
              <div
                className="flex items-center justify-center gap-2 py-6 text-sm text-muted-foreground"
                aria-live="polite"
              >
                <Loader2 className="h-4 w-4 animate-spin" />
                Cargando requisiciones…
              </div>
            ) : queryError != null ? (
              <CatalogoQueryError error={queryError} />
            ) : (
              <>
                <CommandEmpty>
                  {items.length === 0
                    ? 'No hay RQs disponibles en estos estados.'
                    : 'No hay coincidencias.'}
                </CommandEmpty>
                <CommandGroup>
                  {items.map((r) => {
                    const valueParaFilter = `${r.folio} ${r.id}`;
                    return (
                      <CommandItem
                        key={r.id}
                        value={valueParaFilter}
                        onSelect={() => handleSelect(r)}
                        className="flex items-start justify-between gap-3"
                      >
                        <div className="min-w-0 flex-1">
                          <div className="flex items-center gap-2">
                            <span className="truncate font-mono text-xs">
                              {r.folio}
                            </span>
                            <span className="rounded bg-muted px-1.5 py-0.5 text-[10px] font-medium text-muted-foreground">
                              {estadoToString(r.estado)}
                            </span>
                          </div>
                        </div>
                        {r.id === value && (
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
