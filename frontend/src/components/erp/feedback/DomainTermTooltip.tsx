import { type ReactNode } from 'react';
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from '@/components/ui/tooltip';

/**
 * <c>&lt;DomainTermTooltip/&gt;</c> — wrapper agnóstico que envuelve un
 * término del dominio (ej. <c>EnSurtido</c>, <c>Naturaleza Crítica</c>,
 * <c>Cubrimiento</c>) con un tooltip que muestra su definición. Doc 05
 * §13.7.
 *
 * <para><b>Decisión cross-module</b>: el componente NO lee el glosario;
 * el caller pasa <c>definicion</c> explícita. El doc 05 sugiere acoplar
 * el componente a <c>features/compras/lib/glosario.ts</c>, pero al
 * dejarlo agnóstico, CxC / OC / CxP / etc. lo consumen pasando su
 * propio diccionario sin tocar este archivo. Cada módulo expone su
 * propio helper <c>obtenerDefinicion(term)</c>.</para>
 *
 * <para>Si <c>definicion</c> es <c>undefined</c>, el componente
 * renderiza solo <c>children</c> sin tooltip — útil para términos que
 * no existen en el glosario sin condicionar el render del caller.</para>
 *
 * @example
 * ```tsx
 * import { obtenerDefinicion } from '@/features/compras/lib/glosario';
 *
 * <DomainTermTooltip
 *   term="EnSurtido"
 *   definicion={obtenerDefinicion('EnSurtido')?.resumen}
 * >
 *   <Badge>EnSurtido</Badge>
 * </DomainTermTooltip>
 * ```
 */
export interface DomainTermTooltipProps {
  /**
   * Término que se está explicando. No se renderiza directamente; sirve
   * como <c>aria-label</c> y como diagnóstico (data attribute) por si
   * en el futuro queremos métricas de "tooltips más consultados".
   */
  term: string;
  /**
   * Definición a mostrar en el tooltip. <c>undefined</c> = sin tooltip
   * (renderiza solo <c>children</c>).
   */
  definicion?: string;
  /** Contenido visible (típicamente un Badge o un span con icono <c>?</c>). */
  children: ReactNode;
  /** Lado preferido del tooltip. Default <c>'top'</c>. */
  side?: 'top' | 'right' | 'bottom' | 'left';
}

export function DomainTermTooltip({
  term,
  definicion,
  children,
  side = 'top',
}: DomainTermTooltipProps) {
  if (definicion == null) {
    return <>{children}</>;
  }

  return (
    <TooltipProvider delayDuration={300}>
      <Tooltip>
        <TooltipTrigger asChild>
          <span data-domain-term={term} className="inline-flex">
            {children}
          </span>
        </TooltipTrigger>
        <TooltipContent side={side} className="max-w-xs text-xs leading-relaxed">
          {definicion}
        </TooltipContent>
      </Tooltip>
    </TooltipProvider>
  );
}
