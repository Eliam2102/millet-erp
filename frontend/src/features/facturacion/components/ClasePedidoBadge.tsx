import { Badge } from '@/components/ui/badge';
import type { ClasificacionPedidoLectura } from '@/features/facturacion/api/types';
import { etiquetaClasePedido } from '@/features/facturacion/lib/glosario';

/**
 * Badge de la clase de un pedido manual (F6.1): «Venta directa · <Subtipo>»
 * en tono info y «Emergencia» en warning (queda marcado para conciliar con
 * A+W). No pinta nada si el registro no viene de un pedido manual
 * clasificado (A+W, Planta Pintura o legacy).
 */
export function ClasePedidoBadge({
  clasificacion,
  className,
}: {
  clasificacion: ClasificacionPedidoLectura;
  className?: string;
}) {
  const texto = etiquetaClasePedido(clasificacion);
  if (texto == null) return null;
  const esEmergencia = texto === 'Emergencia';
  return (
    <Badge
      variant={esEmergencia ? 'warning' : 'info'}
      className={className}
      data-testid="clase-pedido-badge"
      title={
        esEmergencia
          ? 'Capturado con A+W fuera de servicio; pendiente de conciliar con A+W.'
          : 'Venta directa: se factura con la serie de Venta directa y no se informa a A+W.'
      }
    >
      {texto}
    </Badge>
  );
}
