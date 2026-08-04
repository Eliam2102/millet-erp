import { createFileRoute } from '@tanstack/react-router';
import { DetalleMovimiento } from '@/features/tesoreria/pages/DetalleMovimiento';

/**
 * Detalle de movimiento bancario (TES-FE-PR2, P3): cabecera +
 * aplicaciones (reversa RN-10) + contramovimientos.
 */
export const Route = createFileRoute('/_app/tesoreria/movimientos/$id')({
  component: DetalleMovimientoRoute,
});

function DetalleMovimientoRoute() {
  const { id } = Route.useParams();
  return <DetalleMovimiento id={id} />;
}
