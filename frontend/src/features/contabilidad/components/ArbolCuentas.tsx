import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { ChevronRight } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { cn } from '@/lib/utils';
import { useArbol } from '../api/hooks';
import type { FiltroEstatus, NodoArbol } from '../api/types';
import { InsigniasCuenta } from './InsigniasCuenta';

interface Props {
  estatus: FiltroEstatus;
  /** Se invoca solo con el catálogo vacío (nivel raíz sin filas) para ofrecer el CTA de importar. */
  vacio: React.ReactNode;
}

/** Árbol perezoso por `raizId`: una consulta por nodo expandido (cacheada). Roles ARIA tree/treeitem. */
export function ArbolCuentas({ estatus, vacio }: Props) {
  return (
    <div className="overflow-x-auto rounded-lg bg-surface-card shadow-card-flat">
      <ul role="tree" aria-label="Catálogo de cuentas" className="min-w-[560px] text-sm">
        <Hijos raizId={null} nivel={0} estatus={estatus} vacio={vacio} />
      </ul>
    </div>
  );
}

function Hijos({ raizId, nivel, estatus, vacio }: { raizId: string | null; nivel: number; estatus: FiltroEstatus; vacio?: React.ReactNode }) {
  const q = useArbol(raizId, estatus);
  const sangria = { paddingLeft: nivel * 20 + 12 };

  if (q.isLoading) {
    return (
      <li role="none" aria-label="Cargando cuentas" data-testid="arbol-cargando" className="space-y-1 py-2" style={sangria}>
        <Skeleton className="h-5 w-2/3" />
        <Skeleton className="h-5 w-1/2" />
      </li>
    );
  }
  if (q.isError) {
    return (
      <li role="none" className="flex items-center gap-2 py-2 text-danger-fg" style={sangria}>
        <span>No se pudo cargar este nivel.</span>
        <Button variant="ghost" size="sm" onClick={() => void q.refetch()}>Reintentar</Button>
      </li>
    );
  }
  const nodos = q.data ?? [];
  if (nodos.length === 0) {
    return (
      <li role="none" className="py-3 text-ink-muted" style={sangria}>
        {nivel === 0 ? vacio : 'Sin cuentas hijas.'}
      </li>
    );
  }
  return (
    <>
      {nodos.map((n) => (
        <Fila key={n.id} nodo={n} nivel={nivel} estatus={estatus} />
      ))}
    </>
  );
}

function Fila({ nodo, nivel, estatus }: { nodo: NodoArbol; nivel: number; estatus: FiltroEstatus }) {
  const [abierto, setAbierto] = useState(false);
  return (
    <li role="treeitem" aria-expanded={nodo.tieneHijos ? abierto : undefined}>
      <div className={cn('flex items-center gap-2 border-b border-line-row px-3 py-1.5 text-sm hover:bg-surface-subtle', !nodo.activa && 'text-ink-muted')} style={{ paddingLeft: nivel * 20 + 12 }}>
        {nodo.tieneHijos ? (
          <button
            type="button"
            className="flex h-5 w-5 shrink-0 items-center justify-center rounded-sm hover:bg-surface-muted"
            aria-label={`${abierto ? 'Colapsar' : 'Expandir'} ${nodo.codigo}`}
            onClick={() => setAbierto((v) => !v)}
          >
            <ChevronRight className={cn('size-4 transition-transform', abierto && 'rotate-90')} aria-hidden="true" />
          </button>
        ) : (
          <span className="h-5 w-5 shrink-0" aria-hidden="true" />
        )}
        <Link to="/contabilidad/catalogo/$id" params={{ id: nodo.id }} className="flex min-w-0 flex-1 items-center gap-2 hover:underline">
          <span className="shrink-0 font-mono text-xs">{nodo.codigo}</span>
          <span className="truncate">{nodo.nombre}</span>
        </Link>
        <InsigniasCuenta tipo={nodo.tipo} activa={nodo.activa} pendienteValidacion={nodo.pendienteValidacion} />
      </div>
      {nodo.tieneHijos && abierto && (
        <ul role="group">
          <Hijos raizId={nodo.id} nivel={nivel + 1} estatus={estatus} />
        </ul>
      )}
    </li>
  );
}
