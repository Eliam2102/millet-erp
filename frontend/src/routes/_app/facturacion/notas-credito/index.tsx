import { createFileRoute, redirect } from '@tanstack/react-router';
import { NotasCreditoPorAutorizar } from '@/features/facturacion/pages/NotasCreditoPorAutorizar';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/** NC de A+W por autorizar — <c>/facturacion/notas-credito</c> (86e3wjt5r). */
export const Route = createFileRoute('/_app/facturacion/notas-credito/')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(PermisosCanonicos.FacturacionNotasCreditoLeer))
      throw redirect({ to: '/facturacion' });
  },
  component: NotasCreditoPorAutorizar,
});
