import { Table, TableBody, TableRow, TableCell } from '@/components/ui/table';
import { PaginacionTabla, BarraFiltros } from '@/components/erp';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { usePaginacionLocal } from '../lib/use-paginacion-local';
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

export function ListaMonedasCompacta({ items, idActivo }: ListaMonedasCompactaProps) {
  const pagina = usePaginacionLocal(items, items.map((item) => item.id).join(','));
  return (
    <ListadoModulo
      filtros={<BarraFiltros total={`${items.length} registros`} />}
      pie={
        <PaginacionTabla
          total={items.length}
          offset={pagina.offset}
          limit={10}
          tamanos={[]}
          onChange={pagina.onChange}
        />
      }
    >
      <Table aria-label="Lista de monedas">
        <TableBody>
          {pagina.visibles.map((m) => (
            <ItemCompacto key={m.id} item={m} activo={m.id === idActivo} />
          ))}
        </TableBody>
      </Table>
    </ListadoModulo>
  );
}

interface ItemCompactoProps {
  item: MonedaResponse;
  activo: boolean;
}

function ItemCompacto({ item, activo }: ItemCompactoProps) {
  return (
    <TableRow>
      <TableCell className="p-0">
        <Link
          to="/admin/catalogos/monedas/$id"
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
              {item.activa ? (
                <Badge variant="success">Activa</Badge>
              ) : (
                <Badge variant="neutral" className="text-ink-muted">
                  Inactiva
                </Badge>
              )}
              <ChevronRight className="h-4 w-4 text-ink-muted shrink-0" />
            </div>
          </div>
          <div className="mt-1">
            <span className="truncate text-xs text-ink-muted">{item.nombre}</span>
          </div>
        </Link>
      </TableCell>
    </TableRow>
  );
}
