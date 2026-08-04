import { Link } from '@tanstack/react-router';
import { ChevronRight } from 'lucide-react';
import type { ClienteItem } from '@/modules/datos-maestros/api/types';
import {
  EstatusCatalogoBadge,
  FiscalesIncompletosBadge,
  OrigenBadge,
} from '@/modules/datos-maestros/components/master-badges';
import { cn } from '@/lib/utils';

/**
 * Lista compacta master 320px del módulo Datos Maestros → Clientes
 * (ADR-0048). Item: clave (mono) + razón social + badges de estatus,
 * origen (A+W/Manual) y el chip ámbar "Fiscales incompletos" cuando el
 * registro aún no puede timbrar. Mismo patrón que
 * <see cref="ListaProveedoresCompacta"/>.
 */
export interface ListaClientesCompactaProps {
  items: readonly ClienteItem[];
  idActivo: string | null;
}

export function ListaClientesCompacta({
  items,
  idActivo,
}: ListaClientesCompactaProps) {
  return (
    <ul className="divide-y" role="list" aria-label="Lista de clientes">
      {items.map((c) => (
        <ItemCompacto key={c.id} item={c} activo={c.id === idActivo} />
      ))}
    </ul>
  );
}

interface ItemCompactoProps {
  item: ClienteItem;
  activo: boolean;
}

function ItemCompacto({ item, activo }: ItemCompactoProps) {
  return (
    <li>
      <Link
        to="/admin/datos-maestros/clientes/$id"
        params={{ id: item.id }}
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
            {item.clave}
          </span>
          <div className="flex shrink-0 items-center gap-1">
            <EstatusCatalogoBadge estatus={item.estatus} />
            <ChevronRight className="h-4 w-4 text-muted-foreground shrink-0" />
          </div>
        </div>
        <div className="mt-1 truncate text-xs text-muted-foreground">
          {item.razonSocial}
        </div>
        <div className="mt-1 flex flex-wrap items-center gap-1">
          <OrigenBadge origen={item.origen} className="text-[10px] py-0" />
          <FiscalesIncompletosBadge
            completos={item.datosFiscalesCompletos}
            className="text-[10px] py-0"
          />
        </div>
      </Link>
    </li>
  );
}
