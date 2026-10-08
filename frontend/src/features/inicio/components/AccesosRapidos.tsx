import { Link } from '@tanstack/react-router';
import { Card } from '@/components/ui/card';
import { useAuthStore } from '@/lib/auth/auth-store';
import { modulosInicio } from '../modulos';

export function AccesosRapidos() {
  const permisos = useAuthStore((s) => s.permisos);
  const accesos = modulosInicio(permisos);
  return (
    <section aria-labelledby="modulos-inicio" className="min-w-0 space-y-3">
      <h2 id="modulos-inicio" className="text-lg font-semibold text-ink">
        Módulos
      </h2>
      {accesos.length === 0 ? (
        <p className="text-xs text-ink-muted">No hay módulos disponibles para tu rol.</p>
      ) : (
        <div className="grid grid-cols-2 gap-3">
          {accesos.map(({ to, description, modulo, icon: Icon }) => (
            <Card key={modulo} className="min-w-0 bg-surface-card text-ink shadow-card-flat">
              <Link
                to={to}
                search={{}}
                className="flex h-full min-h-touch items-center gap-3 rounded-lg p-3 hover:bg-surface-subtle focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand"
              >
                <span className="flex size-8 shrink-0 items-center justify-center rounded-md bg-surface-muted">
                  <Icon
                    className="size-4.5 text-ink-secondary"
                    strokeWidth={1.6}
                    aria-hidden="true"
                  />
                </span>
                <span className="min-w-0">
                  <span className="block truncate text-sm font-medium" title={modulo}>
                    {modulo}
                  </span>
                  <span className="mt-1 block truncate text-xs text-ink-muted" title={description}>
                    {description}
                  </span>
                </span>
              </Link>
            </Card>
          ))}
        </div>
      )}
    </section>
  );
}
