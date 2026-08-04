import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { facturacionKeys } from '@/features/facturacion/api/keys';

/** Nodo del árbol de Facturación (mirror de NodoTrazabilidadFacturacion, doc 13 §4.3). */
export interface NodoTrazabilidadFacturacion {
  /** 5–10 (mirror de TipoDocumentoTrazabilidad del árbol cross-módulo). */
  tipo: number;
  id: string;
  folio: string;
  estado: string;
  uuid: string | null;
  total: number | null;
  fecha: string | null;
}

/** Respuesta de GET .../arbol-documentos (un nivel por lado, 13-D). */
export interface ArbolDocumentosFacturacionResponse {
  actual: NodoTrazabilidadFacturacion;
  ascendientes: NodoTrazabilidadFacturacion[];
  descendientes: NodoTrazabilidadFacturacion[];
}

export type RaizTrazabilidad = 'comprobante' | 'pedido';

/**
 * <c>useArbolDocumentosFacturacion(raiz, id)</c> — árbol de trazabilidad
 * documento-céntrico (ANT-PR3, doc 13 §4.3). <c>raiz</c> decide el
 * endpoint: comprobantes (FV/FANT/NC/REPP/CP, resuelto por TPT) o
 * pedidos facturables. Permiso del endpoint: <c>facturacion.facturas.leer</c>.
 */
export function useArbolDocumentosFacturacion(
  raiz: RaizTrazabilidad,
  id: string | null | undefined,
) {
  return useQuery<ArbolDocumentosFacturacionResponse>({
    queryKey:
      id != null
        ? facturacionKeys.arbolDocumentos(raiz, id)
        : ['facturacion', 'noop-arbol'],
    queryFn: async ({ signal }) => {
      if (id == null) throw new Error('useArbolDocumentosFacturacion invocado sin id');
      const base =
        raiz === 'pedido'
          ? '/api/v1/facturacion/pedidos-facturables'
          : '/api/v1/facturacion/comprobantes';
      const { data } = await apiRequest<ArbolDocumentosFacturacionResponse>(
        `${base}/${id}/arbol-documentos`,
        { signal },
      );
      return data;
    },
    enabled: id != null,
    staleTime: 0,
  });
}
