import { Link } from '@tanstack/react-router';
import { DateTimeDisplay } from '@/components/erp';
import { ChipTimbrado } from '@/features/facturacion/pages/BandejaFacturas';
import type { FacturaAnticipoBandejaItem } from '@/features/facturacion/api/types';
import type { FacturasAnticipoSearch } from '@/features/facturacion/lib/facturas-anticipo-search-schema';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;ListaFacturasAnticipoCompacta/&gt;</c> — inbox de 320px para el
 * master-detail de facturas de anticipo (ANT-PR2; molde
 * <c>ListaFacturasCompacta</c>). Folio + receptor + chip de timbrado +
 * saldo; el Link preserva los filtros de la bandeja.
 */
export interface ListaFacturasAnticipoCompactaProps {
  items: readonly FacturaAnticipoBandejaItem[];
  idActivo: string | null;
  search: FacturasAnticipoSearch;
}

export function ListaFacturasAnticipoCompacta({
  items,
  idActivo,
  search,
}: ListaFacturasAnticipoCompactaProps) {
  return (
    <ul className="divide-y" role="list" aria-label="Lista de facturas de anticipo">
      {items.map((f) => (
        <li key={f.id}>
          <Link
            to="/facturacion/anticipos/facturas/$id"
            params={{ id: f.id }}
            search={search}
            state={{ bandejaSearch: search } as never}
            className={cn(
              'block px-3 py-2 transition-colors',
              f.id === idActivo
                ? 'bg-primary/10 hover:bg-primary/15'
                : 'hover:bg-muted/40',
            )}
            aria-current={f.id === idActivo ? 'page' : undefined}
          >
            <div className="flex items-start justify-between gap-2">
              <span
                className={cn(
                  'truncate font-mono text-sm',
                  f.id === idActivo && 'font-semibold',
                )}
              >
                {f.folio}
              </span>
              <ChipTimbrado estado={f.estado} />
            </div>
            <div className="mt-1 flex items-center justify-between gap-2 text-xs text-muted-foreground">
              <span className="truncate">{f.receptorNombre}</span>
              <span className="shrink-0 font-mono tabular-nums">
                {f.saldo != null
                  ? `Saldo ${f.saldo.toFixed(2)}`
                  : `${f.total.toFixed(2)} ${f.moneda}`}
              </span>
            </div>
            {f.fechaTimbrado && (
              <div className="mt-0.5 text-right text-[11px] text-muted-foreground">
                <DateTimeDisplay value={f.fechaTimbrado} />
              </div>
            )}
          </Link>
        </li>
      ))}
    </ul>
  );
}
