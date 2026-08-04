import { useQuery } from '@tanstack/react-query';
import { apiRequest, esApiError } from '@/lib/api';
import { catalogosKeys } from '@/modules/catalogos/api/keys';

/**
 * Búsqueda typeahead de catálogos SAT EN VIVO contra FiscalAPI
 * (FAC-DET-PR3, backend #479): <c>GET /api/v1/catalogos/sat/{catalogo}</c>.
 * A diferencia de los catálogos seedeados (usos-cfdi, formas-pago), estos
 * se consultan al PAC para estar siempre en la versión vigente del SAT —
 * c_ClaveProdServ tiene ~50k claves y no se seedea.
 *
 * <para><b>503 <c>CATALOGO_SAT_NO_DISPONIBLE</c></b>: SDK apagado, empresa
 * sin ConfiguracionPac o FiscalAPI caído. El consumidor (
 * <c>&lt;ClaveSatSelector/&gt;</c>) degrada a captura manual del código —
 * usar <see cref="esCatalogoSatNoDisponible"/> para distinguirlo.</para>
 */

/** Segmento de ruta de cada catálogo SAT en vivo. */
export type CatalogoSatVivo =
  | 'clave-prod-serv'
  | 'clave-unidad'
  | 'objeto-imp'
  | 'fraccion-arancelaria'
  | 'unidad-aduana'
  | 'pais'
  | 'clave-pedimento';

/** Entrada de catálogo SAT: código + descripción (shape del backend). */
export interface CatalogoSatItem {
  codigo: string;
  descripcion: string;
}

/** <c>true</c> si el error es el 503 de catálogo SAT no disponible. */
export function esCatalogoSatNoDisponible(error: unknown): boolean {
  return (
    esApiError(error) &&
    error.status === 503 &&
    error.code === 'CATALOGO_SAT_NO_DISPONIBLE'
  );
}

export interface UseCatalogoSatSearchOptions {
  enabled?: boolean;
  limit?: number;
}

/**
 * Query de búsqueda. Reglas espejo del backend: <c>objeto-imp</c> admite
 * búsqueda vacía (lista el catálogo completo, 8 entradas); los demás
 * requieren al menos 1 carácter (1–3 = lookup exacto por código, ≥4 =
 * búsqueda por texto). No reintenta el 503 (no es transitorio por request)
 * y cachea 1 h — los catálogos SAT cambian pocas veces al año.
 */
export function useCatalogoSatSearch(
  catalogo: CatalogoSatVivo,
  buscar: string,
  { enabled = true, limit = 20 }: UseCatalogoSatSearchOptions = {},
) {
  const termino = buscar.trim();
  const consultable = catalogo === 'objeto-imp' || termino.length > 0;

  return useQuery({
    queryKey: catalogosKeys.satVivo(catalogo, termino.toLowerCase(), limit),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams({ limit: String(limit) });
      if (termino.length > 0) params.set('buscar', termino);
      const { data } = await apiRequest<CatalogoSatItem[]>(
        `/api/v1/catalogos/sat/${catalogo}?${params.toString()}`,
        { signal },
      );
      return data;
    },
    enabled: enabled && consultable,
    staleTime: 60 * 60 * 1000,
    gcTime: 2 * 60 * 60 * 1000,
    retry: (failureCount, error) =>
      !esCatalogoSatNoDisponible(error) && failureCount < 2,
  });
}
