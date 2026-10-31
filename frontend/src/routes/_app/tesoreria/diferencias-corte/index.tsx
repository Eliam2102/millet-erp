import { createFileRoute } from '@tanstack/react-router';
import { BandejaDiferenciasCorte } from '@/features/tesoreria/pages/BandejaDiferenciasCorte';

/** Diferencias de corte (86e3vr1tj): Tesorería autoriza o rechaza faltantes y sobrantes de caja. */
export const Route = createFileRoute('/_app/tesoreria/diferencias-corte/')({
  component: BandejaDiferenciasCorte,
});
