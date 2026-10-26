import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';

const SENTINEL_TODAS = '__todas__';

/**
 * Filtro «Condición de pago» (86e3tfdw2) de la bandeja de pedidos: Todas /
 * Crédito / Contado. Viaja como search param `credito` y el backend filtra
 * server-side (`?credito=true|false`). Solo se muestra a quien tiene
 * `facturacion.pedidos.leer-credito` (sin el permiso no ve pedidos a crédito).
 */
export function FiltroCondicionPago({
  value,
  onChange,
}: {
  value: boolean | undefined;
  onChange: (v: boolean | undefined) => void;
}) {
  return (
    <div className="space-y-1">
      <label htmlFor="filtro-condicion-pago" className="text-xs text-ink-muted">
        Condición de pago
      </label>
      <Select
        value={value == null ? SENTINEL_TODAS : String(value)}
        onValueChange={(v) => onChange(v === SENTINEL_TODAS ? undefined : v === 'true')}
      >
        <SelectTrigger
          id="filtro-condicion-pago"
          aria-label="Filtrar por condición de pago"
          className="w-44"
        >
          <SelectValue placeholder="Todas" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value={SENTINEL_TODAS}>Todas</SelectItem>
          <SelectItem value="true">Crédito</SelectItem>
          <SelectItem value="false">Contado</SelectItem>
        </SelectContent>
      </Select>
    </div>
  );
}
