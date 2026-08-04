import { createFileRoute } from '@tanstack/react-router';
import { BandejaPagos } from '@/features/tesoreria/pages/BandejaPagos';
import { PagosSearchSchema } from '@/features/tesoreria/lib/pagos-search-schema';

/**
 * Bandeja de pasivos pendientes de pago (TES-FE-PR2, P2): filtros
 * server-side + multi-selección → Sheet "Registrar pago".
 */
export const Route = createFileRoute('/_app/tesoreria/pagos/')({
  validateSearch: PagosSearchSchema.parse,
  component: BandejaPagos,
});
