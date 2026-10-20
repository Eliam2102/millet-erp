import { Badge } from '@/components/ui/badge';
import { VARIACION_UI } from '../lib/estados-ui';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';

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
      <Badge variant={VARIACION_UI.sinCapturar.variant}>{VARIACION_UI.sinCapturar.texto}</Badge>
    );
  }

  const absPorcentaje = Math.abs(variacionPorcentaje ?? 0);
  const absMonto = Math.abs(variacionValorMxn ?? 0);

  if (variacionAbsoluta === 0) {
    return (
      <Badge variant={VARIACION_UI.sinVariacion.variant}>{VARIACION_UI.sinVariacion.texto}</Badge>
    );
  }

  const esGrande = absPorcentaje >= 5 || absMonto >= 1000;

  return (
    <div className="flex flex-col items-start gap-1">
      <Badge variant={esGrande ? VARIACION_UI.mayor.variant : VARIACION_UI.menor.variant}>
        {variacionAbsoluta > 0 ? '+' : ''}
        {variacionAbsoluta.toLocaleString('es-MX', {
          minimumFractionDigits: 2,
          maximumFractionDigits: 4,
        })}{' '}
        ({(variacionPorcentaje ?? 0).toFixed(2)} %)
      </Badge>
      <span className="text-xs text-ink-muted">
        <MoneyDisplay amount={variacionValorMxn ?? 0} currency="MXN" strong />
      </span>
      {requiereRecuento && (
        <Badge variant={VARIACION_UI.recuento.variant}>{VARIACION_UI.recuento.texto}</Badge>
      )}
    </div>
  );
}
