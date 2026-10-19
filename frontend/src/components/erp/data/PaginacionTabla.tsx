import { Button } from '@/components/ui/button';

export interface PaginacionTablaProps {
  total: number;
  offset: number;
  limit: number;
  onChange: (offset: number, limit: number) => void;
  /** Opciones de tamaño de página. Vacío oculta el selector. */
  tamanos?: number[];
}

const TAMANOS_DEFAULT = [10, 25, 50];

/** Footer de tabla DS 4.4: «1–25 de 214» + selector de tamaño + Anterior/Siguiente. */
export function PaginacionTabla({
  total,
  offset,
  limit,
  onChange,
  tamanos = TAMANOS_DEFAULT,
}: PaginacionTablaProps) {
  const desde = total === 0 ? 0 : offset + 1;
  const hasta = Math.min(offset + limit, total);
  const opciones = tamanos.includes(limit) ? tamanos : [...tamanos, limit].sort((a, b) => a - b);

  return (
    <nav
      aria-label="Paginación"
      className="flex h-11 items-center justify-between border-t border-line-divider px-4 text-xs text-ink-muted"
    >
      <span className="tabular-nums">
        {total === 0 ? '0 de 0' : `${desde}–${hasta} de ${total}`}
      </span>
      <div className="flex items-center gap-2">
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
        <Button
          type="button"
          variant="secondary"
          size="sm"
          disabled={offset <= 0}
          aria-label="Página anterior"
          onClick={() => onChange(Math.max(0, offset - limit), limit)}
        >
          Anterior
        </Button>
        <Button
          type="button"
          variant="secondary"
          size="sm"
          disabled={offset + limit >= total}
          aria-label="Página siguiente"
          onClick={() => onChange(offset + limit, limit)}
        >
          Siguiente
        </Button>
      </div>
    </nav>
  );
}
