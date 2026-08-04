import { createFileRoute } from '@tanstack/react-router';
import { CxcLandingPage } from '@/features/cxc/pages/CxcLandingPage';

/**
 * Landing del módulo Cuentas por Cobrar — <c>/cxc</c>. Renderiza las
 * cards de las secciones del módulo gateadas por permiso (mismo patrón
 * que Facturación / Compras / CxP). Sin permisos
 * <c>cuentas_por_cobrar.*</c>, la landing muestra un mensaje neutro. La
 * autenticación ya la cubrió <c>/_app</c>.
 */
export const Route = createFileRoute('/_app/cxc/')({
  component: CxcLandingPage,
});
