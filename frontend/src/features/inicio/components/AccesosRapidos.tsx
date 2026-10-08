import { Link } from '@tanstack/react-router';
import { Card } from '@/components/ui/card';
import { accesosNavegacion } from '@/lib/nav';
import { useAuthStore } from '@/lib/auth/auth-store';
import { prioridadAccesos } from '../config';

export function AccesosRapidos() {
  const permisos = useAuthStore((s) => s.permisos);
  const disponibles = accesosNavegacion(permisos).filter((acceso) => acceso.to !== '/');
  const prioritarios = prioridadAccesos.flatMap((to) =>
    disponibles.filter((acceso) => acceso.to === to),
  );
  const accesos = [
    ...prioritarios,
    ...disponibles.filter((acceso) => !prioridadAccesos.includes(acceso.to)),
  ].slice(0, 8);
  return (
    <section aria-labelledby="accesos-rapidos" className="min-w-0 space-y-3">
      <h2 id="accesos-rapidos" className="text-lg font-semibold text-ink">
        Accesos rápidos
      </h2>
      {accesos.length === 0 ? (
        <p className="text-sm text-ink-muted">No hay accesos disponibles para tu rol.</p>
      ) : (
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          {accesos.map(({ to, label, modulo, icon: Icon }) => (
            <Card key={to} className="min-w-0 bg-surface-card text-ink shadow-card">
              <Link
                to={to}
                search={{}}
                className="flex h-full min-h-touch flex-col items-start gap-3 rounded-lg p-3 hover:bg-surface-subtle focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand"
              >
                <span className="flex size-8 items-center justify-center rounded-md bg-surface-muted">
                  <Icon
                    className="size-4.5 text-ink-secondary"
                    strokeWidth={1.6}
                    aria-hidden="true"
                  />
                </span>
                <div className="min-w-0 break-words">
                  <p className="text-sm font-medium">{label}</p>
                  <p className="mt-1 text-xs text-ink-muted">{modulo}</p>
                </div>
              </Link>
            </Card>
          ))}
        </div>
      )}
    </section>
  );
}
