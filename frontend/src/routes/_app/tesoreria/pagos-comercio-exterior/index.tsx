import { createFileRoute } from '@tanstack/react-router';
import { BandejaPagosComercioExterior } from '@/features/tesoreria/pages/BandejaPagosComercioExterior';

/** Autorización de pagos de comercio exterior en dos niveles (I13.3). */
export const Route = createFileRoute('/_app/tesoreria/pagos-comercio-exterior/')({
  component: BandejaPagosComercioExterior,
});
