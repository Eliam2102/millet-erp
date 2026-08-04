import { ArbolDocumentos } from '@/components/erp/trazabilidad/ArbolDocumentos';
import type {
  NodoArbolDocumento,
  TipoDocumentoTrazabilidad,
} from '@/components/erp/trazabilidad/types';
import {
  useArbolDocumentosFacturacion,
  type NodoTrazabilidadFacturacion,
  type RaizTrazabilidad,
} from '@/features/facturacion/api/useTrazabilidad';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * <c>&lt;TrazabilidadFacturacion/&gt;</c> — sección de trazabilidad
 * documento-céntrica (ANT-PR3, doc 13 §6.3): consume el árbol del backend
 * y lo pinta con el <c>ArbolDocumentos</c> cross-módulo (mismo componente
 * que Compras). Se monta en los 5 detalles del módulo (factura, factura de
 * anticipo, carta porte, REPP y pedido). Gateado por
 * <c>facturacion.facturas.leer</c> (permiso del endpoint); si el árbol no
 * tiene relaciones o falla, la sección no estorba (estado vacío / nada).
 */
export function TrazabilidadFacturacion({
  raiz,
  id,
}: {
  raiz: RaizTrazabilidad;
  id: string;
}) {
  const puedeVer = useHasPermission(PermisosCanonicos.FacturacionFacturasLeer);
  const query = useArbolDocumentosFacturacion(raiz, puedeVer ? id : null);

  if (!puedeVer || query.isError) return null;

  return (
    <section className="space-y-2">
      <h2 className="text-sm font-medium">Trazabilidad</h2>
      {query.isLoading || query.data == null ? (
        <div className="h-24 w-full animate-pulse rounded bg-muted" />
      ) : (
        <ArbolDocumentos
          raiz={mapearRaiz(query.data.actual, query.data.ascendientes, query.data.descendientes)}
          tipoActual={query.data.actual.tipo as TipoDocumentoTrazabilidad}
          idActual={query.data.actual.id}
        />
      )}
    </section>
  );
}

function mapearNodo(n: NodoTrazabilidadFacturacion): NodoArbolDocumento {
  return {
    tipoDocumento: n.tipo as TipoDocumentoTrazabilidad,
    id: n.id,
    folio: n.folio,
    estado: n.estado,
    fecha: n.fecha ?? '',
    ascendentes: [],
    descendentes: [],
  };
}

function mapearRaiz(
  actual: NodoTrazabilidadFacturacion,
  ascendientes: NodoTrazabilidadFacturacion[],
  descendientes: NodoTrazabilidadFacturacion[],
): NodoArbolDocumento {
  return {
    ...mapearNodo(actual),
    ascendentes: ascendientes.map(mapearNodo),
    descendentes: descendientes.map(mapearNodo),
  };
}
