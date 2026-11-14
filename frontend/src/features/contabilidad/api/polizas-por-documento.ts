import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { polizaKeys } from './polizas';
import type { EstadoPoliza, RelacionPoliza } from './polizas-types';

export type DocumentoPolizas = { documentoId: string } & (
  | { modulo: 'facturacion'; tipo: 'comprobante' }
  | { modulo: 'cuentas_por_pagar'; tipo: 'factura_proveedor' }
);

export interface PolizaDocumento {
  id: string;
  folio: string | null;
  fecha: string;
  estado: EstadoPoliza;
  polizaAnuladaId: string | null;
  anuladaPorPolizaId: string | null;
  polizaRelacionadaId: string | null;
  relacion: RelacionPoliza | null;
}

interface PaginaPolizasDocumento {
  items: PolizaDocumento[];
  total: number;
  offset: number;
  limit: number;
}

export function usePolizasPorDocumento(documento: DocumentoPolizas, offset = 0, limit = 10) {
  const puedeLeer = useHasPermission(PermisosCanonicos.ContabilidadPolizasLeer);
  return useQuery({
    queryKey: [...polizaKeys.all, 'por-documento', documento, offset, limit],
    enabled: puedeLeer && !!documento.documentoId,
    queryFn: async ({ signal }) => {
      const q = new URLSearchParams({ ...documento, offset: String(offset), limit: String(limit) });
      return (
        await apiRequest<PaginaPolizasDocumento>(
          `/api/v1/contabilidad/polizas/por-documento?${q}`,
          { signal },
        )
      ).data;
    },
  });
}

export interface PolizaDeDocumento {
  id: string;
  folio: string | null;
  fecha: string;
  estado: EstadoPoliza;
}

export interface PolizasDeDocumento {
  documentoId: string;
  polizas: PolizaDeDocumento[];
}

/** Máximo de documentos por consulta (igual que el backend). */
export const MAXIMO_DOCUMENTOS_POR_LOTE = 100;

/**
 * P19: pólizas de los documentos de una página de bandeja en UNA sola llamada (sin N+1). Sin permiso de lectura de pólizas
 * no consulta y devuelve un mapa vacío; la bandeja sigue funcionando sin la columna.
 */
export function usePolizasPorDocumentos(
  modulo: DocumentoPolizas['modulo'],
  tipo: DocumentoPolizas['tipo'],
  documentoIds: readonly string[],
) {
  const puedeLeer = useHasPermission(PermisosCanonicos.ContabilidadPolizasLeer);
  const ids = documentoIds.slice(0, MAXIMO_DOCUMENTOS_POR_LOTE);
  const query = useQuery({
    queryKey: [...polizaKeys.all, 'por-documentos', modulo, tipo, ids],
    enabled: puedeLeer && ids.length > 0,
    queryFn: async ({ signal }) => {
      const q = new URLSearchParams({ modulo, tipo });
      ids.forEach((id) => q.append('documentoIds', id));
      return (
        await apiRequest<PolizasDeDocumento[]>(`/api/v1/contabilidad/polizas/por-documentos?${q}`, { signal })
      ).data;
    },
  });
  return { puedeLeer, porDocumento: indexarPolizas(query.data), isLoading: query.isLoading, isError: query.isError };
}

/** Mapa documentoId → pólizas (en orden de fecha). */
export function indexarPolizas(lotes: readonly PolizasDeDocumento[] | undefined): Record<string, PolizaDeDocumento[]> {
  return Object.fromEntries((lotes ?? []).map((l) => [l.documentoId, l.polizas]));
}

/** Póliza a enlazar en la bandeja: la primera vigente (no anulada); si todas están anuladas, la primera. */
export function polizaPrincipal(polizas: readonly PolizaDeDocumento[] | undefined): PolizaDeDocumento | null {
  if (!polizas || polizas.length === 0) return null;
  return polizas.find((p) => p.estado !== 'Anulada') ?? polizas[0] ?? null;
}
