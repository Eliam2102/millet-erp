import { useState } from 'react';
import { ChevronRight } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import { useHijosJerarquia } from '@/features/almacen/api/useSaldosCierreReportes';
import type {
  NivelNodoJerarquia,
  NodoJerarquiaSaldo,
} from '@/features/almacen/api/types';

/**
 * Árbol de la consulta jerárquica de saldos (PR6, ADR-0047). Carga PEREZOSA:
 * cada nivel se pide al backend solo al expandirse (una llamada acotada por
 * nodo, cacheada por TanStack Query). Auto-expande la CADENA ÚNICA: mientras
 * un nivel tenga un solo hijo no-hoja, se abre solo — con 1 sucursal / 1
 * almacén el usuario aterriza directo donde ramifica.
 *
 * El rollup (cantidad + valor) viene calculado del backend; el FE no suma.
 * La ÚNICA se distingue por badge (esDefault), no por posición.
 */

interface ArbolSaldosProps {
  /** Nodo cuyo contenido se muestra: la raíz global o un salto directo. */
  nodoTipo: NivelNodoJerarquia;
  nodoId?: string;
  /** Modo artículo: filtra todas las ramas; la ubicación es hoja. */
  articuloId?: string;
  incluirVacios?: boolean;
}

export function ArbolSaldos({
  nodoTipo,
  nodoId,
  articuloId,
  incluirVacios,
}: ArbolSaldosProps) {
  return (
    <ul role="tree" className="space-y-0.5 text-sm">
      <ListaHijos
        nodoTipo={nodoTipo}
        nodoId={nodoId}
        articuloId={articuloId}
        incluirVacios={incluirVacios}
        nivel={0}
      />
    </ul>
  );
}

interface ListaHijosProps {
  nodoTipo: NivelNodoJerarquia;
  nodoId?: string;
  articuloId?: string;
  incluirVacios?: boolean;
  nivel: number;
}

/** Los hijos de un nodo: una llamada lazy; skeleton/error/empty por nivel. */
function ListaHijos({
  nodoTipo,
  nodoId,
  articuloId,
  incluirVacios,
  nivel,
}: ListaHijosProps) {
  const query = useHijosJerarquia({
    nodoTipo,
    nodoId,
    articuloId,
    incluirVacios,
  });

  if (query.isLoading) {
    return (
      <li role="none" aria-hidden="true">
        <div
          className="my-1 space-y-1"
          style={{ paddingLeft: nivel * 20 }}
          data-testid="nodo-skeleton"
        >
          <div className="h-6 w-2/3 animate-pulse rounded bg-muted" />
          <div className="h-6 w-1/2 animate-pulse rounded bg-muted" />
        </div>
      </li>
    );
  }

  if (query.isError) {
    return (
      <li role="none">
        <div
          className="flex items-center gap-2 py-1 text-destructive"
          style={{ paddingLeft: nivel * 20 }}
        >
          <span>No se pudo cargar este nivel.</span>
          <Button variant="ghost" size="sm" onClick={() => query.refetch()}>
            Reintentar
          </Button>
        </div>
      </li>
    );
  }

  const hijos = query.data ?? [];
  if (hijos.length === 0) {
    return (
      <li role="none">
        <p
          className="py-1 text-muted-foreground"
          style={{ paddingLeft: nivel * 20 }}
        >
          Sin existencias en este nivel.
        </p>
      </li>
    );
  }

  // Cadena única: un solo hijo no-hoja se abre solo (misma llamada lazy).
  const autoExpandir = hijos.length === 1 && !hijos[0].esHoja;

  return (
    <>
      {hijos.map((nodo) => (
        <FilaNodo
          key={`${nodo.tipo}-${nodo.id}`}
          nodo={nodo}
          nivel={nivel}
          articuloId={articuloId}
          incluirVacios={incluirVacios}
          autoExpandir={autoExpandir}
        />
      ))}
    </>
  );
}

interface FilaNodoProps {
  nodo: NodoJerarquiaSaldo;
  nivel: number;
  articuloId?: string;
  incluirVacios?: boolean;
  autoExpandir: boolean;
}

function FilaNodo({
  nodo,
  nivel,
  articuloId,
  incluirVacios,
  autoExpandir,
}: FilaNodoProps) {
  const [open, setOpen] = useState(autoExpandir);
  const expandible = !nodo.esHoja;

  return (
    <li role="treeitem" aria-expanded={expandible ? open : undefined}>
      <div
        className="flex items-center gap-2 rounded px-2 py-1 hover:bg-muted/50"
        style={{ paddingLeft: nivel * 20 + 8 }}
      >
        {expandible ? (
          <button
            type="button"
            className="flex h-5 w-5 shrink-0 items-center justify-center rounded hover:bg-muted"
            aria-label={`${open ? 'Colapsar' : 'Expandir'} ${nodo.clave}`}
            onClick={() => setOpen((v) => !v)}
          >
            <ChevronRight
              className={cn('h-4 w-4 transition-transform', open && 'rotate-90')}
              aria-hidden="true"
            />
          </button>
        ) : (
          <span className="h-5 w-5 shrink-0" aria-hidden="true" />
        )}

        <span className="font-mono text-xs">{nodo.clave}</span>
        {nodo.nombre && (
          <span className="truncate text-xs text-muted-foreground">
            {nodo.nombre}
          </span>
        )}
        {nodo.esDefault && (
          <Badge variant="outline" className="shrink-0 text-[10px]">
            ÚNICA
          </Badge>
        )}

        <span className="ml-auto shrink-0 text-right font-mono">
          {formatearCantidad(nodo.cantidad)}
        </span>
        <span className="w-32 shrink-0 text-right text-xs text-muted-foreground">
          {formatearMonto(nodo.valorInventarioMxn)}
        </span>
      </div>

      {expandible && open && (
        <ul role="group" className="space-y-0.5">
          <ListaHijos
            // El tipo del hijo ES el nivel a expandir (articulo nunca llega
            // aquí: es hoja).
            nodoTipo={nodo.tipo as NivelNodoJerarquia}
            nodoId={nodo.id}
            articuloId={articuloId}
            incluirVacios={incluirVacios}
            nivel={nivel + 1}
          />
        </ul>
      )}
    </li>
  );
}

function formatearCantidad(v: number): string {
  return v.toLocaleString('es-MX', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
}

function formatearMonto(v: number): string {
  return new Intl.NumberFormat('es-MX', {
    style: 'currency',
    currency: 'MXN',
    minimumFractionDigits: 2,
  }).format(v);
}
