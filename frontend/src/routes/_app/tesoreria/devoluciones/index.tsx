import { createFileRoute } from '@tanstack/react-router';
import { BandejaDevoluciones } from '@/features/tesoreria/pages/BandejaDevoluciones';
import {
  DevolucionesSearchSchema,
  type DevolucionesSearch,
} from '@/features/tesoreria/lib/devoluciones-search-schema';

/** Devoluciones de dinero a clientes (T9.6): depósito erróneo y reembolso de NC. */
export const Route = createFileRoute('/_app/tesoreria/devoluciones/')({
  component: BandejaDevoluciones,
  validateSearch: (input: Record<string, unknown>): DevolucionesSearch =>
    DevolucionesSearchSchema.parse(input),
});
