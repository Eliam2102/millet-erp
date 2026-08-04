import { useMemo } from 'react';
import { useUnidadesMedidaList } from '@/modules/catalogos/api';
import { normalizarCodigoUnidad } from '@/components/erp/forms/decimales-unidad';

/**
 * Resuelve los decimales permitidos de una unidad en el punto de captura
 * (ADR-0046 Etapa 2, PR-2c). Lee el catálogo una vez (cacheado por
 * <c>useUnidadesMedidaList</c>) y expone dos lookups:
 *
 * <list>
 *   <item><c>porId</c>: por <c>unidadMedidaId</c> del artículo seleccionado
 *     (preciso) — captura con <c>ArticuloSelector</c> (RQ/OC, Almacén free-selector).</item>
 *   <item><c>porCodigo</c>: matcheo del string de unidad de una línea contra el
 *     <c>codigo</c> del catálogo (menos preciso, pero correcto: en artículos con FK
 *     el string = código sincronizado desde 1b) — capturas heredadas de Almacén
 *     (recepción, salida-vs-RQ, devolución interna).</item>
 * </list>
 *
 * Ambos devuelven <c>null</c> cuando no resuelven (FK null / string sin match) →
 * el caller cae al fallback global; nunca bloquea.
 */
export interface DecimalesUnidadLookup {
  porId: (unidadMedidaId: string | null | undefined) => number | null;
  porCodigo: (codigo: string | null | undefined) => number | null;
}

export function useDecimalesUnidad(): DecimalesUnidadLookup {
  const { data } = useUnidadesMedidaList();

  return useMemo(() => {
    const porIdMap = new Map<string, number>();
    const porCodigoMap = new Map<string, number>();
    for (const u of data ?? []) {
      porIdMap.set(u.id, u.decimales);
      porCodigoMap.set(normalizarCodigoUnidad(u.codigo), u.decimales);
    }
    return {
      porId: (id) => (id ? porIdMap.get(id) ?? null : null),
      porCodigo: (codigo) => {
        const clave = normalizarCodigoUnidad(codigo);
        return clave ? porCodigoMap.get(clave) ?? null : null;
      },
    };
  }, [data]);
}
