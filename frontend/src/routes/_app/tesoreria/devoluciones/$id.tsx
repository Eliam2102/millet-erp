import { createFileRoute } from '@tanstack/react-router';
import { DetalleDevolucion } from '@/features/tesoreria/pages/DetalleDevolucion';

/** Detalle de una devolución a cliente (T9.6): origen, importe, cuenta destino y seguimiento. */
export const Route = createFileRoute('/_app/tesoreria/devoluciones/$id')({
  component: DetalleDevolucionRoute,
});

function DetalleDevolucionRoute() {
  const { id } = Route.useParams();
  return <DetalleDevolucion id={id} />;
}
