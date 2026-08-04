import { Link } from '@tanstack/react-router';
import { ChevronRight } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import type { EmpresaResponse } from '@/modules/administracion/api/types';
import { cn } from '@/lib/utils';

/**
 * Lista compacta master 320px del módulo Administración → Empresas.
 * Estilo inbox: cada item muestra RFC + razón social + badge de
 * estado (Activa/Inactiva). El item activo (cuando estamos en
 * master-detail con un id en URL) se highlightea con
 * <c>bg-primary/10</c>, igual que el patrón exemplar de Compras.
 */
export interface ListaEmpresasCompactaProps {
  items: readonly EmpresaResponse[];
  idActivo: string | null;
}

export function ListaEmpresasCompacta({
  items,
  idActivo,
}: ListaEmpresasCompactaProps) {
  return (
    <ul className="divide-y" role="list" aria-label="Lista de empresas">
      {items.map((e) => (
        <ItemCompacto key={e.id} item={e} activo={e.id === idActivo} />
      ))}
    </ul>
  );
}

interface ItemCompactoProps {
  item: EmpresaResponse;
  activo: boolean;
}

function ItemCompacto({ item, activo }: ItemCompactoProps) {
  return (
    <li>
      <Link
        to="/admin/empresas/$id"
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
            {item.rfc}
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
        <div className="mt-1 truncate text-xs text-muted-foreground">
          {item.razonSocial}
        </div>
      </Link>
    </li>
  );
}
