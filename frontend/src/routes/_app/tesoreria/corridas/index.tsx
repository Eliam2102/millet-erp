import { createFileRoute } from '@tanstack/react-router';
import { BandejaCorridas } from '@/features/tesoreria/pages/BandejaCorridas';

/** Corridas de pago (T9.1-A): armar, enviar a autorización y autorizar. */
export const Route = createFileRoute('/_app/tesoreria/corridas/')({
  component: BandejaCorridas,
});
