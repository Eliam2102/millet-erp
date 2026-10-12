import { useEffect } from 'react';
import { useQuery } from '@tanstack/react-query';
import { apiQueryRequest } from '@/lib/api/query-request';
import { useAuthStore } from '@/lib/auth/auth-store';

interface SucursalSesion {
  id: string;
  clave: string;
  nombre: string;
}

/** Contexto operativo de la UI; el backend sigue validando cada recurso. */
export function SucursalSelector() {
  const empresaId = useAuthStore((s) => s.currentEmpresaId);
  const sucursalId = useAuthStore((s) => s.currentSucursalId);
  const setSucursalId = useAuthStore((s) => s.setCurrentSucursalId);
  const usuarioId = useAuthStore((s) => s.user?.id);
  const { data: alcance, isPending: cargandoAlcance } = useQuery({
    queryKey: ['auth', 'alcance', usuarioId, empresaId],
    enabled: Boolean(usuarioId && empresaId),
    queryFn: async ({ signal }) => (await apiRequest<{ esCorporativo: boolean }>('/api/auth/alcance', { signal })).data,
  });
  const { data: sucursales = [], isLoading } = useQuery({
    queryKey: ['auth', 'sucursales', empresaId],
    enabled: empresaId != null,
    queryFn: async (context) => (await apiQueryRequest<SucursalSesion[]>('/api/auth/sucursales', context)).data,
    staleTime: 30_000,
  });

  useEffect(() => {
    if (!empresaId || isLoading) return;
    if (sucursalId && !sucursales.some((s) => s.id === sucursalId)) setSucursalId(null);
    else if (!sucursalId && !cargandoAlcance && !alcance?.esCorporativo && sucursales.length === 1) setSucursalId(sucursales[0].id);
  }, [empresaId, isLoading, sucursalId, sucursales, setSucursalId, alcance, cargandoAlcance]);

  if (!empresaId || (sucursales.length === 0 && !isLoading)) return null;
  return (
    <label className="flex items-center gap-2 text-sm">
      <span className="text-muted-foreground">{alcance?.esCorporativo ? 'Corporativo · Millet:' : 'Sucursal:'}</span>
      <select
        aria-label="Sucursal activa"
        value={sucursalId ?? ''}
        disabled={isLoading}
        onChange={(e) => setSucursalId(e.target.value || null)}
        className="max-w-44 rounded-md border border-input bg-background px-2 py-1"
      >
        <option value="">{alcance?.esCorporativo ? 'Todas las sucursales' : 'Todas las permitidas'}</option>
        {sucursales.map((s) => <option key={s.id} value={s.id}>{s.clave} · {s.nombre}</option>)}
      </select>
    </label>
  );
}
