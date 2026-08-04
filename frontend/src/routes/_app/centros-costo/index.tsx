import { createFileRoute } from '@tanstack/react-router';
import { CentrosCostoLandingPage } from '@/features/centros-costo/pages/CentrosCostoLandingPage';

/**
 * Landing del módulo Centros de Costo — <c>/centros-costo</c>
 * (CECO-FE-PR1, 05 §2). El gate fino de permisos vive en cada card de
 * <c>nav.ts</c>; la landing replica el conjunto con
 * <c>filtrarModuloPorPermisos</c> (mismo patrón que Almacén).
 */
export const Route = createFileRoute('/_app/centros-costo/')({
  component: CentrosCostoLandingPage,
});
