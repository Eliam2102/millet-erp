import { useParams } from '@tanstack/react-router';
import { GitBranch } from 'lucide-react';
import {
  ArbolDocumentos,
  TipoDocumentoTrazabilidad,
} from '@/components/erp';
import {
  ErrorState,
  TableSkeleton,
  EmptyState,
} from '@/components/erp';
import { useArbolDocumentos } from '@/features/compras/ordenes/api/useArbolDocumentos';
import { esApiError } from '@/lib/api';

/**
 * <c>P10 — Trazabilidad de OC</c> (UF7-PR2). Wrapper de OC del
 * componente cross-módulo <c>&lt;ArbolDocumentos/&gt;</c>. Renderiza
 * el árbol upstream (RQs) + downstream (recepciones, facturas, pagos
 * cuando esos módulos estén implementados) desde la OC actual.
 *
 * <para>Ruta: <c>/compras/trazabilidad/oc/$id</c>. CxP/Recepción/
 * Tesorería tendrán wrappers análogos consumiendo el mismo endpoint.</para>
 */
export function TrazabilidadOc() {
  const { id } = useParams({
    from: '/_app/compras/trazabilidad/oc/$id',
  });
  const query = useArbolDocumentos(TipoDocumentoTrazabilidad.OrdenCompra, id);

  return (
    <div className="space-y-4 p-4" data-component="trazabilidad-oc">
      <header>
        <h1 className="flex items-center gap-2 text-2xl font-semibold tracking-tight">
          <GitBranch className="h-6 w-6" />
          Trazabilidad documental
        </h1>
        <p className="text-sm text-muted-foreground">
          Cadena RQ → OC → Recepciones → Facturas → Pagos. Click en cualquier
          nodo navega a su detalle (cuando el módulo correspondiente esté
          disponible).
        </p>
      </header>

      {query.isLoading && <TableSkeleton rows={3} />}

      {query.isError && (
        <ErrorState
          title="No se pudo cargar el árbol de trazabilidad"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => {
            void query.refetch();
          }}
        />
      )}

      {query.data && (
        <ArbolDocumentos
          raiz={query.data}
          tipoActual={TipoDocumentoTrazabilidad.OrdenCompra}
          idActual={id}
        />
      )}

      {!query.isLoading && !query.isError && !query.data && (
        <EmptyState
          title="Sin información de trazabilidad"
          description="El backend no devolvió un árbol para esta OC."
        />
      )}
    </div>
  );
}
