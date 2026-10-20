import { useState } from 'react';
import { BarraFiltros, PaginacionTabla } from '@/components/erp';
import { ListadoModulo } from '@/components/erp/data/ListadoModulo';
import { Input } from '@/components/ui/input';
import { usePaginacionLocal } from '../lib/use-paginacion-local';
import {
  Table,
  TableHeader,
  TableRow,
  TableHead,
  TableBody,
  TableCell,
} from '@/components/ui/table';
import { PageHeader } from '@/components/erp/PageHeader';
import { type ReactNode } from 'react';
import { Info } from 'lucide-react';
import { ErrorState, TableSkeleton } from '@/components/erp';
import { esApiError } from '@/lib/api';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;CatalogoSatReadOnlyTable/&gt;</c> — render compartido para los
 * 3 catálogos SAT del Grupo 3 (FormasPago, UsosCfdi,
 * RegimenesFiscales). Banner amarillo arriba + tabla read-only sin
 * botones de mutación.
 */
export interface SatColumn<T> {
  key: string;
  label: string;
  render?: (item: T) => ReactNode;
  className?: string;
}

export interface CatalogoSatReadOnlyTableProps<T> {
  titulo: string;
  items: readonly T[];
  isLoading: boolean;
  isError: boolean;
  error: unknown;
  onRetry: () => void;
  columns: ReadonlyArray<SatColumn<T>>;
  /** Texto explicativo del banner. Se enmarca con icono Info. */
  banner?: string;
  /** Function que devuelve la key estable de cada fila. */
  getRowKey: (item: T) => string;
  /** Mensaje cuando no hay items (caso raro: el seed siempre los carga). */
  emptyMessage?: string;
}

const DEFAULT_BANNER =
  'Catálogo SAT de consulta. Las actualizaciones se administran de forma centralizada.';

export function CatalogoSatReadOnlyTable<T>({
  titulo,
  items,
  isLoading,
  isError,
  error,
  onRetry,
  columns,
  banner = DEFAULT_BANNER,
  getRowKey,
  emptyMessage = 'Aún no hay registros.',
}: CatalogoSatReadOnlyTableProps<T>) {
  const [busqueda, setBusqueda] = useState('');
  const filtrados = items.filter((item) =>
    columns.some((c) =>
      String((item as Record<string, unknown>)[c.key] ?? '')
        .toLocaleLowerCase('es-MX')
        .includes(busqueda.toLocaleLowerCase('es-MX')),
    ),
  );
  const pagina = usePaginacionLocal(filtrados, busqueda);
  return (
    <div className="space-y-5">
      <PageHeader titulo={<>{titulo}</>} />

      <div
        role="note"
        className="flex items-start gap-2 rounded-md bg-warning-note-bg p-3 text-sm text-warning-note-fg"
      >
        <Info className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
        <p>{banner}</p>
      </div>

      <ListadoModulo
        filtros={
          <BarraFiltros
            busqueda={
              <Input
                aria-label="Buscar en el catálogo"
                placeholder={columns.map((c) => c.label).join(', ')}
                value={busqueda}
                onChange={(e) => setBusqueda(e.target.value)}
              />
            }
            total={`${filtrados.length} registros`}
          />
        }
        pie={
          <PaginacionTabla
            total={filtrados.length}
            offset={pagina.offset}
            limit={10}
            tamanos={[]}
            onChange={pagina.onChange}
          />
        }
      >
        {isLoading ? (
          <TableSkeleton rows={6} columns={columns.map(() => ({ width: 'w-full' as const }))} />
        ) : isError ? (
          <ErrorState problem={esApiError(error) ? error.problem : undefined} onRetry={onRetry} />
        ) : items.length === 0 ? (
          <p className="rounded-md border border-dashed bg-surface-muted/20 p-6 text-center text-sm text-ink-muted">
            {emptyMessage}
          </p>
        ) : (
          <div className="overflow-x-auto rounded-lg bg-surface-card shadow-card-flat">
            <Table className="w-full text-sm">
              <TableHeader className="border-b bg-surface-subtle text-xs uppercase text-ink-muted">
                <TableRow>
                  {columns.map((c) => (
                    <TableHead
                      key={c.key}
                      scope="col"
                      className={cn('px-3 py-2 text-left', c.className)}
                    >
                      {c.label}
                    </TableHead>
                  ))}
                </TableRow>
              </TableHeader>
              <TableBody className="divide-y">
                {pagina.visibles.map((item) => (
                  <TableRow key={getRowKey(item)}>
                    {columns.map((c) => (
                      <TableCell key={c.key} className={cn('px-3 py-2', c.className)}>
                        {c.render
                          ? c.render(item)
                          : String((item as Record<string, unknown>)[c.key] ?? '')}
                      </TableCell>
                    ))}
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </ListadoModulo>
    </div>
  );
}
