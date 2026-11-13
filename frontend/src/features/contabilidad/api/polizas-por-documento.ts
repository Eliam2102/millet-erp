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
