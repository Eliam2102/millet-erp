import { cn } from '@/lib/utils';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';

/**
 * Piezas compartidas de las páginas de detalle read-only de CxP
 * (NC, nota de cargo, viáticos, comprobaciones). Mismo lenguaje
 * visual que <c>FacturaDetallePage</c>: dt/dd compactos, ids en
 * mono y montos con formato de moneda es-MX.
 */

interface CampoProps {
  label: string;
  valor: string;
  mono?: boolean;
}

export function Campo({ label, valor, mono }: CampoProps) {
  return (
    <div>
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className={cn('text-sm', mono && 'break-all font-mono text-xs')}>{valor}</dd>
    </div>
  );
}

interface ImporteProps {
  label: string;
  v: number;
  m: string;
  strong?: boolean;
  /** Cifra de resultado (p. ej. Saldo pendiente): `text-3xl tabular-nums`. */
  grande?: boolean;
}

export function Importe({ label, v, m, strong, grande }: ImporteProps) {
  return (
    <div>
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd
        className={
          grande
            ? 'text-3xl font-semibold tabular-nums'
            : strong
              ? 'text-base font-semibold'
              : 'text-sm'
        }
      >
        <MoneyDisplay amount={v} currency={m} />
      </dd>
    </div>
  );
}
