import type { ReactNode } from 'react';
import { Link } from '@tanstack/react-router';
import { ChevronRight } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import type { PendienteConfig } from '../config';
import type { ConteoInicio } from '../api/useConteosInicio';

export interface EstadoFila {
  isPending: boolean;
  isError: boolean;
  isFetching: boolean;
  refetch: () => unknown;
}

export function FilaPendiente({
  fila,
  query,
  conteo,
  estado,
  etiquetaEstado,
}: {
  fila: PendienteConfig;
  query?: EstadoFila;
  conteo?: ConteoInicio;
  estado?: ReactNode;
  etiquetaEstado?: string;
}) {
  const cargando = query?.isPending;
  const error = query?.isError;
  const etiqueta = cargando
    ? 'Cargando'
    : error
      ? 'No se pudo cargar'
      : (etiquetaEstado ??
        (conteo ? (conteo.total > 0 ? `${conteo.total} pendientes` : 'Sin pendientes') : 'Abrir'));
  return (
    <li className="flex flex-wrap items-center border-t border-line-row">
      <Link
        to={fila.to}
        search={fila.search ?? {}}
        aria-label={`${fila.titulo}: ${etiqueta}`}
        aria-busy={cargando || undefined}
        className="flex min-h-16 min-w-0 flex-1 flex-wrap items-center gap-3 rounded-md px-4 py-3 text-ink hover:bg-surface-subtle focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand"
      >
        <Badge
          variant="neutral"
          className="rounded-sm bg-surface-muted text-2xs text-ink-strong sm:w-26 sm:shrink-0"
        >
          {fila.modulo}
        </Badge>
        <div className="min-w-0 flex-1 basis-32">
          <p className="text-sm font-medium">{fila.titulo}</p>
          <p className="text-xs text-ink-muted">{fila.descripcion}</p>
        </div>
        <div className="ml-auto text-right">
          {cargando ? (
            <Skeleton className="h-5 w-16 bg-surface-muted" />
          ) : error ? (
            <span className="text-xs text-danger-fg">No se pudo cargar</span>
          ) : (
            (estado ??
            (conteo && conteo.total > 0 ? (
              <span className="text-lg font-semibold tabular-nums">{conteo.total}</span>
            ) : (
              <span className="text-xs text-ink-muted">{etiqueta}</span>
            )))
          )}
          {!cargando && !error && conteo?.atrasadas !== undefined && (
            <p
              className={
                conteo.atrasadas > 0
                  ? 'text-xs tabular-nums text-danger-fg font-medium'
                  : 'text-xs tabular-nums text-ink-muted'
              }
            >
              {conteo.atrasadas} atrasadas
            </p>
          )}
        </div>
        <ChevronRight
          aria-hidden="true"
          className="size-4 shrink-0 text-ink-subtle"
          strokeWidth={1.6}
        />
      </Link>
      {error && query && (
        <Button
          variant="outline"
          size="sm"
          className="m-3"
          disabled={query.isFetching}
          aria-label={`Reintentar ${fila.titulo}`}
          onClick={() => {
            void query.refetch();
          }}
        >
          Reintentar
        </Button>
      )}
    </li>
  );
}
