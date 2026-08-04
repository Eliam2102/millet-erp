import { createFileRoute } from '@tanstack/react-router';
import { BandejaPagosACuenta } from '@/features/tesoreria/pages/BandejaPagosACuenta';

/**
 * Pagos a cuenta abiertos (TES-FE-PR4, §3.4): bandeja con antigüedad,
 * alta con Sheet (gate RN-2) y liga tardía sin re-desembolso.
 */
export const Route = createFileRoute('/_app/tesoreria/pagos-cuenta/')({
  component: BandejaPagosACuenta,
});
