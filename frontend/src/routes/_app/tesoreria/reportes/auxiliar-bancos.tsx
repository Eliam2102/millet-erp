import { createFileRoute } from '@tanstack/react-router';
import { ReporteAuxiliarBancosPage } from '@/features/tesoreria/pages/ReporteAuxiliarBancosPage';
import { ReportesTesoreriaSearchSchema } from '@/features/tesoreria/lib/reportes-search-schema';

/** Reporte auxiliar de bancos (TES-FE-PR6, ADR-0036). */
export const Route = createFileRoute('/_app/tesoreria/reportes/auxiliar-bancos')({
  validateSearch: ReportesTesoreriaSearchSchema.parse,
  component: ReporteAuxiliarBancosPage,
});
