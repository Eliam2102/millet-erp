import { useMemo } from 'react';
import { Plus, Pencil, Trash2, Send, CheckCircle2, XCircle, Ban, Copy, Paperclip, FileText, AlertTriangle } from 'lucide-react';
import { ErrorState, TableSkeleton, EmptyState } from '@/components/erp';
import { useHistoricoOrdenCompra } from '@/features/compras/ordenes/api/useHistoricoOrdenCompra';
import { useUsuarios, mapById } from '@/features/catalogos/api';
import { esApiError } from '@/lib/api';
import { cn } from '@/lib/utils';

/**
 * <c>&lt;TimelineOrdenCompra/&gt;</c> — timeline cronológico completo
 * de una OC (UF7-PR3). Reemplaza el stub del Tab "Historial" con la
 * lista real de eventos del audit_log.
 *
 * <para>Cada evento se renderea con ícono según operación + entidad,
 * timestamp, usuario resuelto contra el catálogo, y resumen humano.
 * Layout vertical con línea conectora a la izquierda (timeline
 * estándar). Eventos más viejos arriba (orden ASC del backend).</para>
 *
 * <para>NO hace agrupación por día/sesión — la cantidad típica de
 * eventos por OC es baja (decenas, no cientos) y la simplicidad
 * lineal es más útil. Si emerge necesidad, agregar separadores de
 * fecha en versión futura.</para>
 */
export interface TimelineOrdenCompraProps {
  ocId: string;
  /**
   * Si se especifica, sólo se muestran eventos cuya <c>entidad</c>
   * coincida exactamente. Útil para reusar el componente en tabs
   * filtrados (ej. Tab "Autorización" → <c>filtroEntidad="AutorizacionOC"</c>).
   */
  filtroEntidad?: string;
  emptyTitle?: string;
  emptyDescription?: string;
}

export function TimelineOrdenCompra({
  ocId,
  filtroEntidad,
  emptyTitle = 'Sin eventos en el historial',
  emptyDescription = 'Cuando esta OC tenga modificaciones, autorizaciones, recepciones, facturas o pagos, los verás aquí en orden cronológico.',
}: TimelineOrdenCompraProps) {
  const query = useHistoricoOrdenCompra(ocId);
  const usuariosQuery = useUsuarios();
  const usuariosMap = useMemo(
    () => mapById(usuariosQuery.data?.items),
    [usuariosQuery.data],
  );

  if (query.isLoading) {
    return <TableSkeleton rows={5} />;
  }

  if (query.isError) {
    return (
      <ErrorState
        title="No se pudo cargar el historial"
        problem={esApiError(query.error) ? query.error.problem : undefined}
        onRetry={() => {
          void query.refetch();
        }}
      />
    );
  }

  const todosEventos = query.data?.eventos ?? [];
  const eventos = filtroEntidad
    ? todosEventos.filter((e) => e.entidad === filtroEntidad)
    : todosEventos;

  if (eventos.length === 0) {
    return <EmptyState title={emptyTitle} description={emptyDescription} />;
  }

  return (
    <ol
      className="relative space-y-3 border-l border-muted pl-4"
      data-component="timeline-orden-compra"
      aria-label="Historial cronológico de la OC"
    >
      {eventos.map((evento) => {
        const usuario =
          evento.usuarioId && usuariosMap.get(evento.usuarioId)?.nombre;
        const { Icon, color } = resolverEventoVisual(evento.operacion, evento.entidad);
        return (
          <li
            key={evento.id}
            className="relative"
            data-evento={evento.id}
            data-operacion={evento.operacion}
            data-entidad={evento.entidad}
          >
            {/* Punto del timeline */}
            <span
              className={cn(
                'absolute -left-[1.4rem] flex h-5 w-5 items-center justify-center rounded-full ring-2 ring-background',
                color,
              )}
              aria-hidden="true"
            >
              <Icon className="h-3 w-3 text-white" />
            </span>

            {/* Contenido */}
            <div className="rounded-md border bg-card p-3">
              <div className="flex flex-wrap items-baseline justify-between gap-2 text-sm">
                <span className="font-medium">
                  {labelOperacion(evento.operacion)}{' '}
                  <span className="text-muted-foreground">
                    en {evento.entidad}
                  </span>
                </span>
                <time
                  className="text-xs text-muted-foreground tabular-nums"
                  dateTime={evento.timestamp}
                >
                  {new Date(evento.timestamp).toLocaleString()}
                </time>
              </div>
              {evento.resumen && (
                <p className="mt-1 text-xs text-muted-foreground line-clamp-3">
                  {evento.resumen}
                </p>
              )}
              {usuario && (
                <p className="mt-1 text-xs">
                  Por: <span className="font-medium">{usuario}</span>
                </p>
              )}
            </div>
          </li>
        );
      })}
    </ol>
  );
}

// ─── Helpers visuales ─────────────────────────────────────────────

function labelOperacion(op: string): string {
  switch (op) {
    case 'crear':
      return 'Creado';
    case 'actualizar':
      return 'Actualizado';
    case 'borrar':
      return 'Eliminado';
    default:
      return op;
  }
}

function resolverEventoVisual(operacion: string, entidad: string) {
  // Mapping heurístico operación + entidad → ícono + color. Se afina
  // cuando el backend emita eventos más específicos (transmitir,
  // autorizar, etc.) en lugar del CRUD genérico de audit_log.
  if (entidad === 'AutorizacionOC' || entidad.toLowerCase().includes('autoriz')) {
    return operacion === 'crear'
      ? { Icon: CheckCircle2, color: 'bg-emerald-600' }
      : { Icon: XCircle, color: 'bg-rose-600' };
  }
  if (entidad === 'AdjuntoOC' || entidad.toLowerCase().includes('adjunto')) {
    return { Icon: Paperclip, color: 'bg-slate-500' };
  }
  if (entidad === 'OrdenCompraPdf') {
    return { Icon: FileText, color: 'bg-indigo-600' };
  }
  if (entidad === 'LineaOrdenCompra') {
    if (operacion === 'crear') return { Icon: Plus, color: 'bg-emerald-600' };
    if (operacion === 'borrar') return { Icon: Trash2, color: 'bg-rose-600' };
    return { Icon: Pencil, color: 'bg-amber-600' };
  }
  if (entidad === 'OrdenCompra') {
    if (operacion === 'crear')
      return { Icon: Plus, color: 'bg-emerald-600' };
    if (operacion === 'borrar')
      return { Icon: Ban, color: 'bg-rose-600' };
    // Update genérico — sin más contexto del backend; el resumen ayuda.
    return { Icon: Pencil, color: 'bg-amber-600' };
  }
  // Default
  if (operacion === 'crear') return { Icon: Plus, color: 'bg-emerald-600' };
  if (operacion === 'borrar') return { Icon: Trash2, color: 'bg-rose-600' };
  return { Icon: Pencil, color: 'bg-slate-500' };
}

// Imports de íconos no usados en el switch principal pero los exporto
// implícitamente para que el helper resolverEventoVisual los tenga
// disponibles (si se extiende con más entidades).
void Send;
void Copy;
void AlertTriangle;
