import { ListadoCatalogo } from '@/modules/administracion/components/ListadoCatalogo';
import { OPCIONES_CATALOGO } from '@/modules/administracion/lib/estados-ui';
import { Link } from '@tanstack/react-router';
import { ChevronRight } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import {
  EstatusCatalogo,
  type SucursalResponse,
  TipoSucursal,
} from '@/modules/administracion/api/types';
import { cn } from '@/lib/utils';

export interface ListaSucursalesCompactaProps {
  items: readonly SucursalResponse[];
  idActivo: string | null;
}

export function ListaSucursalesCompacta({ items, idActivo }: ListaSucursalesCompactaProps) {
  return (
    <ListadoCatalogo
      items={items}
      texto={(s) => s.clave + ' ' + s.nombre}
      estado={(s) => s.estatus}
      opciones={OPCIONES_CATALOGO}
    >
      {(visibles) => (
        <ul className="divide-y" role="list" aria-label="Lista de sucursales">
          {visibles.map((s) => (
            <ItemCompacto key={s.id} item={s} activo={s.id === idActivo} />
          ))}
        </ul>
      )}
    </ListadoCatalogo>
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
          activo ? 'bg-brand/10 hover:bg-brand/15' : 'hover:bg-surface-muted/40',
        )}
        aria-current={activo ? 'page' : undefined}
      >
        <div className="flex items-start justify-between gap-2">
          <div className="min-w-0 flex-1">
            <div className="flex items-center gap-1.5">
              <span
                className={cn(
                  'truncate font-mono text-sm',
                  activo ? 'font-semibold text-ink' : 'text-ink',
                )}
              >
                {item.clave}
              </span>
              <Badge variant="neutral">
                {item.tipo === TipoSucursal.Planta ? 'Planta' : 'Taller'}
              </Badge>
              {esInactiva ? (
                <Badge variant="warning">Inactiva</Badge>
              ) : (
                <Badge variant="neutral">Activa</Badge>
              )}
            </div>
            <p className="truncate text-xs text-ink-muted">{item.nombre}</p>
          </div>
          <div className="flex shrink-0 items-center pt-1 text-ink-muted">
            <ChevronRight className="h-4 w-4" aria-hidden="true" />
          </div>
        </div>
      </Link>
    </li>
  );
}
