import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest, esConflictoConcurrencia, useBodyScopedIdempotencyKey } from '@/lib/api';
import { useConflictDialog } from '@/components/erp/collaboration/conflict-dialog-context';
import { CE_BASE, ceKeys } from './hooks';
export interface Concepto {
  id: string;
  nombre: string;
  codigo: string;
  baseImportacion: number;
  baseExportacion: number;
}
export interface Gasto {
  id: string;
  conceptoId: string;
  concepto: string;
  proveedorId: string;
  factura: string;
  facturaProveedorId: string | null;
  moneda: string;
  importe: number;
  importeMxn: number;
  tipoCambio: number;
  fechaFactura: string;
  pedimento: string | null;
  cotizado: number | null;
  diferencia: number | null;
  contenedorId: string | null;
  importesDirectos: Record<string, number>;
}
export interface PartidaCosto {
  partidaId: string;
  contenedorId: string | null;
  descripcion: string;
  valorAduanaMxn: number;
  pesoKg: number;
  cantidad: number;
  clienteId: string | null;
}
export interface Distribucion {
  numeroVersion: number;
  fecha: string;
  usuarioId: string;
  renglones: {
    gastoId: string;
    conceptoId: string;
    base: number;
    partidaId: string;
    baseUsada: number;
    importeMxn: number;
  }[];
  partidas: {
    partidaId: string;
    descripcion: string;
    clienteId: string | null;
    importeMxn: number;
    diferenciaMxn: number;
    inventarioMxn: number;
    costoVentasMxn: number;
    costoUnitarioAntes: number;
    costoUnitarioDespues: number;
  }[];
}
export interface Anticipo {
  id: string;
  agenteId: string;
  pedimento: string;
  anticipado: number;
  aplicado: number | null;
  diferencia: number | null;
  saldo: number | null;
  estado: string;
  compensacionRecibida: number;
  resoluciones: {
    tipo: string;
    importe: number;
    referencia: string;
    anticipoDestinoId: string | null;
  }[];
}
export interface Finanzas {
  nombres: Record<string, string>;
  version: number;
  estadoFinanciero: string;
  pendientes: string[];
  conceptos: Concepto[];
  gastos: Gasto[];
  partidas: PartidaCosto[];
  bases: { id: string; pedimento: string }[];
  distribuciones: Distribucion[];
  anticipos: Anticipo[];
}
const key = (id: string) => [...ceKeys.all, 'finanzas', id];
export function useFinanzas(id: string) {
  return useQuery({
    queryKey: key(id),
    enabled: Boolean(id),
    queryFn: ({ signal }) => apiRequest<Finanzas>(`${CE_BASE}/${id}/finanzas`, { signal }),
  });
}
export function useMutarFinanzas(id: string) {
  const qc = useQueryClient();
  const keyFor = useBodyScopedIdempotencyKey();
  const conflict = useConflictDialog();
  return useMutation({
    mutationFn: ({
      ruta,
      body,
      method = 'POST',
      etag,
    }: {
      ruta: string;
      body?: unknown;
      method?: string;
      etag?: string;
    }) => {
      const version = etag ?? qc.getQueryData<{ etag?: string }>(key(id))?.etag;
      return apiRequest<Finanzas>(`${CE_BASE}/${id}/${ruta}`, {
        method,
        body,
        ifMatch: version,
        idempotencyKey: keyFor({ ruta, body, version }),
      });
    },
    onSuccess: async (result) => {
      qc.setQueryData(key(id), result);
      await qc.invalidateQueries({ queryKey: ceKeys.all });
    },
    onError: (error) => {
      if (esConflictoConcurrencia(error))
        conflict.openSimple({ onRefrescar: () => qc.invalidateQueries({ queryKey: ceKeys.all }) });
    },
  });
}
