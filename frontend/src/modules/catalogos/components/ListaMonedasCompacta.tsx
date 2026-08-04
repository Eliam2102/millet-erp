import { Link } from '@tanstack/react-router';
import { ChevronRight } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import type { MonedaResponse } from '@/modules/catalogos/api/types';
import { cn } from '@/lib/utils';

/**
 * Lista compacta master 320px del módulo Catálogos → Monedas. Item:
 * código (3 chars uppercase font-mono) + nombre + badge Activa/Inactiva
 * + ChevronRight. Mismo patrón que <see cref="ListaArticulosCompacta"/>.
 */
export interface ListaMonedasCompactaProps {
  items: readonly MonedaResponse[];
  idActivo: string | null;
}

export function ListaMonedasCompacta({
  items,
  idActivo,
}: ListaMonedasCompactaProps) {
  return (
    <ul className="divide-y" role="list" aria-label="Lista de monedas">
      {items.map((m) => (
        <ItemCompacto key={m.id} item={m} activo={m.id === idActivo} />
      ))}
    </ul>
  );
}

interface ItemCompactoProps {
  item: MonedaResponse;
  activo: boolean;
}

function ItemCompacto({ item, activo }: ItemCompactoProps) {
  return (
    <li>
      <Link
        to="/admin/catalogos/monedas/$id"
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
            {item.codigo}
          </span>
          <div className="flex shrink-0 items-center gap-1">
            {item.activa ? (
              <Badge variant="secondary">Activa</Badge>
            ) : (
              <Badge variant="outline" className="text-muted-foreground">
                Inactiva
              </Badge>
            )}
            <ChevronRight className="h-4 w-4 text-muted-foreground shrink-0" />
          </div>
        </div>
        <div className="mt-1">
          <span className="truncate text-xs text-muted-foreground">
            {item.nombre}
          </span>
        </div>
      </Link>
    </li>
  );
}
