import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';

/**
 * <c>&lt;BadgeSinAsignar/&gt;</c> — badge + toggle del bucket "Sin asignar"
 * de la Capa A (`[Decisión 12-B]`, CAJAS-PR6). Solo se renderiza cuando el
 * backend manda <c>sinAsignarCount</c> (≠ null ⇔ el usuario tiene
 * <c>facturacion.caja.leer-todas</c>): documentos cuya combinación
 * sucursal×canal no mapea a ninguna caja activa.
 */
export function BadgeSinAsignar(props: {
  count: number | null;
  activo: boolean;
  onToggle: () => void;
}) {
  if (props.count == null) return null;
  return (
    <Button
      variant={props.activo ? 'default' : 'outline'}
      size="sm"
      onClick={props.onToggle}
      aria-pressed={props.activo}
    >
      Sin asignar
      <Badge variant="warning" className="ml-2">
        {props.count}
      </Badge>
    </Button>
  );
}
