import { Skeleton } from '@/components/ui/skeleton';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;DetalleSkeleton/&gt;</c> — skeleton de páginas de detalle: encabezado
 * (título + badge) seguido de una lista de definición (<c>dl</c>) de pares
 * etiqueta/valor.
 */
export interface DetalleSkeletonProps {
  /** Número de pares etiqueta/valor. Default 6. */
  campos?: number;
  className?: string;
}

export function DetalleSkeleton({ campos = 6, className }: DetalleSkeletonProps) {
  return (
    <div
      role="status"
      aria-busy="true"
      aria-label="Cargando detalle"
      className={cn('space-y-6', className)}
    >
      <div className="flex items-center gap-3">
        <Skeleton className="h-7 w-48" />
        <Skeleton className="h-5 w-20 rounded-full" />
      </div>
      <dl className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {Array.from({ length: campos }).map((_, i) => (
          <div key={i} className="space-y-1.5">
            <dt>
              <Skeleton className="h-3 w-24" />
            </dt>
            <dd>
              <Skeleton className="h-4 w-40" />
            </dd>
          </div>
        ))}
      </dl>
    </div>
  );
}
