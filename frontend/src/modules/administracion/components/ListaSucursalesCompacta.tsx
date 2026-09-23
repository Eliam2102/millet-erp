import { Link } from '@tanstack/react-router';
import { ChevronRight } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import {
  EstatusCatalogo,
  type SucursalResponse,
} from '@/modules/administracion/api/types';
import { cn } from '@/lib/utils';

export interface ListaSucursalesCompactaProps {
  items: readonly SucursalResponse[];
  idActivo: string | null;
}

export function ListaSucursalesCompacta({
  items,
  idActivo,
}: ListaSucursalesCompactaProps) {
  return (
    <ul className="divide-y" role="list" aria-label="Lista de sucursales">
      {items.map((s) => (
        <ItemCompacto key={s.id} item={s} activo={s.id === idActivo} />
      ))}
    </ul>
  );
}

interface ItemCompactoProps {
  item: SucursalResponse;
  activo: boolean;
}

function ItemCompacto({ item, activo }: ItemCompactoProps) {
  const esInactiva = item.estatus === EstatusCatalogo.Inactivo;

  return (
    <li>
      <Link
        to="/admin/sucursales/$id"
        params={{ id: item.id }}
        className={cn(
          'block px-3 py-2.5 transition-colors',
          activo
            ? 'bg-primary/10 hover:bg-primary/15'
            : 'hover:bg-muted/40',
        )}
        aria-current={activo ? 'page' : undefined}
      >
        <div className="flex items-start justify-between gap-2">
          <div className="min-w-0 flex-1">
            <div className="flex items-center gap-1.5">
              <span
                className={cn(
                  'truncate font-mono text-sm',
                  activo ? 'font-semibold text-foreground' : 'text-foreground',
                )}
              >
                {item.clave}
              </span>
              {esInactiva ? (
                <Badge
                  variant="outline"
                  className="border-amber-400 bg-amber-50 px-1 py-0 text-[10px] text-amber-900 dark:bg-amber-950/40 dark:text-amber-200"
                >
                  Inactiva
                </Badge>
              ) : (
                <Badge
                  variant="secondary"
                  className="px-1 py-0 text-[10px] text-muted-foreground"
                >
                  Activa
                </Badge>
              )}
            </div>
            <p className="truncate text-xs text-muted-foreground">
              {item.nombre}
            </p>
          </div>
          <div className="flex shrink-0 items-center pt-1 text-muted-foreground">
            <ChevronRight className="h-4 w-4" aria-hidden="true" />
          </div>
        </div>
      </Link>
    </li>
  );
}
