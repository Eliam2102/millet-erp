import { createFileRoute } from '@tanstack/react-router';
import { AnalisisComprasPage } from '@/features/compras/reportes/AnalisisComprasPage';

export const Route = createFileRoute('/_app/compras/reportes/analisis')({
  component: AnalisisComprasPage,
});
