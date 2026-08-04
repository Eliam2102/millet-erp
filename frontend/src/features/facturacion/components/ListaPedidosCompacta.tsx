import { Link } from '@tanstack/react-router';
import { DateTimeDisplay } from '@/components/erp';
import { ChipEstadoPedido } from '@/features/facturacion/pages/DetallePedido';
import { ETIQUETA_ORIGEN_PEDIDO } from '@/features/facturacion/lib/glosario';
import type { PedidoBandejaItem } from '@/features/facturacion/api/types';
import type { PedidosSearch } from '@/features/facturacion/lib/pedidos-search-schema';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;ListaPedidosCompacta/&gt;</c> — list view tipo inbox de 320px
 * para el master-detail de pedidos facturables (FAC-UX-PR5, patrón
 * <c>ListaRequisicionesCompacta</c> §6.1). Folio + cliente + chip de
 * estado + total; highlight del item activo; el Link preserva los
 * filtros de la bandeja vía <c>search</c> + <c>state</c>.
 */
export interface ListaPedidosCompactaProps {
  items: readonly PedidoBandejaItem[];
  idActivo: string | null;
  search: PedidosSearch;
}

export function ListaPedidosCompacta({
  items,
  idActivo,
  search,
}: ListaPedidosCompactaProps) {
  return (
    <ul className="divide-y" role="list" aria-label="Lista de pedidos facturables">
      {items.map((p) => (
        <li key={p.id}>
          <Link
            to="/facturacion/pedidos/$id"
            params={{ id: p.id }}
            search={search}
            state={{ bandejaSearch: search } as never}
            className={cn(
              'block px-3 py-2 transition-colors',
              p.id === idActivo
                ? 'bg-primary/10 hover:bg-primary/15'
                : 'hover:bg-muted/40',
            )}
            aria-current={p.id === idActivo ? 'page' : undefined}
          >
            <div className="flex items-start justify-between gap-2">
              <span
                className={cn(
                  'truncate font-mono text-sm',
                  p.id === idActivo && 'font-semibold',
                )}
              >
                {p.numeroPedido ?? 'Manual'}
              </span>
              <div className="flex shrink-0 items-center gap-1">
                <span className="rounded-full bg-muted px-1.5 py-0.5 text-[10px]">
                  {ETIQUETA_ORIGEN_PEDIDO[p.origen] ?? p.origen}
                </span>
                <ChipEstadoPedido estado={p.estado} />
              </div>
            </div>
            <div className="mt-1 flex items-center justify-between gap-2 text-xs text-muted-foreground">
              <span className="truncate">{p.clienteNombre}</span>
              <span className="shrink-0 font-mono tabular-nums">
                {p.total.toFixed(2)} {p.moneda}
              </span>
            </div>
            <div className="mt-0.5 text-right text-[11px] text-muted-foreground">
              <DateTimeDisplay value={p.createdAt} />
            </div>
          </Link>
        </li>
      ))}
    </ul>
  );
}
