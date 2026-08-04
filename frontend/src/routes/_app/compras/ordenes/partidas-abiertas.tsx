import { createFileRoute } from '@tanstack/react-router';
import { PartidasAbiertas } from '@/features/compras/ordenes/pages/PartidasAbiertas';
import {
  PartidasAbiertasSearchSchema,
  type PartidasAbiertasSearch,
} from '@/features/compras/ordenes/lib/partidas-abiertas-search-schema';

/**
 * Ruta P9 — Partidas abiertas (UF7-PR1).
 */
export const Route = createFileRoute(
  '/_app/compras/ordenes/partidas-abiertas',
)({
  component: PartidasAbiertas,
  validateSearch: (input: Record<string, unknown>): PartidasAbiertasSearch =>
    PartidasAbiertasSearchSchema.parse(input),
});
