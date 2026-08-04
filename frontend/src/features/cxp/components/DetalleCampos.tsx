import { cn } from '@/lib/utils';
import { formatearMonto } from '@/features/cxp/lib/formato';

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
      <dd className={cn('text-sm', mono && 'font-mono text-xs')}>{valor}</dd>
    </div>
  );
}

interface ImporteProps {
  label: string;
  v: number;
  m: string;
  strong?: boolean;
}

export function Importe({ label, v, m, strong }: ImporteProps) {
  return (
    <div>
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd
        className={cn(
          'font-mono',
          strong ? 'text-base font-semibold' : 'text-sm',
        )}
      >
        {formatearMonto(v, m)}
      </dd>
    </div>
  );
}
