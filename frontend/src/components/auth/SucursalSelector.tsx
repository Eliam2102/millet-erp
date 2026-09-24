import { useEffect } from 'react';
import { useQuery } from '@tanstack/react-query';
import { apiRequest } from '@/lib/api';
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
  const { data: sucursales = [], isLoading } = useQuery({
    queryKey: ['auth', 'sucursales', empresaId],
    enabled: empresaId != null,
    queryFn: async ({ signal }) => (await apiRequest<SucursalSesion[]>('/api/auth/sucursales', { signal })).data,
  });

  useEffect(() => {
    if (!empresaId || isLoading) return;
    if (sucursalId && !sucursales.some((s) => s.id === sucursalId)) setSucursalId(null);
    else if (!sucursalId && sucursales.length === 1) setSucursalId(sucursales[0].id);
  }, [empresaId, isLoading, sucursalId, sucursales, setSucursalId]);

  if (!empresaId || (sucursales.length === 0 && !isLoading)) return null;
  return (
    <label className="flex items-center gap-2 text-sm">
      <span className="text-muted-foreground">Sucursal:</span>
      <select
        aria-label="Sucursal activa"
        value={sucursalId ?? ''}
        disabled={isLoading}
        onChange={(e) => setSucursalId(e.target.value || null)}
        className="max-w-44 rounded-md border border-input bg-background px-2 py-1"
      >
        <option value="">Todas las permitidas</option>
        {sucursales.map((s) => <option key={s.id} value={s.id}>{s.clave} · {s.nombre}</option>)}
      </select>
    </label>
  );
}
