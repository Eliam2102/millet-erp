import { createFileRoute } from '@tanstack/react-router';
import { FacturacionLandingPage } from '@/features/facturacion/pages/FacturacionLandingPage';

/**
 * Landing del módulo Facturación — <c>/facturacion</c>. Renderiza las
 * cards de las secciones del módulo gateadas por permiso (mismo patrón
 * que Compras / Almacén / CxP). Sin permisos <c>facturacion.*</c>, la
 * landing muestra un mensaje neutro. La autenticación ya la cubrió
 * <c>/_app</c>.
 */
export const Route = createFileRoute('/_app/facturacion/')({
  component: FacturacionLandingPage,
});
