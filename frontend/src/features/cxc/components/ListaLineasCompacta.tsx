import { Link } from '@tanstack/react-router';
import { ChipEstadoLinea } from '@/features/cxc/components/ChipEstadoLinea';
import { formatoMonto } from '@/features/cxc/lib/glosario';
import type { LineaCreditoResponse } from '@/features/cxc/api/types';
import type { LineasSearch } from '@/features/cxc/lib/lineas-search-schema';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;ListaLineasCompacta/&gt;</c> — list view tipo inbox de 320px
 * para el master-detail de líneas de crédito (CXC-FE-PR2, patrón
 * <c>ListaFacturasCompacta</c> §6.1). Razón social + moneda/límite +
 * chip de estado; highlight del item activo; el Link preserva filtros
 * vía <c>search</c> + <c>state</c>.
 */
export interface ListaLineasCompactaProps {
  items: readonly LineaCreditoResponse[];
  idActivo: string | null;
  search: LineasSearch;
  /** Razón social por clienteId (del lookup CxC); fallback al RFC/id. */
  nombresCliente: ReadonlyMap<string, string>;
}

export function ListaLineasCompacta({
  items,
  idActivo,
  search,
  nombresCliente,
}: ListaLineasCompactaProps) {
  return (
    <ul className="divide-y" role="list" aria-label="Lista de líneas de crédito">
      {items.map((l) => (
        <li key={l.id}>
          <Link
            to="/cxc/lineas-credito/$id"
            params={{ id: l.id }}
            search={search}
            state={{ bandejaSearch: search } as never}
            className={cn(
              'block px-3 py-2 transition-colors',
              l.id === idActivo
                ? 'bg-primary/10 hover:bg-primary/15'
                : 'hover:bg-muted/40',
            )}
            aria-current={l.id === idActivo ? 'page' : undefined}
          >
            <div className="flex items-start justify-between gap-2">
              <span
                className={cn(
                  'truncate text-sm',
                  l.id === idActivo && 'font-semibold',
                )}
              >
                {nombresCliente.get(l.clienteId) ?? l.clienteId.slice(0, 8)}
              </span>
              <ChipEstadoLinea estado={l.estado} />
            </div>
            <div className="mt-1 flex items-center justify-between gap-2 text-xs text-muted-foreground">
              <span className="shrink-0 font-mono tabular-nums">
                {formatoMonto(l.limite, l.moneda)}
              </span>
              <span className="truncate">
                {l.plazoDias} días{l.clasificacion ? ` · ${l.clasificacion}` : ''}
              </span>
            </div>
          </Link>
        </li>
      ))}
    </ul>
  );
}
