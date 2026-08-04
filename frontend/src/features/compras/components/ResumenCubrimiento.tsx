import { cn } from '@/lib/utils';
import type { LineaResponse } from '@/features/compras/api/types';

/**
 * <c>&lt;ResumenCubrimiento/&gt;</c> — texto agregado en la cabecera de
 * P3 que muestra el % de líneas completamente cubiertas
 * (<c>cantPendiente=0</c>) sobre el total. Doc 05 §11.3 + F6 Rev. 3.
 *
 * <para>Ejemplo: "Cubrimiento global: 60% (3 de 5 líneas cerradas)".
 * Cuando todas las líneas están cubiertas, el badge se torna verde.
 * Cuando ninguna lo está, se torna ámbar discreto.</para>
 *
 * <para>Solo se muestra para RQs en estados con cubrimiento real
 * (<c>Autorizada</c>/<c>EnSurtido</c>/<c>Cerrada</c>). En estados
 * pre-aut o terminales sin surtido (<c>Rechazada</c>,
 * <c>Cancelada</c>, <c>Eliminada</c>) el componente devuelve
 * <c>null</c> — el caller no necesita gatear.</para>
 */
export interface ResumenCubrimientoProps {
  lineas: readonly LineaResponse[];
  /** Estado actual de la RQ — para decidir si mostrar el resumen. */
  estado: number;
  className?: string;
}

// Estados que tienen cubrimiento informativo. Usamos números literales
// para no acoplar al enum (evita ciclo de imports en algunos paths).
// 2=Autorizada, 3=EnSurtido, 4=Cerrada (ver EstadoRequisicion).
const ESTADOS_CON_CUBRIMIENTO = new Set<number>([2, 3, 4]);

export function ResumenCubrimiento({
  lineas,
  estado,
  className,
}: ResumenCubrimientoProps) {
  if (!ESTADOS_CON_CUBRIMIENTO.has(estado)) return null;
  if (lineas.length === 0) return null;

  const total = lineas.length;
  const cerradas = lineas.filter((l) => l.cantPendiente <= 0).length;
  const pct = Math.round((cerradas / total) * 100);

  const completo = cerradas === total;
  const sinCobertura = cerradas === 0;

  return (
    <div
      className={cn(
        'inline-flex items-center gap-2 rounded-md border px-3 py-1.5 text-sm',
        completo && 'border-emerald-300 bg-emerald-50 text-emerald-900',
        sinCobertura && 'border-amber-300 bg-amber-50 text-amber-900',
        !completo && !sinCobertura && 'border-slate-300 bg-slate-50 text-slate-900',
        className,
      )}
      role="status"
      aria-label={`Cubrimiento global: ${pct}% — ${cerradas} de ${total} líneas cerradas`}
    >
      <span className="font-semibold">Cubrimiento global:</span>
      <span className="font-mono">{pct}%</span>
      <span className="text-muted-foreground">
        ({cerradas} de {total} {total === 1 ? 'línea cerrada' : 'líneas cerradas'})
      </span>
    </div>
  );
}
