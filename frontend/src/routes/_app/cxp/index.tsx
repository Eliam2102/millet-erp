import { createFileRoute } from '@tanstack/react-router';
import { CxpLandingPage } from '@/features/cxp/pages/CxpLandingPage';

/**
 * Landing del módulo Cuentas por Pagar — <c>/cxp</c>. Renderiza cards
 * de las secciones del módulo gateadas por permisos (mismo patrón que
 * Almacén / Compras: el sidebar abre el modal con cards y, si el
 * usuario llega por URL directa, esta landing renderiza el mismo
 * conjunto).
 *
 * <para>El gate de permiso fino está en cada card de <c>nav.ts</c>;
 * aquí no se aplica gate de módulo porque <c>/_app</c> ya cubrió la
 * autenticación. Si el usuario no tiene ningún permiso
 * <c>cuentas_por_pagar.*</c>, ve un mensaje neutro.</para>
 */
export const Route = createFileRoute('/_app/cxp/')({
  component: CxpLandingPage,
});
