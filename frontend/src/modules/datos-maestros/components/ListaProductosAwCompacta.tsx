import { Table, TableBody, TableRow, TableCell } from '@/components/ui/table';
import { PaginacionTabla, BarraFiltros } from '@/components/erp';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { usePaginacionLocal } from '../lib/use-paginacion-local';
import { Link } from '@tanstack/react-router';
import { ChevronRight } from 'lucide-react';
import type { ProductoAwItem } from '@/modules/datos-maestros/api/types';
import {
  EstatusCatalogoBadge,
  FiscalesIncompletosBadge,
  OrigenBadge,
} from '@/modules/datos-maestros/components/master-badges';
import { cn } from '@/lib/utils';

/**
 * Lista compacta master 320px del módulo Datos Maestros → Productos
 * A+W (ADR-0048). Item: referencia externa (mono) + descripción +
 * badges de estatus, origen y el chip ámbar "Fiscales incompletos"
 * cuando faltan claves SAT. Mismo patrón que
 * <see cref="ListaClientesCompacta"/>.
 */
export interface ListaProductosAwCompactaProps {
  items: readonly ProductoAwItem[];
  idActivo: string | null;
}

export function ListaProductosAwCompacta({ items, idActivo }: ListaProductosAwCompactaProps) {
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
      <Table aria-label="Lista de productos A+W">
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
  item: ProductoAwItem;
  activo: boolean;
}

function ItemCompacto({ item, activo }: ItemCompactoProps) {
  return (
    <TableRow>
      <TableCell className="p-0">
        <Link
          to="/admin/datos-maestros/productos-aw/$id"
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
              {item.referenciaExterna}
            </span>
            <div className="flex shrink-0 items-center gap-1">
              <EstatusCatalogoBadge estatus={item.estatus} />
              <ChevronRight className="h-4 w-4 text-ink-muted shrink-0" />
            </div>
          </div>
          <div className="mt-1 truncate text-xs text-ink-muted">{item.descripcion}</div>
          <div className="mt-1 flex flex-wrap items-center gap-1">
            <OrigenBadge comoTexto origen={item.origen} className="text-2xs py-0" />
            <FiscalesIncompletosBadge
              completos={item.datosFiscalesCompletos}
              className="text-2xs py-0"
            />
            <span className="text-2xs text-ink-muted">
              UC {item.unidadMedida}
              {(item.numVariantes ?? 0) > 1 ? ` · ${item.numVariantes} medidas` : ''}
            </span>
            {item.esServicioRecubrimiento && (
              <span className="text-2xs font-medium text-brand">Servicio de recubrimiento</span>
            )}
            {(item.numComponentes ?? 0) > 0 && (
              <span className="text-2xs text-ink-muted">{item.numComponentes} piezas</span>
            )}
          </div>
        </Link>
      </TableCell>
    </TableRow>
  );
}
