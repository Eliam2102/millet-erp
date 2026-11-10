import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import type { Poliza } from './polizas-types';

/**
 * K10.7 · Liquidación mensual de IVA por flujo de efectivo.
 * Espejo de `LiquidacionIva.cs` (GET /api/v1/contabilidad/iva/liquidacion): importes en MXN y enums como texto.
 */
export type LadoIva = 'Acreditable' | 'Trasladado';

export interface ResumenLiquidacionIva {
  ivaTrasladadoCobrado: number;
  ivaAcreditablePagado: number;
  ivaRetenidoPorClientes: number;
  /** IVA retenido a proveedores (fletes): se entera aparte; no resta del IVA a cargo. */
  ivaRetenidoAProveedores: number;
  ivaACargo: number;
  ivaAFavor: number;
}

export interface CuadreRolIva {
  rol: string;
  descripcion: string;
  cuentas: string[];
  porDocumentos: number;
  enCuentas: number;
  diferencia: number;
  cuadra: boolean;
}

export interface DocumentoLiquidacionIva {
  saldoId: string;
  lado: LadoIva;
  origenTipo: string;
  documentoId: string;
  folio: string | null;
  uuid: string | null;
  metodoPago: string | null;
  fecha: string;
  moneda: string;
  base: number;
  iva: number;
  aplicadoMes: number;
  ivaMes: number;
  ivaRetenidoMes: number;
  ivaPendiente: number;
  saldo: number;
  completo: boolean;
  motivos: string[];
}

export interface LiquidacionIva {
  anio: number;
  mes: number;
  resumen: ResumenLiquidacionIva;
  cuadre: CuadreRolIva[];
  cuadra: boolean;
  documentos: DocumentoLiquidacionIva[];
  pendientes: number;
}

const BASE = '/api/v1/contabilidad/iva/liquidacion';

export const liquidacionIvaKeys = {
  all: ['contabilidad', 'liquidacion-iva'] as const,
  mes: (anio: number, mes: number) => ['contabilidad', 'liquidacion-iva', anio, mes] as const,
};

export function useLiquidacionIva(anio: number, mes: number, enabled = true) {
  return useQuery({
    queryKey: liquidacionIvaKeys.mes(anio, mes),
    enabled,
    queryFn: async ({ signal }) => (await apiRequest<LiquidacionIva>(`${BASE}?anio=${anio}&mes=${mes}`, { signal })).data,
  });
}

/** Borrador de la póliza de liquidación del mes (tipo AS) con las cuentas de los roles; el contador la revisa y autoriza. */
export function useGenerarPolizaLiquidacionIva() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (a: { anio: number; mes: number; sucursalId: string; idempotencyKey: string }) =>
      (
        await apiRequest<Poliza>(`${BASE}/poliza`, {
          method: 'POST',
          body: { anio: a.anio, mes: a.mes, sucursalId: a.sucursalId },
          idempotencyKey: a.idempotencyKey,
        })
      ).data,
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['contabilidad', 'polizas'] });
    },
  });
}

/** M-209 (CA4): diferencia de un tratamiento entre lo contabilizado y el XML del documento. */
export interface DiferenciaIva {
  tratamiento: string;
  baseContabilizada: number;
  ivaContabilizado: number;
  baseXml: number;
  ivaXml: number;
  baseDiferencia: number;
  ivaDiferencia: number;
  ivaPagado: number;
}

export interface RecalculoIva {
  saldoId: string;
  folio: string | null;
  uuid: string | null;
  moneda: string;
  diferencias: DiferenciaIva[];
  ivaDiferencia: number;
  sinDiferencia: boolean;
  polizaAjusteId: string | null;
  polizaAjusteFolio: string | null;
}

/**
 * M-209 (CA4): recalcula el IVA de una factura de proveedor contra su XML. Sin `aplicar`, solo la vista previa; con él, póliza de
 * ajuste ligada al documento (la original no se anula) y saldo de IVA corregido.
 */
export function useRecalcularIvaDocumento() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (a: { saldoId: string; motivo: string | null; aplicar: boolean; idempotencyKey: string }) =>
      (
        await apiRequest<RecalculoIva>(`/api/v1/contabilidad/iva/documentos/${a.saldoId}/recalcular`, {
          method: 'POST',
          body: { motivo: a.motivo, aplicar: a.aplicar },
          idempotencyKey: a.idempotencyKey,
        })
      ).data,
    onSuccess: (_, a) => {
      if (!a.aplicar) return;
      void qc.invalidateQueries({ queryKey: liquidacionIvaKeys.all });
      void qc.invalidateQueries({ queryKey: ['contabilidad', 'polizas'] });
    },
  });
}
