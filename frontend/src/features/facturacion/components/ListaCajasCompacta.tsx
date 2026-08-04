import { Link } from '@tanstack/react-router';
import type { CajaListadoItem } from '@/features/facturacion/api/types';
import { resumenAlcance } from '@/features/facturacion/lib/cajas-format';
import type { CajasSearch } from '@/features/facturacion/lib/cajas-search-schema';
import { cn } from '@/lib/utils';

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
              'block px-3 py-2 transition-colors',
              c.id === idActivo ? 'bg-primary/10 hover:bg-primary/15' : 'hover:bg-muted/40',
            )}
            aria-current={c.id === idActivo ? 'page' : undefined}
          >
            <div className="flex items-start justify-between gap-2">
              <span className={cn('truncate text-sm', c.id === idActivo && 'font-semibold')}>
                {c.nombre}
              </span>
              <ChipEstatusCaja estatus={c.estatus} />
            </div>
            <div className="mt-1 text-xs text-muted-foreground">
              {resumenAlcance(c)}
            </div>
          </Link>
        </li>
      ))}
    </ul>
  );
}

export function ChipEstatusCaja({ estatus }: { estatus: string }) {
  const activa = estatus === 'Activo';
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
        activa ? 'bg-emerald-100 text-emerald-800' : 'bg-zinc-200 text-zinc-700',
      )}
    >
      {activa ? 'Activa' : 'Inactiva'}
    </span>
  );
}
