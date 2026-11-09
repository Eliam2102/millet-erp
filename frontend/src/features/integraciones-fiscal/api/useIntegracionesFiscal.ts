import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest, ApiError } from '@/lib/api';
import { integracionesFiscalKeys } from '@/features/integraciones-fiscal/api/keys';
import type {
  ActualizarRfcReceptorPayload,
  AgregarRfcReceptorCommand,
  CertificadoCsdItem,
  ConfiguracionPacResponse,
  EstadoCsdEmpresa,
  GuardarConfiguracionPacPayload,
  ProveedorPac,
  RfcReceptorResponse,
  SubirFielReceptorPayload,
  TestConexionPacPayload,
  TestConexionPacResponse,
} from '@/features/integraciones-fiscal/api/types';

/**
 * Hooks del módulo Integraciones.Fiscal (PR-8). Cubre la configuración
 * del PAC + RFCs receptores + test de conexión. Mutaciones llevan
 * Idempotency-Key (ADR-0020) e invalidan las queries correspondientes.
 */

// ─── Configuración del PAC ─────────────────────────────────────────────

export function useConfiguracionPac(empresaId: string | null, proveedor: ProveedorPac) {
  return useQuery({
    queryKey: integracionesFiscalKeys.configuracionByEmpresa(empresaId ?? '', proveedor),
    queryFn: async ({ signal }) => {
      try {
        const { data } = await apiRequest<ConfiguracionPacResponse>(
          `/api/v1/integraciones/fiscal/configuracion/${empresaId}/${proveedor}`,
          { signal },
        );
        // 204 = no hay configuración aún (modo "crear"); la UI renderiza el form vacío.
        return data ?? null;
      } catch (err) {
        // 404: mismo caso en un API anterior.
        if (err instanceof ApiError && err.status === 404) {
          return null;
        }
        throw err;
      }
    },
    enabled: empresaId !== null && empresaId !== '',
  });
}

export function useGuardarConfiguracionPac() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      empresaId: string;
      proveedor: ProveedorPac;
      payload: GuardarConfiguracionPacPayload;
      versionEsperada?: number;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<ConfiguracionPacResponse>(
        `/api/v1/integraciones/fiscal/configuracion/${args.empresaId}/${args.proveedor}`,
        {
          method: 'PUT',
          body: args.payload,
          idempotencyKey: args.idempotencyKey,
          headers:
            args.versionEsperada == null
              ? undefined
              : { 'X-Expected-Version': String(args.versionEsperada) },
        },
      );
      return data;
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: integracionesFiscalKeys.configuracionByEmpresa(vars.empresaId, vars.proveedor),
      });
      queryClient.invalidateQueries({ queryKey: integracionesFiscalKeys.csdEstado() });
    },
  });
}

// ─── F6.9: certificados CSD ────────────────────────────────────────────

/** Estado del CSD de la empresa actual (aviso en el ERP). Se refresca cada hora. */
export function useEstadoCsd(enabled: boolean) {
  return useQuery<EstadoCsdEmpresa>({
    queryKey: integracionesFiscalKeys.csdEstado(),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<EstadoCsdEmpresa>('/api/v1/integraciones/fiscal/csd/estado', { signal });
      return data;
    },
    enabled,
    staleTime: 60 * 60 * 1000,
  });
}

/** Historial de CSD de la empresa (alta, sustitución, revocación). */
export function useCertificadosCsd(empresaId: string, proveedor: ProveedorPac) {
  return useQuery<CertificadoCsdItem[]>({
    queryKey: integracionesFiscalKeys.certificados(empresaId, proveedor),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<CertificadoCsdItem[]>(
        `/api/v1/integraciones/fiscal/configuracion/${empresaId}/${proveedor}/certificados`,
        { signal },
      );
      return data;
    },
  });
}

/** Revoca el CSD en uso (con motivo): el ERP deja de timbrar con él. */
export function useRevocarCsd() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      empresaId: string;
      proveedor: ProveedorPac;
      motivo: string;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<CertificadoCsdItem>(
        `/api/v1/integraciones/fiscal/configuracion/${args.empresaId}/${args.proveedor}/csd/revocar`,
        { method: 'POST', body: { motivo: args.motivo }, idempotencyKey: args.idempotencyKey },
      );
      return data;
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: integracionesFiscalKeys.configuracionByEmpresa(vars.empresaId, vars.proveedor),
      });
      queryClient.invalidateQueries({ queryKey: integracionesFiscalKeys.csdEstado() });
    },
  });
}

export function useTestConexionPac() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      empresaId: string;
      proveedor: ProveedorPac;
      payload: TestConexionPacPayload;
    }) => {
      const { data } = await apiRequest<TestConexionPacResponse>(
        `/api/v1/integraciones/fiscal/configuracion/${args.empresaId}/${args.proveedor}/test`,
        {
          method: 'POST',
          body: args.payload,
        },
      );
      return data;
    },
    // M-232: probar la configuración guardada registra el resultado (y sube su versión):
    // se recarga para mostrar la última prueba y no guardar con una versión vieja.
    onSettled: (_data, _error, vars) => {
      queryClient.invalidateQueries({
        queryKey: integracionesFiscalKeys.configuracionByEmpresa(vars.empresaId, vars.proveedor),
      });
    },
  });
}

// ─── RFCs receptores ───────────────────────────────────────────────────

export function useRfcsReceptores(empresaId: string | null) {
  return useQuery({
    queryKey: integracionesFiscalKeys.rfcsReceptoresByEmpresa(empresaId ?? ''),
    queryFn: async ({ signal }) => {
      const { data } = await apiRequest<RfcReceptorResponse[]>(
        `/api/v1/integraciones/fiscal/rfcs-receptores?empresaId=${empresaId}`,
        { signal },
      );
      return data;
    },
    enabled: empresaId !== null && empresaId !== '',
  });
}

export function useAgregarRfcReceptor() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      command: AgregarRfcReceptorCommand;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<RfcReceptorResponse>(
        '/api/v1/integraciones/fiscal/rfcs-receptores',
        {
          method: 'POST',
          body: args.command,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: integracionesFiscalKeys.rfcsReceptoresByEmpresa(vars.command.empresaId),
      });
    },
  });
}

export function useActualizarRfcReceptor() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      empresaId: string;
      payload: ActualizarRfcReceptorPayload;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<RfcReceptorResponse>(
        `/api/v1/integraciones/fiscal/rfcs-receptores/${args.id}`,
        {
          method: 'PATCH',
          body: args.payload,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: integracionesFiscalKeys.rfcsReceptoresByEmpresa(vars.empresaId),
      });
    },
  });
}

export function useEliminarRfcReceptor() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      empresaId: string;
      idempotencyKey: string;
    }) => {
      await apiRequest<void>(
        `/api/v1/integraciones/fiscal/rfcs-receptores/${args.id}`,
        {
          method: 'DELETE',
          idempotencyKey: args.idempotencyKey,
        },
      );
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: integracionesFiscalKeys.rfcsReceptoresByEmpresa(vars.empresaId),
      });
    },
  });
}

/**
 * Sube la FIEL (e.firma) del RFC receptor a FiscalAPI. Los archivos
 * van como base64 dentro del JSON. El backend asegura el Person en
 * FiscalAPI, sube los tax-files, y persiste IDs externos + vigencia.
 */
export function useSubirFielReceptor() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (args: {
      id: string;
      empresaId: string;
      payload: SubirFielReceptorPayload;
      idempotencyKey: string;
    }) => {
      const { data } = await apiRequest<RfcReceptorResponse>(
        `/api/v1/integraciones/fiscal/rfcs-receptores/${args.id}/fiel`,
        {
          method: 'POST',
          body: args.payload,
          idempotencyKey: args.idempotencyKey,
        },
      );
      return data;
    },
    onSuccess: (_data, vars) => {
      queryClient.invalidateQueries({
        queryKey: integracionesFiscalKeys.rfcsReceptoresByEmpresa(vars.empresaId),
      });
    },
  });
}
