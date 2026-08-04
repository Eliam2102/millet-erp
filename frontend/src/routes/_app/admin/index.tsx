import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { adminRegistry, type AdminGrupo, type AdminSection } from '@/lib/admin/registry';
import { useAdminRegistry } from '@/lib/admin/use-admin-registry';
import { AdminLandingCard } from '@/components/admin/AdminLandingCard';

/**
 * Landing del área de Administración (<c>/admin</c>).
 *
 * <para><b>Guard</b>: si el usuario no tiene ninguna card visible
 * (ningún <c>permisoRequerido</c> coincide), redirige a <c>/</c>. El
 * gear del Topbar ya hace el mismo gate, pero la URL directa también
 * debe estar protegida.</para>
 *
 * <para><b>Layout</b>: grid de cards agrupado por
 * <see cref="AdminGrupo"/>. Cada grupo es una sección con título y un
 * grid de <see cref="AdminLandingCard"/>. Si solo hay un grupo (caso de
 * UF-Admin-PR1: solo "modulos" con la card de Compras), igualmente se
 * renderiza con su encabezado para que el patrón sea consistente cuando
 * llegue el segundo grupo.</para>
 */
export const Route = createFileRoute('/_app/admin/')({
  beforeLoad: () => {
    const permisos = useAuthStore.getState().permisos;
    const visible = adminRegistry.some((s) =>
      permisos.includes(s.permisoRequerido),
    );
    if (!visible) {
      throw redirect({ to: '/' });
    }
  },
  component: AdminLandingPage,
});

/**
 * Orden visual de los grupos (asc). Cuando un grupo no tiene cards
 * visibles, simplemente no se renderiza.
 */
const ORDEN_GRUPOS: AdminGrupo[] = [
  'organizacion',
  'identidad',
  'catalogos',
  'datos_maestros',
  'modulos',
];

const TITULOS_GRUPOS: Record<AdminGrupo, string> = {
  organizacion: 'Organización',
  identidad: 'Identidad y accesos',
  catalogos: 'Catálogos',
  datos_maestros: 'Datos maestros',
  modulos: 'Módulos',
};

function AdminLandingPage() {
  const cards = useAdminRegistry();

  const cardsPorGrupo = agruparPorGrupo(cards);
  const gruposVisibles = ORDEN_GRUPOS.filter(
    (g) => (cardsPorGrupo[g] ?? []).length > 0,
  );

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Administración</h1>
        <p className="text-sm text-muted-foreground">
          Configuración del sistema. Solo verás las secciones para las que
          tengas permiso.
        </p>
      </div>

      {gruposVisibles.length === 0 && (
        <div className="rounded-md bg-muted/50 px-4 py-6 text-center text-sm text-muted-foreground">
          No hay secciones disponibles para tu rol en este momento.
        </div>
      )}

      {gruposVisibles.map((grupo) => {
        const cardsDelGrupo = (cardsPorGrupo[grupo] ?? []).slice().sort(
          (a, b) => a.orden - b.orden,
        );
        return (
          <section key={grupo} className="space-y-3">
            <h2 className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
              {TITULOS_GRUPOS[grupo]}
            </h2>
            <div className="grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-3">
              {cardsDelGrupo.map((card) => (
                <AdminLandingCard key={card.id} section={card} />
              ))}
            </div>
          </section>
        );
      })}
    </div>
  );
}

function agruparPorGrupo(
  cards: readonly AdminSection[],
): Partial<Record<AdminGrupo, AdminSection[]>> {
  const result: Partial<Record<AdminGrupo, AdminSection[]>> = {};
  for (const card of cards) {
    const bucket = result[card.grupo] ?? [];
    bucket.push(card);
    result[card.grupo] = bucket;
  }
  return result;
}
