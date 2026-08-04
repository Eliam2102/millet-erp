import { cn } from '@/lib/utils';

export interface VariacionBadgeProps {
  variacionAbsoluta: number | null;
  variacionPorcentaje: number | null;
  variacionValorMxn: number | null;
  requiereRecuento: boolean;
}

/**
 * <c>&lt;VariacionBadge/&gt;</c> — badge visual que codifica la
 * magnitud de la variación de una línea de conteo. Doc 00 §A7:
 *
 * <list type="bullet">
 *   <item>Sin variación → verde.</item>
 *   <item>Variación &lt; 5% Y &lt; $1K → amarillo (aprobable
 *     individualmente).</item>
 *   <item>Variación ≥ 5% O ≥ $1K → rojo (requiere recuento o
 *     justificación explícita).</item>
 *   <item>Variación > $10K → rojo intenso (umbral de aprobación
 *     elevada, doc §A8).</item>
 * </list>
 */
export function VariacionBadge({
  variacionAbsoluta,
  variacionPorcentaje,
  variacionValorMxn,
  requiereRecuento,
}: VariacionBadgeProps) {
  if (variacionAbsoluta == null) {
    return (
      <span className="inline-flex items-center rounded-full bg-slate-200 px-2 py-0.5 text-xs text-slate-700">
        Sin capturar
      </span>
    );
  }

  const absPorcentaje = Math.abs(variacionPorcentaje ?? 0);
  const absMonto = Math.abs(variacionValorMxn ?? 0);

  if (variacionAbsoluta === 0) {
    return (
      <span className="inline-flex items-center rounded-full bg-emerald-100 px-2 py-0.5 text-xs font-medium text-emerald-800">
        Sin variación
      </span>
    );
  }

  const esGrande = absPorcentaje >= 5 || absMonto >= 1000;
  const esMuyGrande = absMonto >= 10_000;

  return (
    <div className="flex flex-col items-start gap-1">
      <span
        className={cn(
          'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
          esMuyGrande && 'bg-rose-200 text-rose-900',
          esGrande && !esMuyGrande && 'bg-rose-100 text-rose-800',
          !esGrande && 'bg-amber-100 text-amber-800',
        )}
      >
        {variacionAbsoluta > 0 ? '+' : ''}
        {variacionAbsoluta.toLocaleString('es-MX', {
          minimumFractionDigits: 2,
          maximumFractionDigits: 4,
        })}{' '}
        ({(variacionPorcentaje ?? 0).toFixed(2)}%)
      </span>
      <span className="text-xs font-mono text-muted-foreground">
        {formatearMonto(variacionValorMxn ?? 0)}
      </span>
      {requiereRecuento && (
        <span className="inline-flex items-center rounded-full bg-rose-100 px-1.5 py-0.5 text-[10px] font-semibold uppercase text-rose-800">
          Recuento
        </span>
      )}
    </div>
  );
}

function formatearMonto(v: number): string {
  return new Intl.NumberFormat('es-MX', {
    style: 'currency',
    currency: 'MXN',
    minimumFractionDigits: 2,
  }).format(v);
}
