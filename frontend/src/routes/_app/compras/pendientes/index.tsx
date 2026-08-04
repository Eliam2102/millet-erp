import { createFileRoute } from '@tanstack/react-router';
import { BandejaPendientes } from '@/features/compras/pages/BandejaPendientes';
import {
  PendientesSearchSchema,
  type PendientesSearch,
} from '@/features/compras/lib/pendientes-search-schema';

/**
 * Ruta P2 — Bandeja de pendientes de autorización (doc 05 §5).
 *
 * <para><c>validateSearch</c> aplica los defaults del Zod schema y
 * filtra valores inválidos.</para>
 */
export const Route = createFileRoute('/_app/compras/pendientes/')({
  component: BandejaPendientes,
  validateSearch: (input: Record<string, unknown>): PendientesSearch =>
    PendientesSearchSchema.parse(input),
});
