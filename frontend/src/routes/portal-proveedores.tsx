import { createFileRoute } from '@tanstack/react-router';
import { PortalProveedoresPage } from '@/features/portal-proveedores/PortalProveedoresPage';

/**
 * <c>/portal-proveedores</c> — portal de proveedores (P8.3). Va FUERA del layout <c>_app</c>: no hay
 * guard de sesión del ERP, ni AppShell, ni menú. Su sesión es el token propio del portal.
 */
export const Route = createFileRoute('/portal-proveedores')({
  component: PortalProveedoresPage,
});
