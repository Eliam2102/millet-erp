import { ChevronLeft, ChevronRight, ChevronsLeft, ChevronsRight } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { Button } from '@/components/ui/button';
import { TAMANOS_PAGINA, useTamanoPagina } from './useTamanoPagina';

export interface PaginacionTablaProps {
  total: number;
  offset: number;
  limit: number;
  onChange: (offset: number, limit: number) => void;
  /** Opciones de tamaño de página. Vacío oculta el selector. */
  tamanos?: number[];
  /** Agrega «Ir al inicio» / «Ir al final» junto a Anterior/Siguiente. */
  extremos?: boolean;
  regionId?: string;
}

export function PaginacionTabla({
  total,
  offset,
  limit,
  onChange,
  tamanos = TAMANOS_PAGINA,
  extremos = true,
  regionId,
}: PaginacionTablaProps) {
  const preferencia = useTamanoPagina('principal', 100, 10, regionId);
  const aplicado = useRef<number | null>(null);
  const limiteAnterior = useRef(limit);
  const salto = useRef<HTMLInputElement>(null);
  const [respetarUrl, setRespetarUrl] = useState(() => {
    const url = new URLSearchParams(window.location.search);
    return url.has('pageSize') || url.has('limit');
  });
  const cantidad = Math.max(1, limit);
  const ultima = Math.max(0, Math.ceil(total / cantidad) - 1) * cantidad;
  const posicion = Math.max(0, Math.min(offset, ultima));
  const paginas = Math.max(1, Math.ceil(total / cantidad));
  const pagina = Math.floor(posicion / cantidad) + 1;
  const opciones = tamanos.includes(limit) ? tamanos : [...tamanos, limit].sort((a, b) => a - b);
  useEffect(() => {
    if (limiteAnterior.current === limit) return;
    limiteAnterior.current = limit;
    if (offset > 0) onChange(0, limit);
  }, [limit, offset, onChange]);
  useEffect(() => {
    if (tamanos.length === 0 || respetarUrl || preferencia.editando) return;
    if (limit !== preferencia.limit && aplicado.current !== preferencia.limit) {
      aplicado.current = preferencia.limit;
      onChange(0, preferencia.limit);
    }
  }, [limit, onChange, preferencia.limit, preferencia.editando, tamanos.length, respetarUrl]);
  useEffect(() => {
    if (total > 0 && offset > ultima) onChange(ultima, cantidad);
  }, [total, offset, ultima, cantidad, onChange]);
  const ir = () => {
    const destino = Number(salto.current?.value);
    if (Number.isInteger(destino) && destino >= 1 && destino <= paginas)
      onChange((destino - 1) * cantidad, cantidad);
  };
  const control = 'min-h-11 min-w-11 px-2 md:min-h-8 md:min-w-8';

  return (
    <nav
      aria-label="Paginación"
      data-print="hidden"
      className="flex min-w-0 shrink-0 flex-col gap-2 border-t border-line-divider bg-surface-card px-3 py-2 text-xs text-ink-muted md:flex-row md:flex-wrap md:items-center md:justify-between"
    >
      <fieldset disabled={preferencia.editando} className="contents">
      <div className="flex min-w-0 items-center justify-between gap-3">
        <span className="shrink-0 tabular-nums">
          {total === 0
            ? '0 de 0'
            : `${posicion + 1}–${Math.min(posicion + cantidad, total)} de ${total}`}
        </span>
        {tamanos.length > 0 && (
          <label className="flex items-center gap-1.5">
            <span>
              Filas<span className="hidden md:inline"> por página</span>
            </span>
            <select
              aria-label="Filas por página"
              value={!respetarUrl && preferencia.modo === 'auto' ? 'auto' : limit}
              className="min-h-11 max-w-32 rounded-md border border-line-control bg-surface-card px-1.5 text-xs text-ink md:min-h-8"
              onChange={(e) => {
                setRespetarUrl(false);
                const valor = e.target.value === 'auto' ? 'auto' : Number(e.target.value);
                onChange(0, valor === 'auto' ? preferencia.limit : valor);
                preferencia.elegir(valor);
              }}
            >
              <option value="auto">Automático</option>
              {opciones.map((n) => (
                <option key={n} value={n}>
                  {n}
                </option>
              ))}
            </select>
          </label>
        )}
      </div>
      <div className="flex min-w-0 items-center justify-between gap-1 md:justify-end md:gap-2">
        {extremos && (
          <Button
            type="button"
            variant="secondary"
            size="sm"
            className={`${control} hidden sm:inline-flex`}
            disabled={posicion === 0}
            aria-label="Ir al inicio"
            title="Ir al inicio"
            onClick={() => onChange(0, cantidad)}
          >
            <ChevronsLeft aria-hidden className="size-3.5" />
          </Button>
        )}
        <Button
          type="button"
          variant="secondary"
          size="sm"
          className={control}
          disabled={posicion === 0}
          aria-label="Página anterior"
          title="Página anterior"
          onClick={() => onChange(Math.max(0, posicion - cantidad), cantidad)}
        >
          <ChevronLeft aria-hidden className="size-3.5 md:hidden" />
          <span className="hidden md:inline">Anterior</span>
        </Button>
        <div className="flex min-w-0 items-center gap-1.5">
          <label className="flex items-center gap-1.5">
            <span className="sr-only md:not-sr-only">Página</span>
            <input
              ref={salto}
              key={pagina}
              aria-label="Ir a página"
              type="number"
              min={1}
              max={paginas}
              defaultValue={pagina}
              className="min-h-11 w-12 rounded-md border border-line-control bg-surface-card px-1 text-center tabular-nums text-ink md:min-h-8 md:w-14"
              onKeyDown={(e) => {
                if (e.key === 'Enter') {
                  e.preventDefault();
                  ir();
                }
              }}
            />
          </label>
          <span className="shrink-0 tabular-nums">de {paginas}</span>
          <Button
            type="button"
            size="sm"
            variant="secondary"
            className={control}
            disabled={total === 0}
            onClick={ir}
          >
            Ir
          </Button>
        </div>
        <Button
          type="button"
          variant="secondary"
          size="sm"
          className={control}
          disabled={posicion >= ultima}
          aria-label="Página siguiente"
          title="Página siguiente"
          onClick={() => onChange(Math.min(ultima, posicion + cantidad), cantidad)}
        >
          <span className="hidden md:inline">Siguiente</span>
          <ChevronRight aria-hidden className="size-3.5 md:hidden" />
        </Button>
        {extremos && (
          <Button
            type="button"
            variant="secondary"
            size="sm"
            className={`${control} hidden sm:inline-flex`}
            disabled={posicion >= ultima}
            aria-label="Ir al final"
            title="Ir al final"
            onClick={() => onChange(ultima, cantidad)}
          >
            <ChevronsRight aria-hidden className="size-3.5" />
          </Button>
        )}
      </div>
      </fieldset>
      {preferencia.editando && <span className="text-xs text-ink-muted">Finaliza o cancela la edición para cambiar de página.</span>}
    </nav>
  );
}
