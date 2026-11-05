import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { almacenKeys } from '@/features/almacen/api/keys';

/** A4.6: conciliación diaria del ERP contra A+W (espejos de los DTO de `ConciliacionDiariaEndpoints`). */
export interface ExistenciaLinea {
  articuloId: string;
  articuloClave: string | null;
  articuloDescripcion: string | null;
  productoAwVarianteId: string | null;
  medida: string | null;
  lote: string | null;
  unidad: string;
  teorico: number;
}

export interface DiferenciaConciliacion {
  id: string;
  conciliacionId: string;
  almacenId: string;
  almacenClave: string | null;
  fecha: string;
  articuloId: string;
  articuloClave: string | null;
  articuloDescripcion: string | null;
  productoAwVarianteId: string | null;
  medida: string | null;
  lote: string | null;
  unidad: string;
  teorico: number;
  fisico: number;
  diferencia: number;
  sentido: 'FALTANTE' | 'SOBRANTE';
  costoUnitarioMxn: number;
  valorMxn: number;
  folioMovimiento: string | null;
  estado: 'PENDIENTE' | 'ACLARADA';
  motivoAclaracion: string | null;
  aclaradaAt: string | null;
}

export interface CorridaConciliacion {
  id: string;
  almacenId: string;
  almacenClave: string | null;
  fecha: string;
  fuente: 'AW' | 'CONTEO';
  lineasComparadas: number;
  diferencias: number;
  ejecutadaAt: string;
  detalle: DiferenciaConciliacion[];
}

export interface FisicoLineaRequest {
  articuloId: string;
  productoAwVarianteId: string | null;
  lote: string | null;
  fisico: number;
}

const BASE = '/api/v1/almacen/conciliacion-diaria';

export function useExistenciaConciliacion(almacenId: string | undefined) {
  return useQuery({
    queryKey: almacenKeys.conciliacion.existencia(almacenId ?? ''),
    enabled: !!almacenId,
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<ExistenciaLinea[]>(`${BASE}/existencia?almacenId=${almacenId}`, { signal });
      return data;
    },
  });
}

export function useDiferenciasConciliacion() {
  return useQuery({
    queryKey: almacenKeys.conciliacion.diferencias(),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<DiferenciaConciliacion[]>(`${BASE}/diferencias`, { signal });
      return data;
    },
  });
}

export function useCorridasConciliacion() {
  return useQuery({
    queryKey: almacenKeys.conciliacion.corridas(),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<CorridaConciliacion[]>(`${BASE}/corridas`, { signal });
      return data;
    },
  });
}

export function useEjecutarConciliacion() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: { almacenId: string; fecha: string; fuente: 'AW' | 'CONTEO'; lineas: FisicoLineaRequest[] }) => {
      const { data } = await apiRequest<CorridaConciliacion>(BASE, {
        method: 'POST',
        body,
        idempotencyKey: crypto.randomUUID(),
      });
      return data;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: almacenKeys.all }),
  });
}

export function useAclararDiferencia() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: { id: string; motivo: string }) => {
      const { data } = await apiRequest<DiferenciaConciliacion>(`${BASE}/diferencias/${args.id}/aclarar`, {
        method: 'POST',
        body: { motivo: args.motivo },
        idempotencyKey: crypto.randomUUID(),
      });
      return data;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: almacenKeys.all }),
  });
}
