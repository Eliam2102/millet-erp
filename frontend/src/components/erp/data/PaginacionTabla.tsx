import { ChevronsLeft, ChevronsRight } from 'lucide-react';
import { Button } from '@/components/ui/button';

export interface PaginacionTablaProps {
  total: number;
  offset: number;
  limit: number;
  onChange: (offset: number, limit: number) => void;
  /** Opciones de tamaño de página. Vacío oculta el selector. */
  tamanos?: number[];
  /** Agrega «Ir al inicio» / «Ir al final» junto a Anterior/Siguiente. */
  extremos?: boolean;
}

const TAMANOS_DEFAULT = [10, 25, 50];

/** Footer de tabla DS 4.4: «1–25 de 214» + selector de tamaño + Anterior/Siguiente. */
export function PaginacionTabla({
  total,
  offset,
  limit,
  onChange,
  tamanos = TAMANOS_DEFAULT,
  extremos = false,
}: PaginacionTablaProps) {
  const desde = total === 0 ? 0 : offset + 1;
  const hasta = Math.min(offset + limit, total);
  const opciones = tamanos.includes(limit) ? tamanos : [...tamanos, limit].sort((a, b) => a - b);
  const enPrimera = offset <= 0;
  const enUltima = offset + limit >= total;

  return (
    <nav
      aria-label="Paginación"
      className="flex min-h-11 flex-wrap items-center justify-between gap-2 border-t border-line-divider px-4 py-1.5 text-xs text-ink-muted"
    >
      <span className="tabular-nums">
        {total === 0 ? '0 de 0' : `${desde}–${hasta} de ${total}`}
      </span>
      <div className="flex flex-wrap items-center gap-2">
        {tamanos.length > 0 && (
          <label className="flex items-center gap-1.5">
            Filas por página
            <select
              value={limit}
              onChange={(e) => onChange(0, Number(e.target.value))}
              className="h-ctl-sm rounded-md border border-line-control bg-surface-card px-1.5 text-xs text-ink"
            >
              {opciones.map((n) => (
                <option key={n} value={n}>
                  {n}
                </option>
              ))}
            </select>
          </label>
        )}
        {extremos && (
          <Button
            type="button"
            variant="secondary"
            size="sm"
            disabled={enPrimera}
            aria-label="Ir al inicio"
            title="Ir al inicio"
            onClick={() => onChange(0, limit)}
          >
            <ChevronsLeft aria-hidden className="h-3.5 w-3.5" />
          </Button>
        )}
        <Button
          type="button"
          variant="secondary"
          size="sm"
          disabled={enPrimera}
          aria-label="Página anterior"
          onClick={() => onChange(Math.max(0, offset - limit), limit)}
        >
          Anterior
        </Button>
        <Button
          type="button"
          variant="secondary"
          size="sm"
          disabled={enUltima}
          aria-label="Página siguiente"
          onClick={() => onChange(offset + limit, limit)}
        >
          Siguiente
        </Button>
        {extremos && (
          <Button
            type="button"
            variant="secondary"
            size="sm"
            disabled={enUltima}
            aria-label="Ir al final"
            title="Ir al final"
            onClick={() => onChange(Math.floor((total - 1) / limit) * limit, limit)}
          >
            <ChevronsRight aria-hidden className="h-3.5 w-3.5" />
          </Button>
        )}
      </div>
    </nav>
  );
}
