import { useState } from 'react';
import {
  Ban,
  CheckCircle2,
  ChevronDown,
  ChevronRight,
  CircleDashed,
  FileEdit,
  FilePlus,
  FileX,
  Inbox,
  PackageCheck,
  PackageX,
  Pencil,
  Send,
  ShieldCheck,
  Sparkles,
  Trash2,
  type LucideIcon,
} from 'lucide-react';
import { DateTimeDisplay } from '@/components/erp';
import {
  HISTORICO_TIPO_LABEL,
  HistoricoTipo,
  type HistoricoEntryResponse,
} from '@/features/compras/api/types';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;TimelineRequisicion/&gt;</c> — línea de tiempo completa del
 * agregado <c>Requisicion</c>. Reemplaza al
 * <c>&lt;TimelineAutorizaciones/&gt;</c> (que solo mostraba firmas)
 * con TODAS las transiciones del agregado: creación, edición de
 * líneas, transmisión, autorizaciones, rechazos, eliminación,
 * cancelación, cubrimiento, recepciones y cierre.
 *
 * <para>Doc 05 §14.1 — UF7-PR4. Lee del endpoint
 * <c>GET /requisiciones/{id}/historico</c> (vía
 * <c>useHistoricoRequisicion</c>); cada entrada es un
 * <see cref="HistoricoEntryResponse"/>.</para>
 *
 * <para><b>Diseño visual</b>:</para>
 * <list>
 *   <item><b>Avatar circular</b> a la izquierda con icono según
 *   <c>tipo</c> y color (verde=positivo, rojo=destructivo,
 *   azul=neutral, ámbar=advertencia).</item>
 *   <item><b>Body</b>: etiqueta humana + actor (resuelto por el
 *   caller) + timestamp.</item>
 *   <item><b>Detalle expandible</b>: cada entrada tiene un botón
 *   "Ver cambios" que despliega el JSON <c>cambios</c>
 *   pretty-printed. Default cerrado para no saturar.</item>
 * </list>
 *
 * <para>El campo <c>cambios</c> viene del backend como string JSON
 * crudo; este componente intenta <c>JSON.parse</c> y muestra el
 * resultado pretty; si falla, muestra el string crudo (defensivo).</para>
 */
export interface TimelineRequisicionProps {
  entradas: readonly HistoricoEntryResponse[];
  /** Estado para sin-datos: loading explícito (el caller decide
   * mostrar skeleton) y error. Si se omiten, el componente solo
   * cubre el caso de lista vacía. */
  isLoading?: boolean;
}

export function TimelineRequisicion({
  entradas,
  isLoading,
}: TimelineRequisicionProps) {
  if (isLoading) {
    return (
      <p className="text-sm text-muted-foreground">Cargando histórico…</p>
    );
  }

  if (entradas.length === 0) {
    return (
      <p className="text-sm text-muted-foreground">
        Sin transiciones registradas todavía.
      </p>
    );
  }

  return (
    <ol
      className="space-y-3"
      aria-label="Línea de tiempo de la requisición"
    >
      {entradas.map((e, idx) => (
        <EntradaTimeline
          key={`${e.timestamp}-${e.tipo}-${e.entidadId ?? 'agg'}-${idx}`}
          entrada={e}
        />
      ))}
    </ol>
  );
}

interface EntradaTimelineProps {
  entrada: HistoricoEntryResponse;
}

function EntradaTimeline({ entrada }: EntradaTimelineProps) {
  const [abierto, setAbierto] = useState(false);
  const config = TIPO_CONFIG[entrada.tipo] ?? FALLBACK_CONFIG;
  const Icon = config.icon;
  const label = HISTORICO_TIPO_LABEL[entrada.tipo] ?? `Tipo ${entrada.tipo}`;
  // Actor resuelto en backend (ADR-0042): sin actor → "Sistema"; no resuelto
  // (service principal / borrado) → cae al id.
  const actor =
    entrada.actorId == null ? 'Sistema' : entrada.actorNombre ?? entrada.actorId;
  const cambiosLegible = formatearCambios(entrada.cambios);

  return (
    <li className="flex gap-3 rounded-md border bg-card p-3">
      <div
        className={cn(
          'flex h-9 w-9 shrink-0 items-center justify-center rounded-full',
          config.bgClass,
          config.textClass,
        )}
      >
        <Icon className="h-4 w-4" />
      </div>
      <div className="min-w-0 flex-1">
        <p className="text-sm">
          <span className="font-medium">{label}</span>{' '}
          <span className="text-muted-foreground">por</span>{' '}
          <span className="font-medium">{actor}</span>
        </p>
        <p className="text-xs text-muted-foreground">
          <DateTimeDisplay value={entrada.timestamp} variant="datetime" />
        </p>
        {cambiosLegible != null && (
          <button
            type="button"
            onClick={() => setAbierto((v) => !v)}
            aria-expanded={abierto}
            className="mt-1 inline-flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground"
          >
            {abierto ? (
              <ChevronDown className="h-3 w-3" />
            ) : (
              <ChevronRight className="h-3 w-3" />
            )}
            {abierto ? 'Ocultar cambios' : 'Ver cambios'}
          </button>
        )}
        {abierto && cambiosLegible != null && (
          <pre className="mt-2 max-w-full overflow-x-auto rounded bg-muted/40 p-2 text-[11px] leading-relaxed">
            {cambiosLegible}
          </pre>
        )}
      </div>
    </li>
  );
}

interface TipoConfig {
  icon: LucideIcon;
  bgClass: string;
  textClass: string;
}

/**
 * Mapea cada <see cref="HistoricoTipo"/> a icono + colores. La paleta
 * usa verde para positivos (autorizaciones, cierre), rojo para
 * destructivos (rechazo, eliminación, cancelación), azul para
 * neutrales (creación, edición, transmisión), ámbar para advertencias
 * (saldo no surtido).
 */
const TIPO_CONFIG: Partial<Record<HistoricoTipo, TipoConfig>> = {
  [HistoricoTipo.Cambio]: {
    // Edición genérica (cabecera / notas) que no es transición de
    // estado. Lápiz simple, distinto de Sparkles (Creada) y FileEdit
    // (LineaActualizada). Azul-neutral por la convención del archivo.
    icon: Pencil,
    bgClass: 'bg-blue-100',
    textClass: 'text-blue-700',
  },
  [HistoricoTipo.Creada]: {
    icon: Sparkles,
    bgClass: 'bg-blue-100',
    textClass: 'text-blue-700',
  },
  [HistoricoTipo.LineaAgregada]: {
    icon: FilePlus,
    bgClass: 'bg-blue-100',
    textClass: 'text-blue-700',
  },
  [HistoricoTipo.LineaActualizada]: {
    icon: FileEdit,
    bgClass: 'bg-blue-100',
    textClass: 'text-blue-700',
  },
  [HistoricoTipo.LineaEliminada]: {
    icon: FileX,
    bgClass: 'bg-rose-100',
    textClass: 'text-rose-700',
  },
  [HistoricoTipo.Transmitida]: {
    icon: Send,
    bgClass: 'bg-blue-100',
    textClass: 'text-blue-700',
  },
  [HistoricoTipo.AutorizadaN1]: {
    icon: ShieldCheck,
    bgClass: 'bg-emerald-100',
    textClass: 'text-emerald-700',
  },
  [HistoricoTipo.AutorizadaN2]: {
    icon: ShieldCheck,
    bgClass: 'bg-emerald-100',
    textClass: 'text-emerald-700',
  },
  [HistoricoTipo.Rechazada]: {
    icon: PackageX,
    bgClass: 'bg-rose-100',
    textClass: 'text-rose-700',
  },
  [HistoricoTipo.Eliminada]: {
    icon: Trash2,
    bgClass: 'bg-rose-100',
    textClass: 'text-rose-700',
  },
  [HistoricoTipo.Cancelada]: {
    icon: Ban,
    bgClass: 'bg-amber-100',
    textClass: 'text-amber-800',
  },
  [HistoricoTipo.CubrimientoRegistrado]: {
    icon: Inbox,
    bgClass: 'bg-blue-100',
    textClass: 'text-blue-700',
  },
  [HistoricoTipo.RecepcionRegistrada]: {
    icon: PackageCheck,
    bgClass: 'bg-emerald-100',
    textClass: 'text-emerald-700',
  },
  [HistoricoTipo.SaldoNoSurtido]: {
    icon: CircleDashed,
    bgClass: 'bg-amber-100',
    textClass: 'text-amber-800',
  },
  [HistoricoTipo.Cerrada]: {
    icon: CheckCircle2,
    bgClass: 'bg-emerald-100',
    textClass: 'text-emerald-700',
  },
};

const FALLBACK_CONFIG: TipoConfig = {
  icon: CircleDashed,
  bgClass: 'bg-slate-100',
  textClass: 'text-slate-600',
};

/**
 * Intenta parsear el JSON crudo de <c>cambios</c> y devolverlo
 * pretty-printed. Si el parse falla o el JSON está vacío, devuelve
 * <c>null</c> para que el caller no muestre el botón "Ver cambios".
 */
function formatearCambios(cambios: string): string | null {
  if (cambios == null || cambios.trim() === '' || cambios.trim() === '{}') {
    return null;
  }
  try {
    const parsed = JSON.parse(cambios);
    return JSON.stringify(parsed, null, 2);
  } catch {
    return cambios;
  }
}
