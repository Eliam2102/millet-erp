import { Link } from '@tanstack/react-router';
import { EstadoBadge, DateTimeDisplay } from '@/components/erp';
import type { OrdenCompraResumen } from '@/features/compras/ordenes/api/types';
import type { BandejaOcSearch } from '@/features/compras/ordenes/lib/bandeja-oc-search-schema';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;ListaOrdenesCompacta/&gt;</c> — list view tipo inbox de
 * 320px. Mismo patrón que <c>&lt;ListaRequisicionesCompacta/&gt;</c>:
 * cada item muestra folio + estado + fecha + (si existe) referencia
 * del proveedor. El item activo (master-detail) se highlightea.
 *
 * <para>Click en un item navega a <c>/compras/ordenes/$id</c>. Si la
 * app está en modo master-detail, el detalle se renderiza al lado sin
 * desmontar la lista. Los filtros activos se preservan via
 * <c>search</c> y <c>state.bandejaOcSearch</c> para que el botón
 * "Cerrar" del detalle vuelva sin perder contexto.</para>
 */
export interface ListaOrdenesCompactaProps {
  items: readonly OrdenCompraResumen[];
  /** Id del item actualmente abierto en el panel detalle (si lo hay).
   * <c>null</c> = ningún item seleccionado. */
  idActivo: string | null;
  /** Search params actuales — se pasan como state al Link de cada
   * fila para preservar filtros cuando el detalle haga "Volver". */
  search: BandejaOcSearch;
}

export function ListaOrdenesCompacta({
  items,
  idActivo,
  search,
}: ListaOrdenesCompactaProps) {
  return (
    <ul
      className="divide-y"
      role="list"
      aria-label="Lista de órdenes de compra"
    >
      {items.map((oc) => (
        <ItemCompacto
          key={oc.id}
          item={oc}
          activo={oc.id === idActivo}
          search={search}
        />
      ))}
    </ul>
  );
}

interface ItemCompactoProps {
  item: OrdenCompraResumen;
  activo: boolean;
  search: BandejaOcSearch;
}

function ItemCompacto({ item, activo, search }: ItemCompactoProps) {
  return (
    <li>
      <Link
        to="/compras/ordenes/$id"
        params={{ id: item.id }}
        // Preserva los filtros activos al navegar entre items del
        // master-detail. Sin <c>search</c> explícito, TanStack Router
        // los reemplaza por los defaults del schema y se pierde el
        // filtro aplicado.
        search={search}
        state={{ bandejaOcSearch: search } as never}
        className={cn(
          'block px-3 py-2 transition-colors',
          activo
            ? 'bg-primary/10 hover:bg-primary/15'
            : 'hover:bg-muted/40',
        )}
        aria-current={activo ? 'page' : undefined}
      >
        <div className="flex items-start justify-between gap-2">
          <span
            className={cn(
              'truncate font-mono text-sm',
              activo ? 'font-semibold text-foreground' : 'text-foreground',
            )}
          >
            {item.folio}
          </span>
          <EstadoBadge tipo="orden-compra" estado={item.estado} />
        </div>
        <div className="mt-1 flex items-center justify-between gap-2 text-xs text-muted-foreground">
          <span className="truncate">
            {item.referenciaProveedor != null ? (
              <>Ref. {item.referenciaProveedor}</>
            ) : (
              <span className="italic">sin referencia del proveedor</span>
            )}
          </span>
          <DateTimeDisplay value={item.fechaDocumento} />
        </div>
      </Link>
    </li>
  );
}
