import { DateTimeDisplay } from '@/components/erp';
import { ClasePedidoBadge } from '@/features/facturacion/components/ClasePedidoBadge';
import type { FacturaBandejaItem } from '@/features/facturacion/api/types';
import type { FacturasSearch } from '@/features/facturacion/lib/facturas-search-schema';
import { ChipTimbrado } from '@/features/facturacion/pages/BandejaFacturas';
import { formatMoney } from '@/lib/money';
import { cn } from '@/lib/utils';
import { Link } from '@tanstack/react-router';

/**
 * <c>&lt;ListaFacturasCompacta/&gt;</c> — list view tipo inbox de 320px
 * para el master-detail de comprobantes (FAC-UX-PR5, patrón
 * <c>ListaRequisicionesCompacta</c> §6.1). Folio + receptor + chip de
 * timbrado + total; highlight del item activo; el Link preserva los
 * filtros de la bandeja vía <c>search</c> + <c>state</c>.
 */
export interface ListaFacturasCompactaProps {
  items: readonly FacturaBandejaItem[];
  idActivo: string | null;
  search: FacturasSearch;
}

export function ListaFacturasCompacta({ items, idActivo, search }: ListaFacturasCompactaProps) {
  return (
    <ul className="divide-y" role="list" aria-label="Lista de facturas">
      {items.map((f) => (
        <li key={f.id}>
          <Link
            to="/facturacion/facturas/$id"
            params={{ id: f.id }}
            search={search}
            state={{ bandejaSearch: search } as never}
            className={cn(
              'block px-3.5 py-[11px] transition-colors',
              f.id === idActivo
                ? 'bg-surface-selected hover:bg-surface-selected'
                : 'hover:bg-surface-muted/40',
            )}
            aria-current={f.id === idActivo ? 'page' : undefined}
          >
            <div className="flex items-start justify-between gap-2">
              <span
                className={cn('truncate font-mono text-sm', f.id === idActivo && 'font-semibold')}
              >
                {f.folio}
              </span>
              <ChipTimbrado estado={f.estado} />
            </div>
            <div className="mt-1 flex items-center justify-between gap-2 text-xs text-ink-muted">
              <span className="truncate">{f.receptorNombre}</span>
              <span className="shrink-0 font-mono tabular-nums">
                {formatMoney({ amount: f.total, currency: f.moneda })}
              </span>
            </div>
            <ClasePedidoBadge clasificacion={f} className="mt-1" />
            {f.fechaTimbrado && (
              <div className="mt-0.5 text-right text-[11px] text-ink-muted tabular-nums">
                <DateTimeDisplay value={f.fechaTimbrado} />
              </div>
            )}
          </Link>
        </li>
      ))}
    </ul>
  );
}
