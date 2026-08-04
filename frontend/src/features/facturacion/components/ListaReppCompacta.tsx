import { Link } from '@tanstack/react-router';
import { DateTimeDisplay } from '@/components/erp';
import { ChipTimbrado } from '@/features/facturacion/pages/BandejaFacturas';
import type { ReppBandejaItem } from '@/features/facturacion/api/types';
import type { ReppSearch } from '@/features/facturacion/lib/repp-search-schema';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;ListaReppCompacta/&gt;</c> — list view tipo inbox de 320px para
 * el master-detail de complementos de pago (FAC-UX-PR6, patrón
 * <c>ListaRequisicionesCompacta</c> §6.1).
 */
export interface ListaReppCompactaProps {
  items: readonly ReppBandejaItem[];
  idActivo: string | null;
  search: ReppSearch;
}

export function ListaReppCompacta({ items, idActivo, search }: ListaReppCompactaProps) {
  return (
    <ul className="divide-y" role="list" aria-label="Lista de complementos de pago">
      {items.map((r) => (
        <li key={r.id}>
          <Link
            to="/facturacion/repp/$id"
            params={{ id: r.id }}
            search={search}
            state={{ bandejaSearch: search } as never}
            className={cn(
              'block px-3 py-2 transition-colors',
              r.id === idActivo
                ? 'bg-primary/10 hover:bg-primary/15'
                : 'hover:bg-muted/40',
            )}
            aria-current={r.id === idActivo ? 'page' : undefined}
          >
            <div className="flex items-start justify-between gap-2">
              <span
                className={cn(
                  'truncate font-mono text-sm',
                  r.id === idActivo && 'font-semibold',
                )}
              >
                {r.folio}
              </span>
              <ChipTimbrado estado={r.estado} />
            </div>
            <div className="mt-1 flex items-center justify-between gap-2 text-xs text-muted-foreground">
              <span className="truncate">{r.receptorNombre}</span>
              <span className="shrink-0 font-mono tabular-nums">
                {r.importeTotalPago.toFixed(2)}
              </span>
            </div>
            <div className="mt-0.5 text-right text-[11px] text-muted-foreground">
              <DateTimeDisplay value={r.fechaPago} />
            </div>
          </Link>
        </li>
      ))}
    </ul>
  );
}
