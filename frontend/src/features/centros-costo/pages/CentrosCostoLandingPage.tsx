import { PageHeader } from '@/components/erp/PageHeader';
import { AppLauncherCard } from '@/components/layout/AppLauncherCard';
import { useAuthStore } from '@/lib/auth/auth-store';
import { filtrarModuloPorPermisos, type NavModulo } from '@/lib/nav';
import { Layers, ListTree, Users } from 'lucide-react';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Landing del módulo Centros de costo — <c>/centros-costo</c>
 * (CECO-FE-PR1, 05 §2). Mismo patrón que <c>AlmacenLandingPage</c>:
 * replica las cards del modal del sidebar (mismo
 * <c>filtrarModuloPorPermisos</c>) en formato página.
 *
 * <para>Sin permisos, mensaje neutro (el <c>/_app</c> ya gateó la
 * autenticación; "0 cards visibles" equivale al modal vacío del
 * AppLauncher).</para>
 */
export function CentrosCostoLandingPage() {
  const permisos = useAuthStore((s) => s.permisos);
  const filtrado = filtrarModuloPorPermisos(moduloCentrosCostoLanding, permisos);
  const hayCards = filtrado.secciones.some((s) => s.cards.length > 0);

  return (
    <div className="space-y-5">
      <PageHeader
        titulo="Centros de costo"
        descripcion="Catálogo jerárquico de centros de costo y asignación de alcance por usuario."
      />

      {!hayCards && (
        <div
          className="rounded-md bg-surface-muted/50 px-4 py-6 text-center text-sm text-ink-muted"
          data-testid="centros-costo-sin-cards"
        >
          Sin pantallas disponibles para tu rol en este módulo. Contacta a tu administrador para que
          te asigne los permisos correspondientes.
        </div>
      )}

      {hayCards && (
        <div className="space-y-8">
          {filtrado.secciones.map((seccion) => (
            <section key={seccion.label} className="space-y-3">
              <h2 className="text-xs font-semibold uppercase tracking-wide text-ink-muted">
                {seccion.label}
              </h2>
              <div className="grid grid-cols-1 gap-3 md:grid-cols-2 lg:grid-cols-3">
                {seccion.cards.map((card) => (
                  <AppLauncherCard
                    key={card.to}
                    card={card}
                    onSelect={() => {
                      /* Sin modal contenedor; navegación directa por Link. */
                    }}
                  />
                ))}
              </div>
            </section>
          ))}
        </div>
      )}
    </div>
  );
}

/**
 * Copia local del módulo nav para evitar dep circular con <c>nav.ts</c>
 * (mismo criterio que <c>AlmacenLandingPage</c>). Mantener en sync con la
 * declaración de <c>moduloCentrosCosto</c> en <c>nav.ts</c>.
 */
const moduloCentrosCostoLanding: NavModulo = {
  moduloId: 'centros-costo',
  label: 'Centros de costo',
  icon: Layers,
  secciones: [
    {
      label: 'Configuración',
      cards: [
        {
          label: 'Centros de costo',
          description:
            'Catálogo jerárquico de 3 niveles con grupos de clasificación: consulta y administración.',
          to: '/centros-costo/configuracion',
          icon: ListTree,
          permissionsAny: [
            PermisosCanonicos.CentrosCostoCatalogoLeer,
            PermisosCanonicos.CentrosCostoCatalogoAdministrar,
          ],
        },
        {
          label: 'Asignación de centros de costo',
          description:
            'Alcance por usuario: marcar cualquier nivel del árbol asigna sus hojas (tri-estado).',
          to: '/centros-costo/asignaciones',
          icon: Users,
          permission: PermisosCanonicos.CentrosCostoAsignacionesAdministrar,
        },
      ],
    },
  ],
};
