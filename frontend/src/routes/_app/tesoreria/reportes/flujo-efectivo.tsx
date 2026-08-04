import { createFileRoute } from '@tanstack/react-router';
import { ReporteFlujoEfectivoPage } from '@/features/tesoreria/pages/ReporteFlujoEfectivoPage';
import { ReportesTesoreriaSearchSchema } from '@/features/tesoreria/lib/reportes-search-schema';

/** Reporte de flujo de efectivo (TES-FE-PR6, ADR-0036). */
export const Route = createFileRoute('/_app/tesoreria/reportes/flujo-efectivo')({
  validateSearch: ReportesTesoreriaSearchSchema.parse,
  component: ReporteFlujoEfectivoPage,
});
