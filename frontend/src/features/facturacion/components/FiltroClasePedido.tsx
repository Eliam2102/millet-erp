import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import type { ClasePedidoManualNombre } from '@/features/facturacion/api/types';
import { OPCIONES_CLASE_PEDIDO } from '@/features/facturacion/lib/glosario';

const SENTINEL_TODAS = '__todas__';

/**
 * Filtro «Clase» (F6.1) de las bandejas de pedidos y de facturas: Todas /
 * Venta directa / Emergencia. El valor viaja como search param `clase` y
 * el backend filtra server-side (`?clase=VentaDirecta|Emergencia`).
 */
export function FiltroClasePedido({
  value,
  onChange,
}: {
  value: ClasePedidoManualNombre | undefined;
  onChange: (v: ClasePedidoManualNombre | undefined) => void;
}) {
  return (
    <div className="space-y-1">
      <label
        htmlFor="filtro-clase-pedido"
        className="text-xs text-muted-foreground"
      >
        Clase
      </label>
      <Select
        value={value ?? SENTINEL_TODAS}
        onValueChange={(v) =>
          onChange(
            v === SENTINEL_TODAS ? undefined : (v as ClasePedidoManualNombre),
          )
        }
      >
        <SelectTrigger
          id="filtro-clase-pedido"
          aria-label="Filtrar por clase"
          className="w-44"
        >
          <SelectValue placeholder="Todas" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value={SENTINEL_TODAS}>Todas</SelectItem>
          {OPCIONES_CLASE_PEDIDO.map((o) => (
            <SelectItem key={o.value} value={o.value}>
              {o.label}
            </SelectItem>
          ))}
        </SelectContent>
      </Select>
    </div>
  );
}
