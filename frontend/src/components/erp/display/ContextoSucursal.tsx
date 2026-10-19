import { useQuery } from '@tanstack/react-query';
import { Badge } from '@/components/ui/badge';
import { useAuthStore } from '@/lib/auth/auth-store';
import { apiQueryRequest } from '@/lib/api/query-request';

/** Contexto del topbar; no modifica la sucursal del formulario. */
export function ContextoSucursal() {
  const empresaId = useAuthStore((s) => s.currentEmpresaId);
  const sucursalId = useAuthStore((s) => s.currentSucursalId);
  const { data = [] } = useQuery({
    queryKey: ['auth', 'sucursales', empresaId],
    enabled: empresaId != null,
    queryFn: async (context) =>
      (await apiQueryRequest<{ id: string; nombre: string }[]>('/api/auth/sucursales', context))
        .data,
    staleTime: 30_000,
  });
  const nombre = data.find((s) => s.id === sucursalId)?.nombre;
  return (
    <Badge variant={sucursalId == null ? 'warning' : 'neutral'}>
      {sucursalId == null
        ? 'Todas las sucursales'
        : `Sucursal: ${nombre ?? 'Nombre no disponible'}`}
    </Badge>
  );
}
