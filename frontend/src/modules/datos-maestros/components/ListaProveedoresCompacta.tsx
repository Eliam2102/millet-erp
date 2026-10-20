import { estadoUi } from '../lib/estados-ui';
import { Table, TableBody, TableRow, TableCell } from '@/components/ui/table';
import { PaginacionTabla, BarraFiltros } from '@/components/erp';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { usePaginacionLocal } from '../lib/use-paginacion-local';
import { Link } from '@tanstack/react-router';
import { ChevronRight } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { EstatusCatalogo, type ProveedorItem } from '@/modules/datos-maestros/api/types';
import { cn } from '@/lib/utils';

/**
 * Lista compacta master 320px del módulo Datos Maestros → Proveedores.
 * Estilo inbox: cada item muestra clave (mono) + razón social + badge
 * de estatus. El item activo se highlightea con <c>bg-brand/10</c>,
 * mismo patrón que <see cref="ListaEmpresasCompacta"/>.
 */
export interface ListaProveedoresCompactaProps {
  items: readonly ProveedorItem[];
  idActivo: string | null;
}

export function ListaProveedoresCompacta({ items, idActivo }: ListaProveedoresCompactaProps) {
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
      <Table aria-label="Lista de proveedores">
        <TableBody>
          {pagina.visibles.map((p) => (
            <ItemCompacto key={p.id} item={p} activo={p.id === idActivo} />
          ))}
        </TableBody>
      </Table>
    </ListadoModulo>
  );
}

interface ItemCompactoProps {
  item: ProveedorItem;
  activo: boolean;
}

function ItemCompacto({ item, activo }: ItemCompactoProps) {
  return (
    <TableRow>
      <TableCell className="p-0">
        <Link
          to="/admin/datos-maestros/proveedores/$id"
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
              {item.clave}
            </span>
            <div className="flex shrink-0 items-center gap-1">
              <EstatusBadge estatus={item.estatus} />
              <ChevronRight className="h-4 w-4 text-ink-muted shrink-0" />
            </div>
          </div>
          <div className="mt-1 truncate text-xs text-ink-muted">{item.razonSocial}</div>
        </Link>
      </TableCell>
    </TableRow>
  );
}

function EstatusBadge({ estatus }: { estatus: number }) {
  const ui = estadoUi(
    'catalogo',
    estatus === EstatusCatalogo.Activo
      ? 'Activo'
      : estatus === EstatusCatalogo.EnRevision
        ? 'EnRevision'
        : 'Inactivo',
  );
  return <Badge variant={ui.tono}>{ui.texto}</Badge>;
}
