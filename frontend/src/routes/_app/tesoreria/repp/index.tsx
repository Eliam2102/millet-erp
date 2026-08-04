import { createFileRoute } from '@tanstack/react-router';
import { BandejaReppPendientes } from '@/features/tesoreria/pages/BandejaReppPendientes';

/** REPP de proveedor pendientes (P2, TES-FE-PR6b): SLA 5 días + registro (§3.6.b, TES-4). */
export const Route = createFileRoute('/_app/tesoreria/repp/')({
  component: BandejaReppPendientes,
});
