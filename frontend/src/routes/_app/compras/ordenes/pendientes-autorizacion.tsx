import { z } from 'zod';
import { createFileRoute } from '@tanstack/react-router';
import { BandejaPendientesOc } from '@/features/compras/ordenes/pages/BandejaPendientesOc';

/**
 * Ruta P2 — Bandeja de pendientes de autorización OC (UF4-PR1).
 *
 * <para><c>validateSearch</c> solo para la paginación (<c>page</c>/<c>pageSize</c>):
 * la bandeja maneja el nivel (Nivel1/Nivel2) en estado local porque depende de
 * los permisos del usuario y se selecciona desde un tab switcher dentro de la
 * página, no del URL.</para>
 */
const PendientesOcSearchSchema = z.object({
  page: z.coerce.number().int().min(1).optional(),
  pageSize: z.coerce.number().int().min(10).max(200).optional(),
});

export const Route = createFileRoute(
  '/_app/compras/ordenes/pendientes-autorizacion',
)({
  component: BandejaPendientesOc,
  validateSearch: (input: Record<string, unknown>) =>
    PendientesOcSearchSchema.parse(input),
});
