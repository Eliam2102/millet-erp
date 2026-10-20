import type { CajaListadoItem } from '@/features/facturacion/api/types';
import { EstadoFacturacionBadge } from '@/features/facturacion/components/EstadoFacturacionBadge';
import { resumenAlcance } from '@/features/facturacion/lib/cajas-format';
import type { CajasSearch } from '@/features/facturacion/lib/cajas-search-schema';
import { cn } from '@/lib/utils';
import { Link } from '@tanstack/react-router';

/**
 * <c>&lt;ListaCajasCompacta/&gt;</c> — list view de 320px para el
 * master-detail de cajas (CAJAS-PR5, patrón <c>ListaFacturasCompacta</c>
 * §6.1). Nombre + chip de estatus + counts del alcance.
 */
export interface ListaCajasCompactaProps {
  items: readonly CajaListadoItem[];
  idActivo: string | null;
  search: CajasSearch;
}

export function ListaCajasCompacta({ items, idActivo, search }: ListaCajasCompactaProps) {
  return (
    <ul className="divide-y" role="list" aria-label="Lista de cajas">
      {items.map((c) => (
        <li key={c.id}>
          <Link
            to="/facturacion/cajas/$id"
            params={{ id: c.id }}
            search={search}
            className={cn(
              'block px-3.5 py-[11px] transition-colors',
              c.id === idActivo
                ? 'bg-surface-selected hover:bg-surface-selected'
                : 'hover:bg-surface-muted/40',
            )}
            aria-current={c.id === idActivo ? 'page' : undefined}
          >
            <div className="flex items-start justify-between gap-2">
              <span className={cn('truncate text-sm', c.id === idActivo && 'font-semibold')}>
                {c.nombre}
              </span>
              <ChipEstatusCaja estatus={c.estatus} />
            </div>
            <div className="mt-1 text-xs text-ink-muted">{resumenAlcance(c)}</div>
          </Link>
        </li>
      ))}
    </ul>
  );
}

export function ChipEstatusCaja({ estatus }: { estatus: string }) {
  return <EstadoFacturacionBadge entidad="caja" estado={estatus} />;
}
