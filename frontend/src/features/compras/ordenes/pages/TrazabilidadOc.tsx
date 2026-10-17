import { useParams } from '@tanstack/react-router';
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
import { PageHeader } from '@/components/erp/PageHeader';

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
    <div className="space-y-4" data-component="trazabilidad-oc">
      <PageHeader
        titulo="Trazabilidad documental"
        descripcion="Cadena RQ → OC → Recepciones → Facturas → Pagos; haz clic en un nodo para ir a su detalle."
      />

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
