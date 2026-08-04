import { Link } from '@tanstack/react-router';
import { ChevronRight } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import type { RolResponse } from '@/modules/identidad/api/types';
import { cn } from '@/lib/utils';

/**
 * Lista compacta master 320px del módulo Identidad → Roles. Estilo
 * inbox: cada item muestra Código + Nombre + badge "Sistema" si
 * <c>esDelSistema</c> y badge "Inactivo" cuando aplica. Item activo
 * (master-detail con id en URL) se highlightea con <c>bg-primary/10</c>.
 */
export interface ListaRolesCompactaProps {
  items: readonly RolResponse[];
  idActivo: string | null;
}

export function ListaRolesCompacta({
  items,
  idActivo,
}: ListaRolesCompactaProps) {
  return (
    <ul className="divide-y" role="list" aria-label="Lista de roles">
      {items.map((r) => (
        <ItemCompacto key={r.id} item={r} activo={r.id === idActivo} />
      ))}
    </ul>
  );
}

interface ItemCompactoProps {
  item: RolResponse;
  activo: boolean;
}

function ItemCompacto({ item, activo }: ItemCompactoProps) {
  return (
    <li>
      <Link
        to="/admin/roles/$id"
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
            {item.esDelSistema && (
              <Badge variant="outline" className="text-[10px]">
                Sistema
              </Badge>
            )}
            {!item.activo && (
              <Badge variant="outline" className="text-[10px] text-muted-foreground">
                Inactivo
              </Badge>
            )}
            <ChevronRight className="h-4 w-4 text-muted-foreground shrink-0" />
          </div>
        </div>
        <div className="mt-1 truncate text-xs text-muted-foreground">
          {item.nombre}
        </div>
      </Link>
    </li>
  );
}
