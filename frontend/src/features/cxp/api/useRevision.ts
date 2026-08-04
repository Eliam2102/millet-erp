import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { cxpKeys } from '@/features/cxp/api/keys';
import type {
  AdjuntarEvidenciaResponse,
  EnviarFacturaARevisionCommand,
  EnviarFacturaARevisionResponse,
  Evidencia,
  FacturaEnRevisionItem,
  LiberarRevisionFacturaCommand,
  LiberarRevisionFacturaResponse,
  MotivoRevision,
  PagedResponse,
} from '@/features/cxp/api/types';

/**
 * Hooks de revisión por área + evidencias (FE-F3-PR1). Cubre:
 * <list>
 *   <item>Catálogo de motivos de revisión.</item>
 *   <item>Bandeja de facturas en revisión por dependencia.</item>
 *   <item>Mutaciones <c>enviar-revision</c> + <c>liberar-revision</c>
 *         con <c>X-Expected-Version</c> + Idempotency-Key.</item>
 *   <item>Listado + adjuntar evidencia (multipart) por factura.</item>
 * </list>
 */

export function useMotivosRevision(incluirInactivos = false) {
  return useQuery({
    queryKey: cxpKeys.catalogosMotivosRevision(),
    queryFn: async ({ signal }) => {
      const path = incluirInactivos
        ? '/api/v1/cuentas-por-pagar/catalogos/motivos-revision?incluirInactivos=true'
        : '/api/v1/cuentas-por-pagar/catalogos/motivos-revision';
      const { data } = await apiRequest<MotivoRevision[]>(path, { signal });
      return data;
    },
  });
}

export function useFacturasEnRevision(
  dependenciaRevisoraId: string | undefined,
  motivoRevisionId?: string,
) {
  return useQuery({
    queryKey: cxpKeys.enRevisionList(
      dependenciaRevisoraId ?? '',
      motivoRevisionId,
    ),
    queryFn: async ({ signal }) => {
      const params = new URLSearchParams();
      params.set('dependenciaRevisoraId', dependenciaRevisoraId ?? '');
      if (motivoRevisionId) {
        params.set('motivoRevisionId', motivoRevisionId);
      }
      const { data } = await apiRequest<
        PagedResponse<FacturaEnRevisionItem>
      >(
        `/api/v1/cuentas-por-pagar/facturas/en-revision?${params.toString()}`,
        { signal },
      );
      return data;
    },
    enabled: dependenciaRevisoraId != null && dependenciaRevisoraId !== '',
  });
}

export function useEnviarFacturaARevision() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      versionEsperada: number;
      command: EnviarFacturaARevisionCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<EnviarFacturaARevisionResponse>(
        `/api/v1/cuentas-por-pagar/facturas/${args.id}/enviar-revision`,
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
          headers: { 'X-Expected-Version': String(args.versionEsperada) },
        },
      );
      return data;
    },
    onSuccess: (_, vars) => {
      queryClient.invalidateQueries({
        queryKey: cxpKeys.facturaById(vars.id),
      });
      queryClient.invalidateQueries({ queryKey: cxpKeys.facturas() });
      queryClient.invalidateQueries({ queryKey: cxpKeys.enRevision() });
    },
  });
}

export function useLiberarRevisionFactura() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      versionEsperada: number;
      command: LiberarRevisionFacturaCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<LiberarRevisionFacturaResponse>(
        `/api/v1/cuentas-por-pagar/facturas/${args.id}/liberar-revision`,
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
          headers: { 'X-Expected-Version': String(args.versionEsperada) },
        },
      );
      return data;
    },
    onSuccess: (_, vars) => {
      queryClient.invalidateQueries({
        queryKey: cxpKeys.facturaById(vars.id),
      });
      queryClient.invalidateQueries({ queryKey: cxpKeys.facturas() });
      queryClient.invalidateQueries({ queryKey: cxpKeys.enRevision() });
    },
  });
}

export function useEvidenciasFactura(facturaId: string | undefined) {
  return useQuery({
    queryKey: facturaId
      ? cxpKeys.evidenciasByFactura(facturaId)
      : ['cxp', 'evidencias', 'noop'],
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<Evidencia[]>(
        `/api/v1/cuentas-por-pagar/facturas/${facturaId}/evidencias`,
        { signal },
      );
      return data;
    },
    enabled: facturaId != null,
  });
}

export interface AdjuntarEvidenciaArgs {
  facturaId: string;
  tipo: number;
  comentario: string;
  estadoFirmaFisica: number;
  fechaLimiteFirmaFisica: string | null;
  archivo: File;
  idempotencyKey: string;
}

export function useAdjuntarEvidencia() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: AdjuntarEvidenciaArgs) => {
      const formData = new FormData();
      formData.append('tipo', String(args.tipo));
      formData.append('comentario', args.comentario);
      formData.append('estadoFirmaFisica', String(args.estadoFirmaFisica));
      if (args.fechaLimiteFirmaFisica) {
        formData.append(
          'fechaLimiteFirmaFisica',
          args.fechaLimiteFirmaFisica,
        );
      }
      formData.append('archivo', args.archivo);
      const { data } = await apiRequest<AdjuntarEvidenciaResponse>(
        `/api/v1/cuentas-por-pagar/facturas/${args.facturaId}/evidencias`,
        {
          method: 'POST',
          body: formData,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: (_, vars) => {
      queryClient.invalidateQueries({
        queryKey: cxpKeys.evidenciasByFactura(vars.facturaId),
      });
    },
  });
}
