import { Link } from '@tanstack/react-router';
import { ChevronRight } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import type { UsuarioResponse } from '@/modules/identidad/api/types';
import { cn } from '@/lib/utils';

/**
 * Lista compacta master 320px del módulo Identidad → Usuarios. Estilo
 * inbox: cada item muestra Nombre (truncate, font-semibold) + email
 * pequeño debajo + badge "Inactivo" cuando aplica. Item activo
 * (master-detail con id en URL) se highlightea con
 * <c>bg-primary/10</c>, igual que <c>ListaEmpresasCompacta</c> /
 * <c>ListaRolesCompacta</c>.
 */
export interface ListaUsuariosCompactaProps {
  items: readonly UsuarioResponse[];
  idActivo: string | null;
}

export function ListaUsuariosCompacta({
  items,
  idActivo,
}: ListaUsuariosCompactaProps) {
  return (
    <ul className="divide-y" role="list" aria-label="Lista de usuarios">
      {items.map((u) => (
        <ItemCompacto key={u.id} item={u} activo={u.id === idActivo} />
      ))}
    </ul>
  );
}

interface ItemCompactoProps {
  item: UsuarioResponse;
  activo: boolean;
}

function ItemCompacto({ item, activo }: ItemCompactoProps) {
  return (
    <li>
      <Link
        to="/admin/usuarios/$id"
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
              'truncate text-sm font-semibold',
              activo ? 'text-foreground' : 'text-foreground',
            )}
          >
            {item.nombre}
          </span>
          <div className="flex shrink-0 items-center gap-1">
            {!item.activo && (
              <Badge
                variant="outline"
                className="text-[10px] text-muted-foreground"
              >
                Inactivo
              </Badge>
            )}
            <ChevronRight className="h-4 w-4 text-muted-foreground shrink-0" />
          </div>
        </div>
        <div className="mt-1 truncate text-xs text-muted-foreground">
          {item.email}
        </div>
      </Link>
    </li>
  );
}
