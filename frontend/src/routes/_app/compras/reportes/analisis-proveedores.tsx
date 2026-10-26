import { createFileRoute } from '@tanstack/react-router';
import { AnalisisProveedoresPage } from '@/features/compras/reportes/pages/AnalisisProveedoresPage';
import {
  AnalisisProveedoresSearchSchema,
  type AnalisisProveedoresSearch,
} from '@/features/compras/reportes/lib/analisis-proveedores-search-schema';

/** Ruta «Análisis de proveedores» (F1-COM-13): compras, saldos y cumplimiento. */
export const Route = createFileRoute('/_app/compras/reportes/analisis-proveedores')({
  component: AnalisisProveedoresPage,
  validateSearch: (input: Record<string, unknown>): AnalisisProveedoresSearch =>
    AnalisisProveedoresSearchSchema.parse(input),
});
