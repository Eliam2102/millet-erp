import { Link } from '@tanstack/react-router';
import { EstadoBadge, DateTimeDisplay } from '@/components/erp';
import { OrigenBadge } from '@/features/compras/components/OrigenBadge';
import type { RequisicionListItemResponse } from '@/features/compras/api/types';
import type { BandejaSearch } from '@/features/compras/lib/bandeja-search-schema';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;ListaRequisicionesCompacta/&gt;</c> — list view tipo inbox de
 * 320px. Diseño polish (design/frontend-polish): cada item muestra
 * folio + estado + fecha (más una segunda línea con descripción si
 * existe). El item activo (cuando estamos en master-detail con un
 * id seleccionado) se highlightea.
 *
 * <para>Click en un item navega a <c>/compras/requisiciones/$id</c>.
 * Si la app está en modo master-detail (route layout con
 * <c>&lt;Outlet/&gt;</c>), el detalle se renderiza al lado sin
 * desmontar la lista. Si no, el comportamiento es la navegación
 * tradicional.</para>
 */
export interface ListaRequisicionesCompactaProps {
  items: readonly RequisicionListItemResponse[];
  /** Id del item actualmente abierto en el panel detalle (si lo hay).
   * <c>null</c> = ningún item seleccionado. */
  idActivo: string | null;
  /** Search params actuales — se pasan como state al Link de cada
   * fila para preservar filtros cuando el detalle haga "Volver". */
  search: BandejaSearch;
}

export function ListaRequisicionesCompacta({
  items,
  idActivo,
  search,
}: ListaRequisicionesCompactaProps) {
  return (
    <ul
      className="divide-y"
      role="list"
      aria-label="Lista de requisiciones"
    >
      {items.map((r) => (
        <ItemCompacto
          key={r.id}
          item={r}
          activo={r.id === idActivo}
          search={search}
        />
      ))}
    </ul>
  );
}

interface ItemCompactoProps {
  item: RequisicionListItemResponse;
  activo: boolean;
  search: BandejaSearch;
}

function ItemCompacto({ item, activo, search }: ItemCompactoProps) {
  return (
    <li>
      <Link
        to="/compras/requisiciones/$id"
        params={{ id: item.id }}
        // Preserva los filtros activos al navegar entre items del
        // master-detail. Sin <c>search</c> explícito, TanStack Router
        // los reemplaza por los defaults del schema y se pierde el
        // filtro aplicado por el usuario en la lista.
        search={search}
        state={{ bandejaSearch: search } as never}
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
          <div className="flex shrink-0 items-center gap-1">
            <OrigenBadge origen={item.origen} />
            <EstadoBadge
              tipo="requisicion"
              estado={item.estado}
              situacion={item.situacionSurtido}
            />
          </div>
        </div>
        <div className="mt-1 flex items-center justify-between gap-2 text-xs text-muted-foreground">
          <span className="truncate">
            {item.descripcion ?? <span className="italic">sin descripción</span>}
          </span>
          <DateTimeDisplay value={item.fechaSolicitud} />
        </div>
      </Link>
    </li>
  );
}
