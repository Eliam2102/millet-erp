import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { PolizaDetallePage } from '@/features/contabilidad/pages/PolizaDetallePage';
import { PolizasSearchSchema, type PolizasSearch } from '@/features/contabilidad/lib/polizas';

/** Guard de UX: sin ContabilidadPolizasLeer se redirige; la autorización real es del API (403). */
export const Route = createFileRoute('/_app/contabilidad/polizas/$id')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(PermisosCanonicos.ContabilidadPolizasLeer)) throw redirect({ to: '/' });
  },
  validateSearch: (input: Record<string, unknown>): PolizasSearch => PolizasSearchSchema.parse(input),
  component: DetalleRoute,
});

function DetalleRoute() {
  const { id } = Route.useParams();
  const search = Route.useSearch();
  const navigate = Route.useNavigate();
  return (
    <PolizaDetallePage
      id={id}
      search={search}
      onNavegar={(destino) =>
        void (destino
          ? navigate({ to: '/contabilidad/polizas/$id', params: { id: destino }, search })
          : navigate({ to: '/contabilidad/polizas', search }))
      }
    />
  );
}
