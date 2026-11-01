import type { UseInfiniteQueryResult } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import { ErrorState, TableSkeleton } from '@/components/erp';
import { Badge } from '@/components/ui/badge';
import { esApiError } from '@/lib/api';
import { estadoUi } from '../lib/estados-ui';

/**
 * Contenido denso de una fila: una línea con clave mono, nombre, aviso «Incompleto» y
 * estado; <c>sub</c> añade una segunda línea opcional (origen, clasificación…). Va
 * dentro del <c>Link</c> de cada entidad.
 */
export function FilaMaestroContenido({
  clave,
  titulo,
  activo,
  incompleto = false,
  etiquetaIncompleto = 'Incompleto',
  derecha,
  sub,
}: {
  clave: string;
  titulo: string;
  activo: boolean;
  incompleto?: boolean;
  etiquetaIncompleto?: string;
  derecha?: ReactNode;
  sub?: ReactNode;
}) {
  return (
    <>
      <div className="flex items-center gap-2">
        <span
          className={cn('w-28 shrink-0 truncate font-mono', activo ? 'font-semibold' : 'text-ink')}
          title={clave}
        >
          {clave}
        </span>
        <span className="min-w-0 flex-1 truncate text-xs text-ink-secondary" title={titulo}>
          {titulo}
        </span>
        {incompleto && (
          <Badge variant={estadoUi('fiscal', 'Incompleto').tono} className="shrink-0 py-0 text-2xs">
            {etiquetaIncompleto}
          </Badge>
        )}
        {derecha}
      </div>
      {sub != null && (
        <div className="mt-0.5 flex flex-wrap items-center gap-x-2 pl-[7.5rem] text-2xs text-ink-muted">
          {sub}
        </div>
      )}
    </>
  );
}

/** Lista de filas (ul/li) de una entidad; cada hijo es el enlace de una fila. */
export function MaestroFilas({ etiqueta, children }: { etiqueta: string; children: ReactNode[] }) {
  return (
    <ul aria-label={etiqueta} className="divide-y divide-line-divider">
      {children.map((c, i) => (
        <li key={i}>{c}</li>
      ))}
    </ul>
  );
}

/**
 * Cuerpo común de las listas de Datos Maestros: carga, error, vacío, la lista con
 * scroll, contador «N de Total» y «Cargar más» (siguiente página de la API).
 */
export function MaestroListaCuerpo({
  query,
  cargados,
  total,
  plural,
  vacio,
  children,
}: {
  query: UseInfiniteQueryResult<unknown>;
  cargados: number;
  total: number;
  /** «clientes», «proveedores»… para el contador. */
  plural: string;
  vacio: ReactNode;
  children: ReactNode;
}) {
  if (query.isLoading) {
    return (
      <div className="p-3">
        <TableSkeleton rows={8} columns={[{ width: 'w-full' }, { width: 'w-full' }]} />
      </div>
    );
  }
  if (query.isError && !query.isFetchNextPageError) {
    const problem = esApiError(query.error) ? query.error.problem : undefined;
    return <ErrorState problem={problem} onRetry={() => void query.refetch()} />;
  }
  if (cargados === 0) return <>{vacio}</>;

  return (
    <>
      <div className="min-h-0 flex-1 overflow-y-auto">{children}</div>
      <div className="flex shrink-0 flex-wrap items-center justify-between gap-2 border-t border-line-divider px-3 py-2 text-xs text-ink-muted">
        <p role="status" aria-live="polite" className="tabular-nums">
          {cargados} de {total} {plural}
        </p>
        {query.hasNextPage && (
          <Button
            type="button"
            variant="outline"
            size="sm"
            disabled={query.isFetchingNextPage}
            onClick={() => void query.fetchNextPage()}
          >
            {query.isFetchingNextPage
              ? 'Cargando…'
              : query.isFetchNextPageError
                ? 'No se pudo cargar. Reintentar'
                : 'Cargar más'}
          </Button>
        )}
      </div>
    </>
  );
}
