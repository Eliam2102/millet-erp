import { createFileRoute } from '@tanstack/react-router';
import { BandejaConciliaciones } from '@/features/tesoreria/pages/BandejaConciliaciones';

/** Conciliación bancaria mensual (T9.2 · 86e3ke6ne): cruce extracto BBVA ↔ movimientos internos por cuenta. */
export const Route = createFileRoute('/_app/tesoreria/conciliacion/')({
  component: BandejaConciliaciones,
});
