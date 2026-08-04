import { createFileRoute } from '@tanstack/react-router';
import { TesoreriaLandingPage } from '@/features/tesoreria/pages/TesoreriaLandingPage';

/**
 * Landing del módulo Tesorería — <c>/tesoreria</c> (TES-FE-PR1).
 * Renderiza las cards de las secciones del módulo gateadas por permiso
 * (mismo patrón que CxC / Facturación / Compras). La autenticación ya
 * la cubrió <c>/_app</c>.
 */
export const Route = createFileRoute('/_app/tesoreria/')({
  component: TesoreriaLandingPage,
});
