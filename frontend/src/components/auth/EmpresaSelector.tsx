import { useState } from 'react';
import { Loader2 } from 'lucide-react';
import { useAuth } from '@/lib/auth/useAuth';

/**
 * Dropdown que permite al usuario cambiar la empresa activa de su sesión.
 * Llama POST <c>/api/auth/cambiar-empresa</c> que valida acceso y re-emite
 * el JWT con el nuevo <c>current_empresa_id</c> + permisos actualizados.
 *
 * Si el usuario solo tiene una empresa, no se muestra (no hay nada que
 * elegir). Si tiene cero, tampoco se muestra (no se renderiza, el
 * shell del App muestra el mensaje "sin empresa asignada").
 */
export function EmpresaSelector() {
  const { empresas, currentEmpresa, changeEmpresa, isSwitchingEmpresa } = useAuth();
  const [isChanging, setIsChanging] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (empresas.length <= 1) {
    return null;
  }

  const isBusy = isChanging || isSwitchingEmpresa;

  const handleChange = async (event: React.ChangeEvent<HTMLSelectElement>) => {
    const empresaId = event.target.value;
    if (!empresaId || empresaId === currentEmpresa?.id) {
      return;
    }

    setIsChanging(true);
    setError(null);
    try {
      await changeEmpresa(empresaId);
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setIsChanging(false);
    }
  };

  return (
    <div className="flex min-w-0 flex-1 flex-col md:flex-none">
      <label className="flex min-w-0 flex-col items-start gap-1 md:flex-row md:items-center md:gap-2">
        <span className="text-2xs text-ink-muted md:hidden 2xl:inline">Empresa:</span>
        <select
          aria-label="Empresa activa"
          value={currentEmpresa?.id ?? ''}
          onChange={handleChange}
          disabled={isBusy}
          className="h-11 w-full min-w-0 rounded-md border border-line-control bg-surface-card px-2 text-sm disabled:opacity-50 md:h-8 md:w-auto md:max-w-28 2xl:max-w-44"
        >
          {empresas.map((e) => (
            <option key={e.id} value={e.id}>
              {e.razonSocial} ({e.rfc})
            </option>
          ))}
        </select>
        {isBusy && <Loader2 className="h-4 w-4 animate-spin text-muted-foreground" />}
      </label>
      {error && <span className="text-xs text-danger-fg mt-1">{error}</span>}
    </div>
  );
}
