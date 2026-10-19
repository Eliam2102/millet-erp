import type { ReactNode } from 'react';
import { Button } from '@/components/ui/button';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';

interface BarraFiltrosProps {
  /** Búsqueda server-side (<c>BusquedaFolio</c>); la barra le fija 260 px. */
  busqueda?: ReactNode;
  /** Filtros principales, siempre visibles en la misma línea. */
  filtros?: ReactNode;
  /**
   * Filtros secundarios. Van dentro del popover «+ Filtro», apilados a todo el
   * ancho (320 px), para que la barra siga en una línea a 1024–1366 px.
   */
  masFiltros?: ReactNode;
  /** Cuántos de los secundarios están activos (se muestra en el botón). */
  masFiltrosActivos?: number;
  /** Chips de filtros activos que llegan por la URL (<c>FiltroActivoChip</c>). */
  chips?: ReactNode;
  /** Total de la bandeja («214 facturas»). */
  total?: ReactNode;
  /** Si se pasa, aparece «Limpiar filtros». */
  onLimpiar?: () => void;
  'aria-label'?: string;
}

/**
 * Barra de filtros de las bandejas (DS 4.4): búsqueda de 260 px, filtros
 * principales, chip «+ Filtro» que despliega el resto, chips activos y total
 * a la derecha. Una sola línea con wrap solo como último recurso.
 */
export function BarraFiltros({
  busqueda,
  filtros,
  masFiltros,
  masFiltrosActivos = 0,
  chips,
  total,
  onLimpiar,
  'aria-label': ariaLabel = 'Filtros de la bandeja',
}: BarraFiltrosProps) {
  return (
    <div
      role="search"
      aria-label={ariaLabel}
      className="flex flex-wrap items-center gap-2"
      data-print="hidden"
    >
      {busqueda != null && (
        <div className="w-[260px] max-w-full shrink-0 [&_input]:h-ctl-md [&_input]:rounded-[7px] [&_input]:border-line">
          {busqueda}
        </div>
      )}
      {filtros}
      {masFiltros != null && (
        <Popover>
          <PopoverTrigger asChild>
            <button
              type="button"
              aria-label="Más filtros"
              className="inline-flex h-ctl-md items-center gap-1 rounded-[7px] border border-dashed border-line-dashed px-2.5 text-xs text-ink-secondary hover:bg-surface-subtle focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand"
            >
              + Filtro
              {masFiltrosActivos > 0 && (
                <span className="tabular-nums text-brand">({masFiltrosActivos})</span>
              )}
            </button>
          </PopoverTrigger>
          <PopoverContent align="start" className="w-80 space-y-3 [&_button[role=combobox]]:w-full">
            <p className="text-sm font-medium text-ink">Más filtros</p>
            {masFiltros}
          </PopoverContent>
        </Popover>
      )}
      {chips}
      {onLimpiar && (
        <Button type="button" variant="ghost" size="sm" onClick={onLimpiar}>
          Limpiar filtros
        </Button>
      )}
      {total != null && (
        <span className="ml-auto whitespace-nowrap text-xs text-ink-muted" aria-live="polite">
          {total}
        </span>
      )}
    </div>
  );
}
