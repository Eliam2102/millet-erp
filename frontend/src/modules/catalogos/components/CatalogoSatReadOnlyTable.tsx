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
  'Catálogo SAT mantenido vía migración. No editable desde la UI.';

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
  return (
    <div className="space-y-4 p-4">
      <h1 className="text-xl font-semibold tracking-tight">{titulo}</h1>

      <div
        role="note"
        className="flex items-start gap-2 rounded-md border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900 dark:border-amber-700 dark:bg-amber-900/20 dark:text-amber-200"
      >
        <Info className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
        <p>{banner}</p>
      </div>

      {isLoading ? (
        <TableSkeleton
          rows={6}
          columns={columns.map(() => ({ width: 'w-full' as const }))}
        />
      ) : isError ? (
        <ErrorState
          problem={esApiError(error) ? error.problem : undefined}
          onRetry={onRetry}
        />
      ) : items.length === 0 ? (
        <p className="rounded-md border border-dashed bg-muted/20 p-6 text-center text-sm text-muted-foreground">
          {emptyMessage}
        </p>
      ) : (
        <div className="overflow-x-auto rounded-md border bg-card">
          <table className="w-full text-sm">
            <thead className="border-b bg-muted/30 text-xs uppercase text-muted-foreground">
              <tr>
                {columns.map((c) => (
                  <th
                    key={c.key}
                    scope="col"
                    className={cn('px-3 py-2 text-left', c.className)}
                  >
                    {c.label}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y">
              {items.map((item) => (
                <tr key={getRowKey(item)}>
                  {columns.map((c) => (
                    <td key={c.key} className={cn('px-3 py-2', c.className)}>
                      {c.render
                        ? c.render(item)
                        : String((item as Record<string, unknown>)[c.key] ?? '')}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
