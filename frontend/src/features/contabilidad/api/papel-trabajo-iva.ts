import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest, esApiError } from '@/lib/api';
import type { CuadreRolIva, DocumentoLiquidacionIva, ResumenLiquidacionIva } from './liquidacion-iva';

/**
 * M-311 · Papel de trabajo del IVA. Espejo de `PapelTrabajoIva.cs` (GET/POST /api/v1/contabilidad/iva/papel-trabajo):
 * la foto de la liquidación del mes es inmutable; las diferencias se comparan contra ella.
 */
export interface AcuseIva {
  id: string;
  nombre: string;
  tipoContenido: string;
  sha256: string;
  adjuntadoEn: string;
}

export type EstadoDiferenciaIva = 'Pendiente' | 'Cerrada';

export interface DiferenciaFiscalIva {
  id: string;
  saldoIvaDocumentoId: string;
  folio: string | null;
  uuid: string | null;
  ivaFoto: number;
  ivaActual: number;
  diferencia: number;
  causa: string;
  estado: EstadoDiferenciaIva;
  ajustePolizaId: string | null;
  detectadaEn: string;
  cerradaEn: string | null;
}

export interface PapelTrabajoIva {
  fotoId: string;
  anio: number;
  mes: number;
  polizaId: string;
  folioPoliza: string;
  creadaEn: string;
  resumen: ResumenLiquidacionIva;
  cuadre: CuadreRolIva[];
  documentos: DocumentoLiquidacionIva[];
  acuse: AcuseIva | null;
  diferencias: DiferenciaFiscalIva[];
  diferenciasPendientes: number;
}

const BASE = '/api/v1/contabilidad/iva/papel-trabajo';

export const papelTrabajoIvaKeys = {
  mes: (anio: number, mes: number) => ['contabilidad', 'papel-trabajo-iva', anio, mes] as const,
};

/** Sin foto del mes (aún no se generó la póliza de liquidación) el servidor responde 404: se trata como «sin papel». */
export function usePapelTrabajoIva(anio: number, mes: number) {
  return useQuery({
    queryKey: papelTrabajoIvaKeys.mes(anio, mes),
    retry: false,
    queryFn: async ({ signal }): Promise<PapelTrabajoIva | null> => {
      try {
        return (await apiRequest<PapelTrabajoIva>(`${BASE}?anio=${anio}&mes=${mes}`, { signal })).data;
      } catch (e) {
        if (esApiError(e) && e.status === 404) return null;
        throw e;
      }
    },
  });
}

function useInvalidarPapel() {
  const qc = useQueryClient();
  return () => qc.invalidateQueries({ queryKey: ['contabilidad', 'papel-trabajo-iva'] });
}

export function useDetectarDiferenciasIva() {
  const invalidar = useInvalidarPapel();
  return useMutation({
    mutationFn: async (a: { anio: number; mes: number; idempotencyKey: string }) =>
      (await apiRequest<PapelTrabajoIva>(`${BASE}/detectar?anio=${a.anio}&mes=${a.mes}`, { method: 'POST', idempotencyKey: a.idempotencyKey })).data,
    onSuccess: () => void invalidar(),
  });
}

export function useAdjuntarAcuseIva() {
  const invalidar = useInvalidarPapel();
  return useMutation({
    mutationFn: async (a: { anio: number; mes: number; archivo: File; idempotencyKey: string }) => {
      const form = new FormData();
      form.append('archivo', a.archivo);
      return (await apiRequest<AcuseIva>(`${BASE}/acuse?anio=${a.anio}&mes=${a.mes}`, { method: 'POST', body: form, idempotencyKey: a.idempotencyKey })).data;
    },
    onSuccess: () => void invalidar(),
  });
}

export function useLigarAjusteDiferenciaIva() {
  const invalidar = useInvalidarPapel();
  return useMutation({
    mutationFn: async (a: { diferenciaId: string; ajustePolizaId: string; idempotencyKey: string }) =>
      (
        await apiRequest<DiferenciaFiscalIva>(`${BASE}/diferencias/${a.diferenciaId}/ligar-ajuste`, {
          method: 'POST',
          body: { ajustePolizaId: a.ajustePolizaId },
          idempotencyKey: a.idempotencyKey,
        })
      ).data,
    onSuccess: () => void invalidar(),
  });
}
