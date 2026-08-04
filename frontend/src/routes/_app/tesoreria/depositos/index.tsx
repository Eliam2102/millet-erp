import { createFileRoute } from '@tanstack/react-router';
import { BandejaDepositos } from '@/features/tesoreria/pages/BandejaDepositos';

/** Depósitos por confirmar (P2, TES-FE-PR4b): propuestas CxC + expectativas de Caja (§3.3, TES-9). */
export const Route = createFileRoute('/_app/tesoreria/depositos/')({
  component: BandejaDepositos,
});
