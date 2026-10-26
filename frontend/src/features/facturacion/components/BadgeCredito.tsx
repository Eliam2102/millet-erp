import { Badge } from '@/components/ui/badge';

/**
 * Badge «Crédito» (86e3tfdw2): marca un pedido cuya condición de pago en el
 * origen no es contado. Tono `warning` (DESIGN.md: «Atención») porque el
 * pedido sigue un flujo distinto al de caja. No renderiza nada si no aplica.
 */
export function BadgeCredito({
  esCredito,
  className,
}: {
  esCredito: boolean | undefined;
  className?: string;
}) {
  if (!esCredito) return null;
  return (
    <Badge variant="warning" className={className}>
      Crédito
    </Badge>
  );
}
