import { createFileRoute } from '@tanstack/react-router';
import { DetalleCorrida } from '@/features/tesoreria/pages/DetalleCorrida';

/** Detalle de una corrida de pagos (T9.1-A): desglose por proveedor, firmas y autorización. */
export const Route = createFileRoute('/_app/tesoreria/corridas/$id')({
  component: DetalleCorridaRoute,
});

function DetalleCorridaRoute() {
  const { id } = Route.useParams();
  return <DetalleCorrida id={id} />;
}
