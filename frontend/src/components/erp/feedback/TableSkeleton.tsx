import { Skeleton } from '@/components/ui/skeleton';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;TableSkeleton/&gt;</c> — skeleton "muy parecido a una tabla" para
 * estados de loading de bandejas. Doc 05 §13.1.
 *
 * <para>Configurable por <c>rows</c> (default 8) y por <c>columns</c>
 * (array de definiciones que controla el ancho relativo de cada celda).
 * No pretende calzar pixel-perfect con la tabla real — es un placeholder
 * "blocky" que evita el flash de "no hay nada" mientras la query
 * resuelve.</para>
 *
 * @example
 * ```tsx
 * <TableSkeleton rows={6} columns={[
 *   { width: 'w-24', label: 'Folio' },
 *   { width: 'w-32', label: 'Fecha' },
 *   { width: 'w-48', label: 'Requisitante' },
 *   { width: 'w-24', label: 'Monto' },
 *   { width: 'w-20', label: 'Estado' },
 * ]} />
 * ```
 */
export interface TableSkeletonColumn {
  /** Tailwind width class para la columna (ej. <c>'w-24'</c>). */
  width: string;
  /** Etiqueta accesible (no se renderiza visualmente). */
  label?: string;
}

export interface TableSkeletonProps {
  /** Número de filas a esqueletizar. Default 8. */
  rows?: number;
  /** Definición de columnas. Si se omite, usa 5 columnas estándar. */
  columns?: TableSkeletonColumn[];
  /** Override de clases del wrapper exterior. */
  className?: string;
}

const COLUMNAS_DEFAULT: TableSkeletonColumn[] = [
  { width: 'w-24' },
  { width: 'w-32' },
  { width: 'w-48' },
  { width: 'w-24' },
  { width: 'w-20' },
];

export function TableSkeleton({
  rows = 8,
  columns = COLUMNAS_DEFAULT,
  className,
}: TableSkeletonProps) {
  return (
    <div
      role="status"
      aria-busy="true"
      aria-label="Cargando tabla"
      className={cn('space-y-2', className)}
    >
      {/* Header */}
      <div className="flex gap-4 border-b px-2 py-3">
        {columns.map((col, i) => (
          <Skeleton
            key={`h-${i}`}
            className={cn('h-3', col.width)}
            aria-label={col.label}
          />
        ))}
      </div>
      {/* Filas */}
      {Array.from({ length: rows }).map((_, rowIdx) => (
        <div key={`r-${rowIdx}`} className="flex gap-4 px-2 py-2">
          {columns.map((col, colIdx) => (
            <Skeleton key={`r-${rowIdx}-c-${colIdx}`} className={cn('h-4', col.width)} />
          ))}
        </div>
      ))}
    </div>
  );
}
