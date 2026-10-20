import { ESTADOS_CUENTA } from '../lib/estados-ui';
import { ListadoCatalogo } from '@/modules/administracion/components/ListadoCatalogo';
import { OPCIONES_CUENTA } from '@/modules/identidad/lib/estados-ui';
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
 * <c>bg-brand/10</c>, igual que <c>ListaEmpresasCompacta</c> /
 * <c>ListaRolesCompacta</c>.
 */
export interface ListaUsuariosCompactaProps {
  items: readonly UsuarioResponse[];
  idActivo: string | null;
}

export function ListaUsuariosCompacta({ items, idActivo }: ListaUsuariosCompactaProps) {
  return (
    <ListadoCatalogo
      items={items}
      texto={(u) => u.nombre + ' ' + u.email}
      estado={(u) => Number(u.activo)}
      opciones={OPCIONES_CUENTA}
    >
      {(visibles) => (
        <ul className="divide-y" role="list" aria-label="Lista de usuarios">
          {visibles.map((u) => (
            <ItemCompacto key={u.id} item={u} activo={u.id === idActivo} />
          ))}
        </ul>
      )}
    </ListadoCatalogo>
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
          activo ? 'bg-brand/10 hover:bg-brand/15' : 'hover:bg-surface-muted/40',
        )}
        aria-current={activo ? 'page' : undefined}
      >
        <div className="flex items-start justify-between gap-2">
          <span className={cn('truncate text-sm font-semibold', activo ? 'text-ink' : 'text-ink')}>
            {item.nombre}
          </span>
          <div className="flex shrink-0 items-center gap-1">
            {!item.activo && <Badge variant={ESTADOS_CUENTA.inactivo.tono}>Inactivo</Badge>}
            <ChevronRight className="h-4 w-4 text-ink-muted shrink-0" />
          </div>
        </div>
        <div className="mt-1 truncate text-xs text-ink-muted">{item.email}</div>
        <Badge variant={item.empleadoId ? 'success' : 'neutral'}>
          {item.empleadoId ? 'Empleado vinculado' : 'Sin empleado vinculado'}
        </Badge>
      </Link>
    </li>
  );
}
