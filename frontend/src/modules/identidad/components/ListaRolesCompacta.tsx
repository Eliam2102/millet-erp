import { ESTADOS_CUENTA } from '../lib/estados-ui';
import { ListadoCatalogo } from '@/modules/administracion/components/ListadoCatalogo';
import { OPCIONES_CUENTA } from '@/modules/identidad/lib/estados-ui';
import { Link } from '@tanstack/react-router';
import { ChevronRight } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import type { RolResponse } from '@/modules/identidad/api/types';
import { cn } from '@/lib/utils';

/**
 * Lista compacta master 320px del módulo Identidad → Roles. Estilo
 * inbox: cada item muestra Código + Nombre + badge "Sistema" si
 * <c>esDelSistema</c> y badge "Inactivo" cuando aplica. Item activo
 * (master-detail con id en URL) se highlightea con <c>bg-brand/10</c>.
 */
export interface ListaRolesCompactaProps {
  items: readonly RolResponse[];
  idActivo: string | null;
}

export function ListaRolesCompacta({ items, idActivo }: ListaRolesCompactaProps) {
  return (
    <ListadoCatalogo
      items={items}
      texto={(r) => r.codigo + ' ' + r.nombre}
      estado={(r) => Number(r.activo)}
      opciones={OPCIONES_CUENTA}
    >
      {(visibles) => (
        <ul className="divide-y" role="list" aria-label="Lista de roles">
          {visibles.map((r) => (
            <ItemCompacto key={r.id} item={r} activo={r.id === idActivo} />
          ))}
        </ul>
      )}
    </ListadoCatalogo>
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
          activo ? 'bg-brand/10 hover:bg-brand/15' : 'hover:bg-surface-muted/40',
        )}
        aria-current={activo ? 'page' : undefined}
      >
        <div className="flex items-start justify-between gap-2">
          <span
            className={cn(
              'truncate font-mono text-sm',
              activo ? 'font-semibold text-ink' : 'text-ink',
            )}
          >
            {item.codigo}
          </span>
          <div className="flex shrink-0 items-center gap-1">
            {item.esDelSistema && <Badge variant={ESTADOS_CUENTA.sistema.tono}>Sistema</Badge>}
            {!item.activo && <Badge variant={ESTADOS_CUENTA.inactivo.tono}>Inactivo</Badge>}
            <ChevronRight className="h-4 w-4 text-ink-muted shrink-0" />
          </div>
        </div>
        <div className="mt-1 truncate text-xs text-ink-muted">{item.nombre}</div>
      </Link>
    </li>
  );
}
