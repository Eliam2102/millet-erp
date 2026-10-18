import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest, esConflictoConcurrencia, useBodyScopedIdempotencyKey } from '@/lib/api';
import { useConflictDialog } from '@/components/erp/collaboration/conflict-dialog-context';
import { ceKeys } from './hooks';
export interface OtrEnvio {
  id: string;
  idOrigen: string;
  version: number;
  versionAplicada: number;
  hash: string;
  sucursalId: string;
  clienteId: string;
  expedienteId: string | null;
  contenedorId: string | null;
  estado: 1 | 2 | 3;
  motivoVinculacion: string | null;
  contenido: {
    cliente: string;
    direccionEntrega: string;
    producto: string | null;
    contenedor: {
      numero: string | null;
      ruta: string | null;
      pesoKg: number | null;
      bultos: number | null;
      fechaCargaPrevista: string | null;
    };
  };
  lineas: {
    id: string;
    versionOrigen: number;
    numeroPedido: string;
    posicion: number;
    cantidad: number;
    unidad: string;
    estado: 1 | 2 | 3;
    motivo: string | null;
    partidaId: string | null;
  }[];
  conflictos: {
    id: string;
    versionOrigen: number;
    campo: string;
    valorOtr: string;
    valorErp: string;
    actorErp: string;
    fechaErp: string;
    recibidoAt: string;
    estado: 'Pendiente' | 'Resuelto' | 'Superado';
    superadoPorVersion: number | null;
    resueltoPorId: string | null;
    resueltoAt: string | null;
    usarOtr: boolean | null;
    motivo: string | null;
  }[];
  salidas: {
    id: string;
    tipo: string;
    versionErp: number;
    estado: 1 | 2 | 3 | 4 | 5;
    intento: number;
    reemplazadaPorId: string | null;
    sinAcuse: boolean;
    intentos: number;
    ultimoError: string | null;
    acuseAt: string | null;
    acuseResultado: string | null;
    acuseMotivo: string | null;
  }[];
}
const BASE = '/api/v1/comercio-exterior/otr';
export function useOtrEnvios(expedienteId?: string) {
  return useQuery({
    queryKey: [...ceKeys.all, 'otr', expedienteId],
    queryFn: async ({ signal }) =>
      (
        await apiRequest<OtrEnvio[]>(
          `${BASE}${expedienteId ? `?expedienteId=${expedienteId}` : ''}`,
          { signal },
        )
      ).data,
  });
}
export function useOtrCandidatos(id: string) {
  return useQuery({
    queryKey: [...ceKeys.all, 'otr-candidatos', id],
    queryFn: async ({ signal }) =>
      (await apiRequest<{ id: string; folio: string }[]>(`${BASE}/${id}/expedientes`, { signal }))
        .data,
  });
}
export function useOtrAccion() {
  const qc = useQueryClient();
  const keyFor = useBodyScopedIdempotencyKey();
  const conflict = useConflictDialog();
  return useMutation({
    mutationFn: ({ envio, ruta, body }: { envio: OtrEnvio; ruta: string; body?: unknown }) =>
      apiRequest<OtrEnvio>(`${BASE}/${envio.id}/${ruta}`, {
        method: 'POST',
        body,
        ifMatch: `"${envio.version}"`,
        idempotencyKey: keyFor({ id: envio.id, version: envio.version, ruta, body }),
      }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ceKeys.all }),
    onError: (error) => {
      if (esConflictoConcurrencia(error))
        conflict.openSimple({ onRefrescar: () => qc.invalidateQueries({ queryKey: ceKeys.all }) });
    },
  });
}
