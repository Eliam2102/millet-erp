import { useAuth } from '@/lib/auth/useAuth';
import { seedDevUsers } from '@/lib/auth/config';

/**
 * Selector de usuarios seed para desarrollo local (ADR-0015). Solo se
 * renderiza si <c>VITE_AUTH_MODE === 'FakeForLocalDev'</c>. Vite inlinea
 * el env var en build time, así que builds de producción tree-shake este
 * componente entero.
 *
 * Click en un usuario → POST /api/dev/fake-login con su oid sintético.
 * Solo <c>dev-superadmin</c> está bootstrappeado con rol y empresa; los
 * demás se auto-provisionan (sin asignaciones, útil para probar UI de
 * "usuario sin acceso").
 */
export function DevUserSelector() {
  const { loginAsFakeUser, isLoading } = useAuth();

  return (
    <div className="space-y-3">
      <p className="text-sm text-slate-500 dark:text-slate-400">
        Modo dev (ADR-0015). Selecciona un usuario seed:
      </p>
      <div className="grid gap-2">
        {seedDevUsers.map((user) => (
          <button
            key={user.oid}
            onClick={() =>
              loginAsFakeUser(user.oid, user.email, user.nombre)
            }
            disabled={isLoading}
            className="text-left rounded-md border border-slate-200 dark:border-slate-700 px-4 py-3 hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
          >
            <div className="font-medium">{user.nombre}</div>
            <div className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">
              <code>{user.oid}</code> — {user.descripcion}
            </div>
          </button>
        ))}
      </div>
    </div>
  );
}
