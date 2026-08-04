import { createFileRoute } from '@tanstack/react-router';
import { BandejaCuentas } from '@/features/tesoreria/pages/BandejaCuentas';

/** Catálogo de cuentas bancarias propias (P1, TES-7 revisada): saldos + CRUD. */
export const Route = createFileRoute('/_app/tesoreria/cuentas/')({
  component: BandejaCuentas,
});
