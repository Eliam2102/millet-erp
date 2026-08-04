import { Link } from '@tanstack/react-router';
import { DateTimeDisplay } from '@/components/erp';
import { ChipTimbrado } from '@/features/facturacion/pages/BandejaFacturas';
import type { CartaPorteBandejaItem } from '@/features/facturacion/api/types';
import type { CartaPorteSearch } from '@/features/facturacion/lib/carta-porte-search-schema';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;ListaCartaPorteCompacta/&gt;</c> — list view tipo inbox de
 * 320px para el master-detail de Carta Porte (FAC-UX-PR6, patrón
 * <c>ListaRequisicionesCompacta</c> §6.1).
 */
export interface ListaCartaPorteCompactaProps {
  items: readonly CartaPorteBandejaItem[];
  idActivo: string | null;
  search: CartaPorteSearch;
}

export function ListaCartaPorteCompacta({
  items,
  idActivo,
  search,
}: ListaCartaPorteCompactaProps) {
  return (
    <ul className="divide-y" role="list" aria-label="Lista de Cartas Porte">
      {items.map((c) => (
        <li key={c.id}>
          <Link
            to="/facturacion/carta-porte/$id"
            params={{ id: c.id }}
            search={search}
            state={{ bandejaSearch: search } as never}
            className={cn(
              'block px-3 py-2 transition-colors',
              c.id === idActivo
                ? 'bg-primary/10 hover:bg-primary/15'
                : 'hover:bg-muted/40',
            )}
            aria-current={c.id === idActivo ? 'page' : undefined}
          >
            <div className="flex items-start justify-between gap-2">
              <span
                className={cn(
                  'truncate font-mono text-sm',
                  c.id === idActivo && 'font-semibold',
                )}
              >
                {c.folio}
              </span>
              <div className="flex shrink-0 items-center gap-1">
                <span className="rounded-full bg-muted px-1.5 py-0.5 text-[10px]">
                  {c.tipo}
                </span>
                <ChipTimbrado estado={c.estado} />
              </div>
            </div>
            <div className="mt-1 flex items-center justify-between gap-2 text-xs text-muted-foreground">
              <span className="truncate">{c.tramo}</span>
              <DateTimeDisplay value={c.fechaSalida} />
            </div>
          </Link>
        </li>
      ))}
    </ul>
  );
}
