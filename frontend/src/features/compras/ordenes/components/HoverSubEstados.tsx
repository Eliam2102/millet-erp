import {
  SubEstadoFacturacion,
  SubEstadoPago,
  SubEstadoRecepcion,
  subEstadoRecepcionToString,
  subEstadoFacturacionToString,
  subEstadoPagoToString,
} from '@/features/compras/ordenes/api/types';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;HoverSubEstados/&gt;</c> — chip compacto con los 3
 * sub-estados (R/F/P) + tooltip detallado al hover (UF6-PR1, mejora
 * visual de bandeja). Promueve el patrón inline que existía en
 * <c>BandejaOrdenesCompra</c> a un componente reusable cross-vista
 * (también lo consumirá <c>PartidasAbiertas</c> en UF7-PR1).
 *
 * <para>Layout: 3 chips redondos con la letra (R/F/P) coloreados según
 * progreso. <b>Un solo tooltip combinado</b> sobre el wrapper (multi-
 * línea con los 3 detalles) en lugar de 3 tooltips individuales —
 * más rápido de consumir al hover sobre una bandeja densa.</para>
 *
 * <para>A11y: el wrapper tiene <c>role="img"</c> + <c>aria-label</c>
 * con el tooltip plain text para que screen readers lean los 3 estados
 * de una sola vez sin tener que enfocar cada chip.</para>
 */
export interface HoverSubEstadosProps {
  recepcion: SubEstadoRecepcion;
  facturacion: SubEstadoFacturacion;
  pago: SubEstadoPago;
  className?: string;
}

type Progreso = 'vacio' | 'parcial' | 'completo';

const DOT_BG: Record<Progreso, string> = {
  vacio: 'bg-slate-200 text-slate-600 ring-slate-300',
  parcial: 'bg-amber-200 text-amber-900 ring-amber-300',
  completo: 'bg-emerald-200 text-emerald-900 ring-emerald-300',
};

export function HoverSubEstados({
  recepcion,
  facturacion,
  pago,
  className,
}: HoverSubEstadosProps) {
  const tooltip = [
    `Recepción: ${subEstadoRecepcionToString(recepcion)}`,
    `Facturación: ${subEstadoFacturacionToString(facturacion)}`,
    `Pago: ${subEstadoPagoToString(pago)}`,
  ].join('\n');

  return (
    <div
      className={cn('inline-flex items-center gap-1', className)}
      title={tooltip}
      role="img"
      aria-label={tooltip}
      data-component="hover-sub-estados"
    >
      <Chip letra="R" progreso={progresoRecepcion(recepcion)} kind="recepcion" />
      <Chip
        letra="F"
        progreso={progresoFacturacion(facturacion)}
        kind="facturacion"
      />
      <Chip letra="P" progreso={progresoPago(pago)} kind="pago" />
    </div>
  );
}

function Chip({
  letra,
  progreso,
  kind,
}: {
  letra: 'R' | 'F' | 'P';
  progreso: Progreso;
  kind: 'recepcion' | 'facturacion' | 'pago';
}) {
  return (
    <span
      data-sub-estado={kind}
      data-progreso={progreso}
      className={cn(
        'inline-flex h-5 w-5 items-center justify-center rounded-full text-[0.65rem] font-semibold ring-1 ring-inset',
        DOT_BG[progreso],
      )}
    >
      {letra}
    </span>
  );
}

function progresoRecepcion(s: SubEstadoRecepcion): Progreso {
  if (s === SubEstadoRecepcion.Completa) return 'completo';
  if (s === SubEstadoRecepcion.Parcial) return 'parcial';
  return 'vacio';
}
function progresoFacturacion(s: SubEstadoFacturacion): Progreso {
  if (s === SubEstadoFacturacion.Completa) return 'completo';
  if (s === SubEstadoFacturacion.Parcial) return 'parcial';
  return 'vacio';
}
function progresoPago(s: SubEstadoPago): Progreso {
  if (s === SubEstadoPago.Pagada) return 'completo';
  if (s === SubEstadoPago.Parcial) return 'parcial';
  return 'vacio';
}
