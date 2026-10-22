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
  Undo2,
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

export function TimelineRequisicion({ entradas, isLoading }: TimelineRequisicionProps) {
  if (isLoading) {
    return <p className="text-sm text-ink-muted">Cargando histórico…</p>;
  }

  if (entradas.length === 0) {
    return <p className="text-sm text-ink-muted">Sin transiciones registradas todavía.</p>;
  }

  // El diff de auditoría omite el motivo cuando se repite: se arrastra el
  // último motivo conocido de cada línea (las entradas vienen en orden).
  const motivoPorLinea = new Map<string, string>();
  const motivos = entradas.map((e) => {
    if (e.tipo !== HistoricoTipo.ApartadoLiberado) return undefined;
    const motivo = leerDiff(e.cambios).MotivoUltimaLiberacion?.despues;
    if (typeof motivo === 'string' && e.entidadId) motivoPorLinea.set(e.entidadId, motivo);
    return typeof motivo === 'string' ? motivo : motivoPorLinea.get(e.entidadId ?? '');
  });

  return (
    <ol className="space-y-3" aria-label="Actividad de la requisición">
      {entradas.map((e, idx) => (
        <EntradaTimeline
          key={`${e.timestamp}-${e.tipo}-${e.entidadId ?? 'agg'}-${idx}`}
          entrada={e}
          motivoLiberacion={motivos[idx]}
        />
      ))}
    </ol>
  );
}

interface EntradaTimelineProps {
  entrada: HistoricoEntryResponse;
  motivoLiberacion?: string;
}

function EntradaTimeline({ entrada, motivoLiberacion }: EntradaTimelineProps) {
  const [abierto, setAbierto] = useState(false);
  const config = TIPO_CONFIG[entrada.tipo] ?? FALLBACK_CONFIG;
  const Icon = config.icon;
  const label = HISTORICO_TIPO_LABEL[entrada.tipo] ?? `Tipo ${entrada.tipo}`;
  // Actor resuelto en backend (ADR-0042): sin actor → "Sistema"; no resuelto
  // (service principal / borrado) → indica el dato faltante.
  const actor =
    entrada.actorId == null ? 'Sistema' : entrada.actorNombre?.trim() || 'Sin asignar';
  const cambiosLegible = formatearCambios(entrada.cambios);

  return (
    <li className="flex gap-3 rounded-md border bg-surface-card p-3">
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
          <span className="font-medium">{label}</span> <span className="text-ink-muted">por</span>{' '}
          <span className={cn('font-medium', actor === 'Sin asignar' && 'text-ink-muted')}>{actor}</span>
        </p>
        <p className="text-xs text-ink-muted">
          <DateTimeDisplay value={entrada.timestamp} variant="datetime" />
        </p>
        {entrada.tipo === HistoricoTipo.ApartadoLiberado && (
          <ResumenLiberacion cambios={entrada.cambios} motivo={motivoLiberacion} />
        )}
        {cambiosLegible != null && (
          <button
            type="button"
            onClick={() => setAbierto((v) => !v)}
            aria-expanded={abierto}
            className="mt-1 inline-flex items-center gap-1 text-xs text-ink-muted hover:text-ink"
          >
            {abierto ? <ChevronDown className="h-3 w-3" /> : <ChevronRight className="h-3 w-3" />}
            {abierto ? 'Ocultar cambios' : 'Ver cambios'}
          </button>
        )}
        {abierto && cambiosLegible != null && (
          <pre className="mt-2 max-w-full overflow-x-auto rounded bg-surface-muted/40 p-2 text-2xs leading-relaxed">
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
    bgClass: 'bg-info-bg',
    textClass: 'text-info-fg',
  },
  [HistoricoTipo.Creada]: {
    icon: Sparkles,
    bgClass: 'bg-info-bg',
    textClass: 'text-info-fg',
  },
  [HistoricoTipo.LineaAgregada]: {
    icon: FilePlus,
    bgClass: 'bg-info-bg',
    textClass: 'text-info-fg',
  },
  [HistoricoTipo.LineaActualizada]: {
    icon: FileEdit,
    bgClass: 'bg-info-bg',
    textClass: 'text-info-fg',
  },
  [HistoricoTipo.LineaEliminada]: {
    icon: FileX,
    bgClass: 'bg-danger-bg',
    textClass: 'text-danger-fg',
  },
  [HistoricoTipo.Transmitida]: {
    icon: Send,
    bgClass: 'bg-info-bg',
    textClass: 'text-info-fg',
  },
  [HistoricoTipo.AutorizadaN1]: {
    icon: ShieldCheck,
    bgClass: 'bg-success-bg',
    textClass: 'text-success-fg',
  },
  [HistoricoTipo.AutorizadaN2]: {
    icon: ShieldCheck,
    bgClass: 'bg-success-bg',
    textClass: 'text-success-fg',
  },
  [HistoricoTipo.Rechazada]: {
    icon: PackageX,
    bgClass: 'bg-danger-bg',
    textClass: 'text-danger-fg',
  },
  [HistoricoTipo.Eliminada]: {
    icon: Trash2,
    bgClass: 'bg-danger-bg',
    textClass: 'text-danger-fg',
  },
  [HistoricoTipo.Cancelada]: {
    icon: Ban,
    bgClass: 'bg-warning-bg',
    textClass: 'text-warning-fg',
  },
  [HistoricoTipo.CubrimientoRegistrado]: {
    icon: Inbox,
    bgClass: 'bg-info-bg',
    textClass: 'text-info-fg',
  },
  [HistoricoTipo.RecepcionRegistrada]: {
    icon: PackageCheck,
    bgClass: 'bg-success-bg',
    textClass: 'text-success-fg',
  },
  [HistoricoTipo.SaldoNoSurtido]: {
    icon: CircleDashed,
    bgClass: 'bg-warning-bg',
    textClass: 'text-warning-fg',
  },
  [HistoricoTipo.Cerrada]: {
    icon: CheckCircle2,
    bgClass: 'bg-success-bg',
    textClass: 'text-success-fg',
  },
  [HistoricoTipo.ApartadoLiberado]: {
    icon: Undo2,
    bgClass: 'bg-warning-bg',
    textClass: 'text-warning-fg',
  },
};

const FALLBACK_CONFIG: TipoConfig = {
  icon: CircleDashed,
  bgClass: 'bg-surface-muted',
  textClass: 'text-ink-muted',
};

type Diff = Record<string, { antes?: unknown; despues?: unknown } | undefined>;

function leerDiff(cambios: string): Diff {
  try {
    return (JSON.parse(cambios) as { diff?: Diff }).diff ?? {};
  } catch {
    return {};
  }
}

/** Cantidad, destino y motivo de una liberación de apartado (86e3tferc). */
function ResumenLiberacion({ cambios, motivo }: { cambios: string; motivo?: string }) {
  const diff = leerDiff(cambios);
  const liberada = diff.CantidadLiberadaApartado;
  const cantidad = Number(liberada?.despues ?? 0) - Number(liberada?.antes ?? 0);
  return (
    <p className="mt-1 text-sm text-ink">
      Liberó {cantidad} · {diff.CantidadDeCompra ? 'se manda a compra' : 'no se compra'}
      {motivo && <> · Motivo: {motivo}</>}
    </p>
  );
}

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
