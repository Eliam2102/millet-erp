import { createFileRoute } from '@tanstack/react-router';
import { BandejaMovimientos } from '@/features/tesoreria/pages/BandejaMovimientos';
import { MovimientosSearchSchema } from '@/features/tesoreria/lib/movimientos-search-schema';

/**
 * Libro de movimientos bancarios (TES-FE-PR2, P1): tabla con filtros
 * server-side; "Ver" navega al detalle.
 */
export const Route = createFileRoute('/_app/tesoreria/movimientos/')({
  validateSearch: MovimientosSearchSchema.parse,
  component: BandejaMovimientos,
});
