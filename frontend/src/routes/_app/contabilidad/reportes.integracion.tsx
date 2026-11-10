import { createFileRoute, redirect } from '@tanstack/react-router';
import { z } from 'zod';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ReporteIntegracionPage } from '@/features/contabilidad/pages/ReporteIntegracionPage';

/** Guard de UX: sin ContabilidadPolizasLeer se redirige; la autorización real es del API (403). */
export const Route = createFileRoute('/_app/contabilidad/reportes/integracion')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(PermisosCanonicos.ContabilidadPolizasLeer)) throw redirect({ to: '/' });
  },
  validateSearch: z.object({
    tipo: z.enum(['Ingresos', 'Costo', 'Gastos', 'Financiamiento', 'Compras']).optional().catch(undefined),
  }).parse,
  component: IntegracionRuta,
});

function IntegracionRuta() {
  const { tipo } = Route.useSearch();
  return <ReporteIntegracionPage key={tipo ?? 'Ingresos'} tipoInicial={tipo} />;
}
