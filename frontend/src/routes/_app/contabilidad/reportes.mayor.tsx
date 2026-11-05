import { createFileRoute, redirect } from '@tanstack/react-router';
import { z } from 'zod';
import { zId } from '@/lib/z-id';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { ReporteMayorPage } from '@/features/contabilidad/pages/ReporteMayorPage';

/** Guard de UX: sin ContabilidadPolizasLeer se redirige; la autorización real es del API (403). Filtros en la URL. */
export const Route = createFileRoute('/_app/contabilidad/reportes/mayor')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(PermisosCanonicos.ContabilidadPolizasLeer)) throw redirect({ to: '/' });
  },
  validateSearch: z.object({
    cuentaId: zId().optional().catch(undefined),
    anio: z.coerce.number().int().min(2000).max(2999).optional().catch(undefined),
    desde: z.coerce.number().int().min(1).max(13).optional().catch(undefined),
    hasta: z.coerce.number().int().min(1).max(13).optional().catch(undefined),
    sucursalId: zId().optional().catch(undefined),
  }).parse,
  component: ReporteMayorPage,
});
