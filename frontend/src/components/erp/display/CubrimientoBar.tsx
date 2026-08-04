import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from '@/components/ui/tooltip';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;CubrimientoBar/&gt;</c> — barra horizontal segmentada que
 * representa el cubrimiento de una línea de requisición. Doc 05 §11.3
 * + F6 Rev. 3 (WCAG AA + táctil sin hover).
 *
 * <para><b>4 segmentos</b>, cada uno con patrón visual distintivo
 * (distinguible sin depender solo del color):</para>
 * <list>
 *   <item><b>Almacén</b> — verde sólido. Cantidad ya en stock asignada
 *   a esta línea (reserva firme).</item>
 *   <item><b>Pendiente recepción</b> — amarillo con rayas diagonales.
 *   Cantidad ordenada al proveedor que aún no se recibe.</item>
 *   <item><b>Recibido</b> — azul oscuro punteado. Cantidad recibida en
 *   almacén tras la recepción (parcial o total) de la OC.</item>
 *   <item><b>Pendiente compra</b> — gris con cross-hatch. Cantidad
 *   solicitada que aún no tiene OC.</item>
 * </list>
 *
 * <para><b>Números visibles al lado de la barra siempre</b> (F6 Rev. 3
 * R2): "10 total · 3 alm · 5 OC (2 rec) · 2 pend". El dato no depende
 * de hover ni tooltip — el tooltip queda como detalle adicional para
 * desktop con mouse.</para>
 *
 * <para><b>Accesibilidad</b>: <c>role="img"</c> con <c>aria-label</c>
 * que repite la información numérica completa para screen readers.
 * Cada segmento tiene contraste ≥ 4.5:1 (WCAG AA).</para>
 */
export interface CubrimientoBarProps {
  /** Cantidad total solicitada en la línea. */
  cantidad: number;
  /** Asignada de almacén (reserva firme). */
  cantDeAlmacen: number;
  /** Ordenada en OC (incluye recibido y pendiente recepción). */
  cantDeCompra: number;
  /** De la cantDeCompra, cuánto ya se recibió. */
  cantRecibida: number;
  /** Cantidad sin asignar/ordenar todavía (= cantidad - alm - oc). */
  cantPendiente: number;
  className?: string;
}

interface Segmento {
  /** Identificador para keys / data attrs. */
  id: 'almacen' | 'recibida' | 'pendienteRecepcion' | 'pendienteCompra';
  /** Etiqueta corta visible en el tooltip y aria. */
  label: string;
  /** Cantidad en este segmento. */
  valor: number;
  /** % del total (0..100). */
  pct: number;
  /** Clases Tailwind del color de fondo. */
  bgClass: string;
  /** Background-image inline para el patrón distinguible. */
  pattern: string;
}

export function CubrimientoBar({
  cantidad,
  cantDeAlmacen,
  cantDeCompra,
  cantRecibida,
  cantPendiente,
  className,
}: CubrimientoBarProps) {
  // El backend nos da campos brutos; calculamos derivados acá.
  // cantDeCompra incluye la recibida y la pendiente de recepción.
  const cantPendienteRecepcion = Math.max(0, cantDeCompra - cantRecibida);

  // Para evitar divisiones por cero cuando cantidad=0 (raro pero posible).
  const total = Math.max(1, cantidad);
  const pct = (n: number): number => (n / total) * 100;

  const segmentos: Segmento[] = [
    {
      id: 'almacen',
      label: 'almacén',
      valor: cantDeAlmacen,
      pct: pct(cantDeAlmacen),
      bgClass: 'bg-emerald-500',
      pattern: 'none',
    },
    {
      id: 'recibida',
      label: 'recibida',
      valor: cantRecibida,
      pct: pct(cantRecibida),
      bgClass: 'bg-blue-700',
      pattern:
        'radial-gradient(circle, rgba(255,255,255,0.6) 1px, transparent 1.5px)',
    },
    {
      id: 'pendienteRecepcion',
      label: 'pendiente recepción',
      valor: cantPendienteRecepcion,
      pct: pct(cantPendienteRecepcion),
      bgClass: 'bg-amber-400',
      pattern:
        'repeating-linear-gradient(45deg, rgba(0,0,0,0.18) 0 4px, transparent 4px 8px)',
    },
    {
      id: 'pendienteCompra',
      label: 'pendiente compra',
      valor: cantPendiente,
      pct: pct(cantPendiente),
      bgClass: 'bg-slate-300',
      pattern:
        'repeating-linear-gradient(45deg, rgba(0,0,0,0.25) 0 1px, transparent 1px 6px), repeating-linear-gradient(-45deg, rgba(0,0,0,0.25) 0 1px, transparent 1px 6px)',
    },
  ];

  // Etiqueta numérica visible al lado de la barra (no depende de hover).
  // Formato: "10 total · 3 alm · 5 OC (2 rec) · 2 pend"
  const inline = formatearInline({
    cantidad,
    cantDeAlmacen,
    cantDeCompra,
    cantRecibida,
    cantPendiente,
  });

  // Aria-label: misma info pero en frase legible para screen readers.
  const aria = formatearAria({
    cantidad,
    cantDeAlmacen,
    cantPendienteRecepcion,
    cantRecibida,
    cantPendiente,
  });

  // Backgroundsize default para los patrones.
  const patternSize = '6px 6px';

  return (
    <TooltipProvider delayDuration={300}>
      <Tooltip>
        <TooltipTrigger asChild>
          <span
            className={cn(
              'inline-flex items-center gap-2 text-xs',
              className,
            )}
          >
            {/* Barra: role="img" con aria-label numérico completo */}
            <span
              role="img"
              aria-label={aria}
              className="relative inline-flex h-3 w-32 overflow-hidden rounded border border-slate-300 bg-slate-100"
            >
              {segmentos.map((s) =>
                s.pct > 0 ? (
                  <span
                    key={s.id}
                    data-segmento={s.id}
                    className={cn('h-full', s.bgClass)}
                    style={{
                      width: `${s.pct}%`,
                      backgroundImage: s.pattern === 'none' ? undefined : s.pattern,
                      backgroundSize: s.pattern === 'none' ? undefined : patternSize,
                    }}
                  />
                ) : null,
              )}
            </span>

            {/* Números visibles SIEMPRE (no dependen de hover) */}
            <span className="font-mono text-muted-foreground">{inline}</span>
          </span>
        </TooltipTrigger>
        <TooltipContent side="top" className="max-w-xs">
          <DesgloseTooltip
            cantidad={cantidad}
            segmentos={segmentos}
          />
        </TooltipContent>
      </Tooltip>
    </TooltipProvider>
  );
}

function DesgloseTooltip({
  cantidad,
  segmentos,
}: {
  cantidad: number;
  segmentos: Segmento[];
}) {
  return (
    <div className="space-y-1">
      <div className="font-semibold">Cubrimiento — total {fmt(cantidad)}</div>
      <ul className="space-y-0.5">
        {segmentos.map((s) => (
          <li key={s.id} className="flex items-center justify-between gap-3">
            <span className="capitalize">{s.label}</span>
            <span className="font-mono">
              {fmt(s.valor)} ({s.pct.toFixed(0)}%)
            </span>
          </li>
        ))}
      </ul>
    </div>
  );
}

interface InlineArgs {
  cantidad: number;
  cantDeAlmacen: number;
  cantDeCompra: number;
  cantRecibida: number;
  cantPendiente: number;
}

function formatearInline({
  cantidad,
  cantDeAlmacen,
  cantDeCompra,
  cantRecibida,
  cantPendiente,
}: InlineArgs): string {
  const partes: string[] = [`${fmt(cantidad)} total`];
  if (cantDeAlmacen > 0) partes.push(`${fmt(cantDeAlmacen)} alm`);
  if (cantDeCompra > 0) {
    const oc = `${fmt(cantDeCompra)} OC`;
    if (cantRecibida > 0) {
      partes.push(`${oc} (${fmt(cantRecibida)} rec)`);
    } else {
      partes.push(oc);
    }
  }
  if (cantPendiente > 0) partes.push(`${fmt(cantPendiente)} pend`);
  return partes.join(' · ');
}

interface AriaArgs {
  cantidad: number;
  cantDeAlmacen: number;
  cantPendienteRecepcion: number;
  cantRecibida: number;
  cantPendiente: number;
}

function formatearAria({
  cantidad,
  cantDeAlmacen,
  cantPendienteRecepcion,
  cantRecibida,
  cantPendiente,
}: AriaArgs): string {
  const partes: string[] = [`Cubrimiento de ${fmt(cantidad)}`];
  if (cantDeAlmacen > 0)
    partes.push(`${fmt(cantDeAlmacen)} en almacén`);
  if (cantRecibida > 0)
    partes.push(`${fmt(cantRecibida)} recibido`);
  if (cantPendienteRecepcion > 0)
    partes.push(`${fmt(cantPendienteRecepcion)} pendiente de recepción`);
  if (cantPendiente > 0)
    partes.push(`${fmt(cantPendiente)} pendiente de compra`);
  if (cantPendiente === 0 && cantPendienteRecepcion === 0) {
    partes.push('línea cubierta');
  }
  return partes.join(', ') + '.';
}

function fmt(n: number): string {
  return n % 1 === 0 ? String(n) : n.toFixed(2);
}
