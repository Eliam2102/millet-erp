import { useEffect } from 'react';
import { useQuery } from '@tanstack/react-query';
import { apiQueryRequest } from '@/lib/api/query-request';
import { useAuthStore } from '@/lib/auth/auth-store';

interface SucursalSesion {
  id: string;
  clave: string;
  nombre: string;
}

// Etiquetas visibles de Millet. El valor del selector sigue siendo el ID de la sucursal.
const prefijosMillet: Readonly<Partial<Record<string, string>>> = {
  '101': 'CON',
  '102': 'CHI',
  '103': 'CIR',
  '104': 'CAN',
  '105': 'PIN',
};

/** Contexto operativo de la UI; el backend sigue validando cada recurso. */
export function SucursalSelector() {
  const empresaId = useAuthStore((s) => s.currentEmpresaId);
  const sucursalId = useAuthStore((s) => s.currentSucursalId);
  const setSucursalId = useAuthStore((s) => s.setCurrentSucursalId);
  const usuarioId = useAuthStore((s) => s.user?.id);
  const { data: alcance, isPending: cargandoAlcance } = useQuery({
    queryKey: ['auth', 'alcance', usuarioId, empresaId],
    enabled: Boolean(usuarioId && empresaId),
    queryFn: async (context) =>
      (await apiQueryRequest<{ esCorporativo: boolean }>('/api/auth/alcance', context)).data,
    staleTime: 30_000,
  });
  const { data: sucursales = [], isLoading } = useQuery({
    queryKey: ['auth', 'sucursales', empresaId],
    enabled: empresaId != null,
    queryFn: async (context) =>
      (await apiQueryRequest<SucursalSesion[]>('/api/auth/sucursales', context)).data,
    staleTime: 30_000,
  });

  useEffect(() => {
    if (!empresaId || isLoading) return;
    if (sucursalId && !sucursales.some((s) => s.id === sucursalId)) setSucursalId(null);
    else if (!sucursalId && !cargandoAlcance && !alcance?.esCorporativo && sucursales.length === 1)
      setSucursalId(sucursales[0].id);
  }, [empresaId, isLoading, sucursalId, sucursales, setSucursalId, alcance, cargandoAlcance]);

  if (!empresaId || (sucursales.length === 0 && !isLoading)) return null;
  return (
    <label className="flex min-w-0 flex-1 flex-col items-start gap-1 text-sm md:flex-none md:flex-row md:items-center md:gap-2">
      <span className="text-2xs text-ink-muted md:hidden 2xl:hidden">Sucursal</span>
      <span className="hidden text-muted-foreground 2xl:inline">
        {alcance?.esCorporativo ? 'Corporativo · Millet:' : 'Sucursal:'}
      </span>
      <select
        aria-label="Sucursal activa"
        value={sucursalId ?? ''}
        disabled={isLoading}
        onChange={(e) => setSucursalId(e.target.value || null)}
        className="h-11 w-full min-w-0 rounded-md border border-line-control bg-surface-card px-2 md:h-8 md:w-auto md:max-w-32 2xl:max-w-44"
      >
        <option value="">
          {alcance?.esCorporativo ? 'Todas las sucursales' : 'Todas las permitidas'}
        </option>
        {sucursales.map((s) => (
          <option key={s.id} value={s.id}>
            {prefijosMillet[s.clave] ?? s.clave} · {s.nombre}
          </option>
        ))}
      </select>
    </label>
  );
}
