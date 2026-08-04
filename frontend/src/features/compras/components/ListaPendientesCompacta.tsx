import { Link } from '@tanstack/react-router';
import { EstadoBadge, NivelPendienteBadge, DateTimeDisplay } from '@/components/erp';
import { requisitanteLabel } from '@/features/compras/lib/nombres';
import type { RequisicionListItemResponse } from '@/features/compras/api/types';
import type { PendientesSearch } from '@/features/compras/lib/pendientes-search-schema';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;ListaPendientesCompacta/&gt;</c> — list view inbox de 320px
 * para la bandeja P2. Misma estructura que la P1 pero la línea
 * secundaria muestra <b>requisitante</b> (más relevante para el
 * autorizador, que filtra mentalmente por "quién pidió esto") en
 * lugar de la descripción de la RQ.
 *
 * <para>Click en un item navega a
 * <c>/compras/pendientes/$id</c> — el <c>&lt;PendientesLayout/&gt;</c>
 * monta el detalle al lado sin desmontar la lista, preservando
 * filtros via <c>search</c> en el Link.</para>
 */
export interface ListaPendientesCompactaProps {
  items: readonly RequisicionListItemResponse[];
  idActivo: string | null;
  search: PendientesSearch;
}

export function ListaPendientesCompacta({
  items,
  idActivo,
  search,
}: ListaPendientesCompactaProps) {
  return (
    <ul
      className="divide-y"
      role="list"
      aria-label="Lista de requisiciones pendientes"
    >
      {items.map((r) => (
        <ItemCompacto
          key={r.id}
          item={r}
          activo={r.id === idActivo}
          requisitante={requisitanteLabel(r)}
          search={search}
        />
      ))}
    </ul>
  );
}

interface ItemCompactoProps {
  item: RequisicionListItemResponse;
  activo: boolean;
  requisitante: string;
  search: PendientesSearch;
}

function ItemCompacto({ item, activo, requisitante, search }: ItemCompactoProps) {
  return (
    <li>
      <Link
        to="/compras/pendientes/$id"
        params={{ id: item.id }}
        search={search}
        state={{ pendientesSearch: search } as never}
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
          <div className="flex shrink-0 flex-col items-end gap-1">
            <EstadoBadge tipo="requisicion" estado={item.estado} />
            {item.nivelPendiente != null && (
              <NivelPendienteBadge nivel={item.nivelPendiente} />
            )}
          </div>
        </div>
        <div className="mt-1 flex items-center justify-between gap-2 text-xs text-muted-foreground">
          <span className="truncate">{requisitante}</span>
          <DateTimeDisplay value={item.fechaSolicitud} />
        </div>
      </Link>
    </li>
  );
}
