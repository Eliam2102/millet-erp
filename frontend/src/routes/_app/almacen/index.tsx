import { createFileRoute } from '@tanstack/react-router';
import { AlmacenLandingPage } from '@/features/almacen/pages/AlmacenLandingPage';

/**
 * Landing del módulo Almacén — <c>/almacen</c>. Muestra cards de las
 * secciones del módulo gateadas por permisos (mismo patrón que
 * Compras: el sidebar abre el modal con cards y, si el usuario llega
 * por URL directa, esta landing renderiza el mismo conjunto).
 *
 * <para>El gate de permiso fino está en cada card de
 * <c>nav.ts</c>; aquí solo verificamos que el usuario tenga al menos
 * uno de los permisos <c>almacen.*</c> (cualquiera). Sin permiso →
 * <c>AccessDenied</c>.</para>
 */
export const Route = createFileRoute('/_app/almacen/')({
  component: AlmacenLandingPage,
});
