import { cn } from '@/lib/utils';
import { DomainTermTooltip } from '@/components/erp/feedback/DomainTermTooltip';
import {
  Naturaleza,
  naturalezaToKey,
  naturalezaToString,
} from '@/features/compras/api/types';
import { obtenerDefinicion } from '@/features/compras/lib/glosario';

/**
 * <c>&lt;NaturalezaBadge/&gt;</c> — badge para la naturaleza de un
 * artículo (Estandar / Servicio / Crítico / Riesgo). Doc 05 §11.3.
 *
 * <para>La naturaleza alimenta la matriz de aprobación A1 (§3.bis del
 * 01-diseno). El color refuerza el código sin reemplazarlo —
 * <c>Crítico</c> se ve rojo intenso para alertar al autorizador, pero
 * el label siempre está presente (WCAG 1.4.1: no depender solo de
 * color).</para>
 *
 * <para>El componente recibe un valor numérico de <see cref="Naturaleza"/>
 * (mirror del enum del backend). Si en el futuro
 * <c>LineaResponse</c> incluye la naturaleza embebida (hoy no la
 * trae), el caller la pasa directo; por ahora viene del catálogo de
 * artículos vía <c>useArticulo(articuloId)</c> (UF2-PR1).</para>
 */
export interface NaturalezaBadgeProps {
  naturaleza: Naturaleza;
  className?: string;
}

const COLORES: Record<Naturaleza, string> = {
  // Estandar: neutral.
  [Naturaleza.Estandar]: 'bg-slate-100 text-slate-700 ring-slate-200',
  // Servicio: azul claro (no genera entrada a almacén).
  [Naturaleza.Servicio]: 'bg-sky-100 text-sky-900 ring-sky-200',
  // Crítico: rojo (requiere N2 sin importar monto).
  [Naturaleza.Critico]: 'bg-rose-100 text-rose-900 ring-rose-200',
  // Riesgo: naranja (cumplimiento / control interno).
  [Naturaleza.Riesgo]: 'bg-orange-100 text-orange-900 ring-orange-200',
};

export function NaturalezaBadge({
  naturaleza,
  className,
}: NaturalezaBadgeProps) {
  const label = naturalezaToString(naturaleza);
  const definicion = obtenerDefinicion(naturalezaToKey(naturaleza))?.resumen;

  return (
    <DomainTermTooltip
      term={naturalezaToKey(naturaleza)}
      definicion={definicion}
    >
      <span
        className={cn(
          'inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ring-inset',
          COLORES[naturaleza],
          className,
        )}
        data-naturaleza={naturalezaToKey(naturaleza)}
      >
        {label}
      </span>
    </DomainTermTooltip>
  );
}
