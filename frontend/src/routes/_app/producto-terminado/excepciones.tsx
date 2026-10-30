import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { ProductoTerminadoPage } from '@/features/producto-terminado/pages/ProductoTerminadoPage';
import { ptSearchSchema } from '@/features/producto-terminado/lib/search';
export const Route = createFileRoute('/_app/producto-terminado/excepciones')({
  validateSearch: ptSearchSchema.parse,
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(P.ProductoTerminadoExcepcionesGestionar))
      throw redirect({ to: '/' });
  },
  component: () => <ProductoTerminadoPage exceptions={true} />,
});
