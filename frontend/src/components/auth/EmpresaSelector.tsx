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
    <div className="flex flex-col">
      <label className="flex flex-col gap-1">
        <span className="text-sm text-rail-fg">Empresa:</span>
        <select
          aria-label="Empresa activa"
          value={currentEmpresa?.id ?? ''}
          onChange={handleChange}
          disabled={isBusy}
          className="w-full min-w-0 focus-visible:outline-2 focus-visible:outline-brand rounded-md border border-rail-active bg-rail-active text-white px-2 py-1 text-sm disabled:opacity-50"
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
