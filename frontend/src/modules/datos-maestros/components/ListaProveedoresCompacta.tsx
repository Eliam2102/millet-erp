import { Link } from '@tanstack/react-router';
import { ChevronRight } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import {
  EstatusCatalogo,
  type ProveedorItem,
} from '@/modules/datos-maestros/api/types';
import { cn } from '@/lib/utils';

/**
 * Lista compacta master 320px del módulo Datos Maestros → Proveedores.
 * Estilo inbox: cada item muestra clave (mono) + razón social + badge
 * de estatus. El item activo se highlightea con <c>bg-primary/10</c>,
 * mismo patrón que <see cref="ListaEmpresasCompacta"/>.
 */
export interface ListaProveedoresCompactaProps {
  items: readonly ProveedorItem[];
  idActivo: string | null;
}

export function ListaProveedoresCompacta({
  items,
  idActivo,
}: ListaProveedoresCompactaProps) {
  return (
    <ul className="divide-y" role="list" aria-label="Lista de proveedores">
      {items.map((p) => (
        <ItemCompacto key={p.id} item={p} activo={p.id === idActivo} />
      ))}
    </ul>
  );
}

interface ItemCompactoProps {
  item: ProveedorItem;
  activo: boolean;
}

function ItemCompacto({ item, activo }: ItemCompactoProps) {
  return (
    <li>
      <Link
        to="/admin/datos-maestros/proveedores/$id"
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
            <EstatusBadge estatus={item.estatus} />
            <ChevronRight className="h-4 w-4 text-muted-foreground shrink-0" />
          </div>
        </div>
        <div className="mt-1 truncate text-xs text-muted-foreground">
          {item.razonSocial}
        </div>
      </Link>
    </li>
  );
}

function EstatusBadge({ estatus }: { estatus: number }) {
  if (estatus === EstatusCatalogo.Activo) {
    return <Badge variant="secondary">Activo</Badge>;
  }
  if (estatus === EstatusCatalogo.EnRevision) {
    return <Badge variant="outline">En revisión</Badge>;
  }
  return (
    <Badge variant="outline" className="text-muted-foreground">
      Inactivo
    </Badge>
  );
}
