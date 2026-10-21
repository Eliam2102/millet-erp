import { estadoUi } from '../lib/estados-ui';
import { Table, TableBody, TableRow, TableCell } from '@/components/ui/table';
import { PaginacionTabla, BarraFiltros } from '@/components/erp';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { usePaginacionLocal } from '../lib/use-paginacion-local';
import { Link } from '@tanstack/react-router';
import { ChevronRight } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { EstatusCatalogo, Naturaleza, type ArticuloItem } from '@/modules/datos-maestros/api/types';
import { cn } from '@/lib/utils';

/**
 * Lista compacta master 320px del módulo Datos Maestros → Artículos.
 * Item: clave (mono) + nombre + badge de naturaleza + badge de
 * estatus + ChevronRight. Mismo patrón que
 * <see cref="ListaProveedoresCompacta"/>.
 */
export interface ListaArticulosCompactaProps {
  items: readonly ArticuloItem[];
  idActivo: string | null;
}

export function ListaArticulosCompacta({ items, idActivo }: ListaArticulosCompactaProps) {
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
      <Table aria-label="Lista de artículos">
        <TableBody>
          {pagina.visibles.map((a) => (
            <ItemCompacto key={a.id} item={a} activo={a.id === idActivo} />
          ))}
        </TableBody>
      </Table>
    </ListadoModulo>
  );
}

interface ItemCompactoProps {
  item: ArticuloItem;
  activo: boolean;
}

function ItemCompacto({ item, activo }: ItemCompactoProps) {
  return (
    <TableRow>
      <TableCell className="p-0">
        <Link
          to="/admin/datos-maestros/articulos/$id"
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
          <div className="mt-1 flex items-center gap-1.5">
            <span className="truncate text-xs text-ink-muted">{item.nombre}</span>
            <NaturalezaBadge naturaleza={item.naturaleza} />
          </div>
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

function NaturalezaBadge({ naturaleza }: { naturaleza: number }) {
  const nombre =
    Object.entries(Naturaleza).find(([, valor]) => valor === naturaleza)?.[0] ?? 'Estandar';
  const ui = estadoUi('naturaleza', nombre);
  return <Badge variant={ui.tono}>{ui.texto}</Badge>;
}
