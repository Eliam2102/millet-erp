import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { z } from 'zod';
import { zId } from '@/lib/z-id';
import { ExpedientesPage } from '@/features/comercio-exterior/pages/ExpedientesPage';
const filtros = z.object({
  sucursalId: zId().or(z.literal('todas')).optional().catch(undefined),
  tipo: z.coerce.number().int().min(1).max(2).optional().catch(undefined),
  estado: z.coerce.number().int().min(1).max(2).optional().catch(undefined),
  atraso: z
    .enum(['true', 'false'])
    .transform((v) => v === 'true')
    .or(z.boolean())
    .optional()
    .catch(undefined),
  q: z.string().max(200).optional(),
  offset: z.coerce.number().int().min(0).optional().catch(0),
});
export const Route = createFileRoute('/_app/comercio-exterior/expedientes/')({
  beforeLoad: () => {
    if (
      !useAuthStore.getState().permisos.includes(PermisosCanonicos.ComercioExteriorExpedientesLeer)
    ) {
      throw redirect({ to: '/' });
    }
  },
  validateSearch: filtros.parse,
  component: ExpedientesPage,
});
