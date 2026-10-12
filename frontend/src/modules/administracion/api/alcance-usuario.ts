import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
import { useAuthStore } from '@/lib/auth/auth-store';

export interface AlcanceUsuario { esCorporativo: boolean; sucursales: string[] }
export function useAlcanceUsuario(usuarioId: string | null) {
  const empresaId = useAuthStore((s) => s.currentEmpresaId);
  return useQuery({
    queryKey: ['admin', 'usuarios', usuarioId, 'alcance', empresaId],
    enabled: usuarioId != null && empresaId != null,
    queryFn: async ({ signal }) => (await apiRequest<AlcanceUsuario>(`/api/v1/admin/usuarios/${usuarioId}/alcance`, { signal })).data,
  });
}
export function useActualizarAlcanceUsuario(usuarioId: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: async (body: AlcanceUsuario) => (await apiRequest<AlcanceUsuario>(`/api/v1/admin/usuarios/${usuarioId}/alcance`, {
      method: 'PUT', body, idempotencyKey: crypto.randomUUID(),
    })).data,
    onSuccess: async () => {
      await Promise.all(['admin', 'catalogos', 'auth', 'compras'].map((key) => client.invalidateQueries({ queryKey: [key] })));
    },
  });
}
