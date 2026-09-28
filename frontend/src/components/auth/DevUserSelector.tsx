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
    <div className="mt-7">
      <p className="text-sm font-semibold text-[#253755]">
        Acceso de prueba
      </p>
      <div className="mt-3 grid max-h-[40vh] gap-2 overflow-y-auto pr-1">
        {seedDevUsers.map((user) => (
          <button
            type="button"
            key={user.oid}
            onClick={() => void loginAsFakeUser(user.oid, user.email, user.nombre)}
            disabled={isLoading}
            className="rounded-lg border border-[#d7dfeb] bg-white px-4 py-3 text-left transition-colors hover:bg-[#f7f9fc] focus-visible:outline-3 focus-visible:outline-offset-2 focus-visible:outline-[#6679c9] disabled:cursor-wait disabled:opacity-50"
          >
            <div className="text-sm font-semibold text-[#253755]">{user.nombre}</div>
            <div className="mt-0.5 text-xs text-[#68778d]">
              {user.descripcion}
            </div>
          </button>
        ))}
      </div>
    </div>
  );
}
