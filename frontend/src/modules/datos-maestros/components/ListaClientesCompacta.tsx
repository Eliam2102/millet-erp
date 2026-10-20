import { Table, TableBody, TableRow, TableCell } from '@/components/ui/table';
import { PaginacionTabla, BarraFiltros } from '@/components/erp';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { usePaginacionLocal } from '../lib/use-paginacion-local';
import { Link } from '@tanstack/react-router';
import { ChevronRight } from 'lucide-react';
import type { ClienteItem } from '@/modules/datos-maestros/api/types';
import {
  EstatusCatalogoBadge,
  FiscalesIncompletosBadge,
  OrigenBadge,
  ResultadoSincronizacionBadge,
} from '@/modules/datos-maestros/components/master-badges';
import { cn } from '@/lib/utils';

/**
 * Lista compacta master 320px del módulo Datos Maestros → Clientes
 * (ADR-0048). Item: clave (mono) + razón social + badges de estatus,
 * origen (A+W/Manual) y el chip ámbar "Fiscales incompletos" cuando el
 * registro aún no puede timbrar. Mismo patrón que
 * <see cref="ListaProveedoresCompacta"/>.
 */
export interface ListaClientesCompactaProps {
  items: readonly ClienteItem[];
  idActivo: string | null;
}

export function ListaClientesCompacta({ items, idActivo }: ListaClientesCompactaProps) {
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
      <Table aria-label="Lista de clientes">
        <TableBody>
          {pagina.visibles.map((c) => (
            <ItemCompacto key={c.id} item={c} activo={c.id === idActivo} />
          ))}
        </TableBody>
      </Table>
    </ListadoModulo>
  );
}

interface ItemCompactoProps {
  item: ClienteItem;
  activo: boolean;
}

function ItemCompacto({ item, activo }: ItemCompactoProps) {
  return (
    <TableRow>
      <TableCell className="p-0">
        <Link
          to="/admin/datos-maestros/clientes/$id"
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
              <EstatusCatalogoBadge estatus={item.estatus} />
              <ChevronRight className="h-4 w-4 text-ink-muted shrink-0" />
            </div>
          </div>
          <div className="mt-1 truncate text-xs text-ink-muted">{item.razonSocial}</div>
          <div className="mt-1 flex flex-wrap items-center gap-1">
            <OrigenBadge comoTexto origen={item.origen} className="text-2xs py-0" />
            <FiscalesIncompletosBadge
              completos={item.datosFiscalesCompletos}
              className="text-2xs py-0"
            />
            {item.origenAw != null && (
              <ResultadoSincronizacionBadge
                comoTexto
                resultado={item.origenAw.resultado}
                className="text-2xs py-0"
              />
            )}
            {item.clasificacionId == null ? (
              <span className="text-xs text-warning-fg">Sin clasificar</span>
            ) : (
              item.clasificacionNombre && (
                <span className="text-2xs text-ink-muted">{item.clasificacionNombre}</span>
              )
            )}
          </div>
        </Link>
      </TableCell>
    </TableRow>
  );
}
