import { cn } from '@/lib/utils';
import { DomainTermTooltip } from '@/components/erp/feedback/DomainTermTooltip';
import { obtenerDefinicionOc } from '@/features/compras/lib/glosario';
import {
  SubEstadoFacturacion,
  SubEstadoPago,
  SubEstadoRecepcion,
  subEstadoFacturacionToString,
  subEstadoPagoToString,
  subEstadoRecepcionToString,
} from '@/features/compras/ordenes/api/types';

/**
 * <c>&lt;SubEstadosBar/&gt;</c> — 3 barras horizontales para las 3
 * dimensiones post-autorización de una OC: Recepción, Facturación,
 * Pago (FOC1 cerrado del 05-frontend-diseno §9.1).
 *
 * <para><b>Scope UF1-PR1</b>: una sola sección por barra (OC-level)
 * porque el backend hoy expone los sub-estados materializados a nivel
 * agregado, no a nivel línea
 * (<c>OrdenCompraResponse.subEstadoRecepcion</c>, etc.). Cuando el
 * backend exponga sub-estados por línea (futuro F11+), este componente
 * se extiende con un prop opcional <c>lineas</c> y cada barra se
 * convierte en una serie de segmentos (uno por línea) con el mismo
 * código de colores. La API actual no rompe — el caller solo agrega
 * <c>lineas</c> si quiere granularidad mayor.</para>
 *
 * <para><b>Accesibilidad (doc 05 §12)</b>: cada barra combina <b>3</b>
 * señales visuales para ser robusta a daltonismo y bajo contraste:
 * (a) color de fondo semántico, (b) patrón diagonal cuando el estado
 * es Parcial, (c) etiqueta textual con el label del sub-estado y un
 * porcentaje aproximado. El tooltip extra agrega la definición del
 * glosario.</para>
 *
 * <para><b>Verify WCAG AA contraste</b>: cada par fondo/texto se
 * verificó a mano (≥ 4.5:1) — los colores reusan la familia ya
 * verificada de <c>EstadoBadge</c>: gris para "sin", ámbar para
 * "parcial", verde para "completo". Tabla en el body del PR.</para>
 */
export interface SubEstadosBarProps {
  recepcion: SubEstadoRecepcion;
  facturacion: SubEstadoFacturacion;
  pago: SubEstadoPago;
  /** Override de clases del contenedor exterior. */
  className?: string;
}

interface BarraInfo {
  /** Etiqueta humana de la dimensión (ej. "Recepción"). */
  dimension: string;
  /** Etiqueta del sub-estado actual (ej. "Sin recepción"). */
  label: string;
  /** Clave del término en el glosario para el tooltip. */
  glosarioTerm: string;
  /** Aproximación visual del progreso. */
  progreso: 'vacio' | 'parcial' | 'completo';
  /** Identificador estable para tests/scraping. */
  data: string;
}

const PROGRESO_FILL: Record<BarraInfo['progreso'], string> = {
  // Vacío (Sin*): tracker visible pero sin fill.
  vacio: 'w-0 bg-slate-300',
  // Parcial: 50% de fill ámbar + patrón diagonal.
  parcial:
    'w-1/2 bg-amber-400 [background-image:repeating-linear-gradient(45deg,_rgba(0,0,0,0.12)_0_4px,_transparent_4px_8px)]',
  // Completo (Completa / Pagada): 100% verde.
  completo: 'w-full bg-emerald-500',
};

const PROGRESO_RING: Record<BarraInfo['progreso'], string> = {
  vacio: 'ring-slate-200',
  parcial: 'ring-amber-300',
  completo: 'ring-emerald-300',
};

function progresoRecepcion(s: SubEstadoRecepcion): BarraInfo['progreso'] {
  if (s === SubEstadoRecepcion.Completa) return 'completo';
  if (s === SubEstadoRecepcion.Parcial) return 'parcial';
  return 'vacio';
}
function progresoFacturacion(s: SubEstadoFacturacion): BarraInfo['progreso'] {
  if (s === SubEstadoFacturacion.Completa) return 'completo';
  if (s === SubEstadoFacturacion.Parcial) return 'parcial';
  return 'vacio';
}
function progresoPago(s: SubEstadoPago): BarraInfo['progreso'] {
  if (s === SubEstadoPago.Pagada) return 'completo';
  if (s === SubEstadoPago.Parcial) return 'parcial';
  return 'vacio';
}

function porcentajeAprox(p: BarraInfo['progreso']): string {
  switch (p) {
    case 'vacio':
      return '0 %';
    case 'parcial':
      return '~50 %';
    case 'completo':
      return '100 %';
  }
}

export function SubEstadosBar({
  recepcion,
  facturacion,
  pago,
  className,
}: SubEstadosBarProps) {
  const barras: BarraInfo[] = [
    {
      dimension: 'Recepción',
      label: subEstadoRecepcionToString(recepcion),
      glosarioTerm: 'SubEstado',
      progreso: progresoRecepcion(recepcion),
      data: 'recepcion',
    },
    {
      dimension: 'Facturación',
      label: subEstadoFacturacionToString(facturacion),
      glosarioTerm: 'SubEstado',
      progreso: progresoFacturacion(facturacion),
      data: 'facturacion',
    },
    {
      dimension: 'Pago',
      label: subEstadoPagoToString(pago),
      glosarioTerm: 'SubEstado',
      progreso: progresoPago(pago),
      data: 'pago',
    },
  ];

  const definicion = obtenerDefinicionOc('SubEstado')?.resumen;

  return (
    <div
      className={cn('flex flex-col gap-2', className)}
      role="group"
      aria-label="Sub-estados de la orden de compra (Recepción / Facturación / Pago)"
      data-component="sub-estados-bar"
    >
      {barras.map((b) => (
        <DomainTermTooltip
          key={b.data}
          term={b.glosarioTerm}
          definicion={definicion}
        >
          <div
            className="grid grid-cols-[6.5rem_1fr_8rem] items-center gap-2 text-xs"
            data-sub-estado={b.data}
            data-progreso={b.progreso}
          >
            <span className="font-medium text-foreground">{b.dimension}</span>
            <div
              className={cn(
                'relative h-2.5 overflow-hidden rounded-full bg-slate-100 ring-1 ring-inset',
                PROGRESO_RING[b.progreso],
              )}
              role="progressbar"
              aria-label={`${b.dimension}: ${b.label}`}
              aria-valuemin={0}
              aria-valuemax={100}
              aria-valuenow={
                b.progreso === 'completo'
                  ? 100
                  : b.progreso === 'parcial'
                    ? 50
                    : 0
              }
            >
              <div
                className={cn(
                  'h-full rounded-full transition-[width]',
                  PROGRESO_FILL[b.progreso],
                )}
              />
            </div>
            <span
              className={cn(
                'truncate tabular-nums text-muted-foreground',
                b.progreso === 'completo' && 'text-emerald-700',
                b.progreso === 'parcial' && 'text-amber-800',
              )}
            >
              {b.label} ({porcentajeAprox(b.progreso)})
            </span>
          </div>
        </DomainTermTooltip>
      ))}
    </div>
  );
}
