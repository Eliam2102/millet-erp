import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { adminRegistry, type AdminGrupo, type AdminSection } from '@/lib/admin/registry';
import { useAdminRegistry } from '@/lib/admin/use-admin-registry';
import { useState } from 'react';
import { Search } from 'lucide-react';
import { AdminLandingCard } from '@/components/admin/AdminLandingCard';
import { cn } from '@/lib/utils';

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
    const visible = adminRegistry.some((s) => permisos.includes(s.permisoRequerido));
    if (!visible) {
      throw redirect({ to: '/' });
    }
  },
  component: AdminLandingPage,
});

/**
 * Orden visual de los grupos (asc). Cuando un grupo no tiene opciones
 * visibles, simplemente no se renderiza.
 */
const ORDEN_GRUPOS: AdminGrupo[] = [
  'organizacion',
  'identidad',
  'facturacion_reglas',
  'catalogos',
  'datos_maestros',
  'modulos',
];

const TITULOS_GRUPOS: Record<AdminGrupo, string> = {
  organizacion: 'Organización',
  identidad: 'Accesos',
  facturacion_reglas: 'Facturación y reglas',
  catalogos: 'Catálogos',
  datos_maestros: 'Datos maestros',
  modulos: 'Módulos',
};

/**
 * Diseño «grupos con lista» (Eliam, 10-oct): título del grupo a la
 * izquierda y sus opciones a la derecha, todo visible y a un clic, con un
 * buscador que filtra por nombre o descripción.
 */
function AdminLandingPage() {
  const cards = useAdminRegistry();
  const [busqueda, setBusqueda] = useState('');

  const termino = normalizar(busqueda.trim());
  const filtradas = termino
    ? cards.filter((c) => normalizar(`${c.titulo} ${c.descripcion}`).includes(termino))
    : cards;
  const cardsPorGrupo = agruparPorGrupo(filtradas);
  const gruposVisibles = ORDEN_GRUPOS.filter((g) => (cardsPorGrupo[g] ?? []).length > 0);

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-3xl font-semibold tracking-[-0.01em] text-ink">Administración</h1>
          <p className="text-sm text-ink-muted">
            Configuración de Millet en el ERP. Solo verás las opciones para las que tengas permiso.
          </p>
        </div>
        <label className="relative w-full max-w-[380px]">
          <span className="sr-only">Buscar en Administración</span>
          <Search
            size={15}
            strokeWidth={1.8}
            aria-hidden="true"
            className="pointer-events-none absolute top-1/2 left-2.5 -translate-y-1/2 text-ink-muted"
          />
          <input
            type="search"
            value={busqueda}
            onChange={(e) => setBusqueda(e.target.value)}
            placeholder="Buscar en Administración…"
            className="h-[34px] w-full rounded-md border border-line bg-surface-card pr-3 pl-8 text-sm text-ink placeholder:text-ink-muted focus-visible:outline-2 focus-visible:outline-brand"
          />
        </label>
      </div>

      {gruposVisibles.length === 0 && (
        <div className="rounded-lg bg-surface-card px-4 py-6 text-center text-sm text-ink-muted shadow-card-flat">
          {termino
            ? 'Ninguna opción coincide con la búsqueda.'
            : 'No hay secciones disponibles para tu rol en este momento.'}
        </div>
      )}

      {gruposVisibles.map((grupo, i) => {
        const cardsDelGrupo = (cardsPorGrupo[grupo] ?? [])
          .slice()
          .sort((a, b) => a.orden - b.orden);
        return (
          <section
            key={grupo}
            aria-labelledby={`admin-grupo-${grupo}`}
            className={cn(
              'grid gap-3 md:grid-cols-[200px_1fr] md:gap-5',
              i > 0 && 'border-t border-line pt-5',
            )}
          >
            <div>
              <h2 id={`admin-grupo-${grupo}`} className="text-lg font-semibold text-ink">
                {TITULOS_GRUPOS[grupo]}
              </h2>
              <p className="text-xs text-ink-muted">
                {cardsDelGrupo.length} {cardsDelGrupo.length === 1 ? 'opción' : 'opciones'}
              </p>
            </div>
            <div className="grid grid-cols-1 gap-2 lg:grid-cols-2 2xl:grid-cols-3">
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

function normalizar(texto: string): string {
  return texto
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLowerCase();
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
