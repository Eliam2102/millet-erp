import { createFileRoute } from '@tanstack/react-router';
import { useAuth } from '@/lib/auth/useAuth';
import { RequirePermission } from '@/components/auth/RequirePermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Home <c>/</c> dentro del shell autenticado. Por ahora muestra una vista
 * de "sesión activa" + demos de gating por permiso. Reemplazará por un
 * dashboard real cuando los módulos tengan métricas que mostrar.
 */
export const Route = createFileRoute('/_app/')({
  component: HomeComponent,
});

function HomeComponent() {
  const { user, currentEmpresa, empresas } = useAuth();

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">
          Bienvenido, {user?.nombre?.split(' ')[0] ?? 'usuario'}
        </h1>
        <p className="text-sm text-muted-foreground">
          {currentEmpresa
            ? `Trabajando en ${currentEmpresa.razonSocial}`
            : 'Sin empresa seleccionada'}
        </p>
      </div>

      <div className="rounded-lg border bg-card p-6 max-w-2xl">
        <h2 className="text-lg font-semibold mb-3">Sesión activa</h2>
        <dl className="grid grid-cols-[max-content_1fr] gap-x-4 gap-y-2 text-sm">
          <dt className="text-muted-foreground">Usuario</dt>
          <dd>{user?.nombre}</dd>
          <dt className="text-muted-foreground">Email</dt>
          <dd>{user?.email}</dd>
          <dt className="text-muted-foreground">User ID</dt>
          <dd className="font-mono text-xs">{user?.id}</dd>
          <dt className="text-muted-foreground">Empresas</dt>
          <dd>
            {empresas.length === 0 ? (
              <span className="text-amber-600">
                Ninguna asignada — contacta a tu admin para que asigne roles y
                empresas.
              </span>
            ) : (
              <span>{empresas.length} accesibles</span>
            )}
          </dd>
        </dl>
      </div>

      <RequirePermission code={PermisosCanonicos.InfraHealthLeer}>
        <div className="rounded-lg border border-blue-200 bg-blue-50 p-4 max-w-2xl text-sm">
          <strong>✓ Tienes el permiso{' '}
            <code className="rounded bg-white px-1">infra.health.leer</code>.
          </strong>{' '}
          Esto solo aparece si el rol del usuario lo incluye (el SuperAdmin sí).
        </div>
      </RequirePermission>

      <RequirePermission
        code="modulo.no.existente"
        fallback={
          <div className="rounded-lg border bg-muted/40 p-4 max-w-2xl text-sm text-muted-foreground">
            No tienes el permiso <code>modulo.no.existente</code> (esperado —
            ese código no existe en el manifest, ningún usuario lo tiene).
          </div>
        }
      >
        <div>Esto no aparece</div>
      </RequirePermission>
    </div>
  );
}
