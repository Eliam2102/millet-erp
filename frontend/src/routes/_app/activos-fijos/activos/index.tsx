import { createFileRoute, redirect } from '@tanstack/react-router';
import { useAuthStore } from '@/lib/auth/auth-store';
import { PermisosCanonicos as P } from '@/lib/auth/permission-codes';
import { z } from 'zod';
import { ActivosPage } from '@/features/activos-fijos/pages/ActivosPage';
export const Route = createFileRoute('/_app/activos-fijos/activos/')({
  beforeLoad: () => {
    if (!useAuthStore.getState().permisos.includes(P.ActivosFijosActivosVer))
      throw redirect({ to: '/' });
  },
  validateSearch: z.object({
    claseId: z.string().optional(),
    sucursalId: z.string().optional(),
    responsableId: z.string().optional(),
    equipoId: z.string().optional(),
    estado: z.coerce.number().int().min(0).max(2).optional().catch(undefined),
    offset: z.coerce.number().int().min(0).optional().catch(0),
  }).parse,
  component: ActivosPage,
});
