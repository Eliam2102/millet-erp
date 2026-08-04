import { createFileRoute, redirect } from '@tanstack/react-router';
import { DEFAULT_BANDEJA_SEARCH } from '@/features/compras/lib/bandeja-search-schema';

/**
 * Placeholder del módulo Compras: <c>/compras</c> redirige a la bandeja
 * principal de requisiciones (<c>/compras/requisiciones</c>) con los
 * defaults del schema Zod (offset=0, limit=50, sin filtros).
 *
 * <para>El gate de permiso fino vive en el sidebar (<c>Sidebar.tsx</c>):
 * el usuario sin <c>compras.requisiciones.leer</c> no ve el item, así
 * que aterrizar acá ya implica que el permiso está. Si llega por URL
 * directa, el backend igual rechazará cualquier llamada subsiguiente
 * con 403.</para>
 */
export const Route = createFileRoute('/_app/compras/')({
  beforeLoad: () => {
    throw redirect({
      to: '/compras/requisiciones',
      search: DEFAULT_BANDEJA_SEARCH,
    });
  },
});
