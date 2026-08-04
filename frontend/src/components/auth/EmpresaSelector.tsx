import { useState } from 'react';
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
  const { empresas, currentEmpresa, changeEmpresa } = useAuth();
  const [isChanging, setIsChanging] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (empresas.length <= 1) {
    return null;
  }

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
      <label className="flex items-center gap-2">
        <span className="text-sm text-slate-500 dark:text-slate-400">
          Empresa:
        </span>
        <select
          value={currentEmpresa?.id ?? ''}
          onChange={handleChange}
          disabled={isChanging}
          className="rounded-md border border-slate-300 dark:border-slate-700 bg-white dark:bg-slate-900 px-2 py-1 text-sm disabled:opacity-50"
        >
          {empresas.map((e) => (
            <option key={e.id} value={e.id}>
              {e.razonSocial} ({e.rfc})
            </option>
          ))}
        </select>
      </label>
      {error && (
        <span className="text-xs text-red-600 dark:text-red-400 mt-1">
          {error}
        </span>
      )}
    </div>
  );
}
