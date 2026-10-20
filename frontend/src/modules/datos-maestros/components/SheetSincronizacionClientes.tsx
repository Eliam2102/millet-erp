import { ControlOrigenAw } from '@/modules/datos-maestros/components/ControlOrigenAw';
import { useState } from 'react';
import { ArrowLeft, RefreshCw } from 'lucide-react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  SheetBody,
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import { ErroresEjecucionSync } from '@/modules/datos-maestros/components/ErroresEjecucionSync';
import {
  mensajeErrorSincronizacion,
  useEjecucionesSync,
  useEjecucionSync,
  useIniciarSincronizacion,
  useReintentarCliente,
} from '@/modules/datos-maestros/api';
import { useInvalidarF1AAlTerminar } from '@/modules/datos-maestros/api/use-invalidar-f1a-al-terminar';
import type { EjecucionSyncResumen, EstadoEjecucionSync } from '@/modules/datos-maestros/api/types';
import { esApiError } from '@/lib/api';
import { cn } from '@/lib/utils';

/**
 * Sheet de sincronización de clientes A+W (F1-ADM-06, permiso
 * `sincronizar`; el botón que lo abre ya está gateado). Inicia un
 * barrido (202 → "En cola"), lista las ejecuciones y muestra el detalle
 * con contadores y errores/conflictos por referencia. Parcial/Fallida/
 * Cancelada NUNCA se presentan como éxito.
 */
const ESTADO_UI: Record<EstadoEjecucionSync, [string, string]> = {
  Pendiente: ['En cola', ''],
  EnCurso: ['En curso', 'border-info text-info-fg '],
  Completa: ['Completa', ''],
  Parcial: ['Parcial (con errores)', 'border-warning bg-warning-bg text-warning-fg '],
  Fallida: ['Fallida', 'border-danger/40 bg-danger-bg/10 text-danger-fg'],
  Cancelada: ['Cancelada', 'text-ink-muted'],
};

const SIN_EJECUCIONES: readonly { id: string; estado: EstadoEjecucionSync }[] = [];

export function EstadoEjecucionBadge({ estado }: { estado: EstadoEjecucionSync }) {
  const [texto, tono] = ESTADO_UI[estado] ?? [estado, ''];
  return (
    <Badge variant={estado === 'Completa' ? 'secondary' : 'outline'} className={cn(tono)}>
      {texto}
    </Badge>
  );
}

export function SheetSincronizacionClientes({
  open,
  onOpenChange,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const [detalleId, setDetalleId] = useState<string | null>(null);
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent size="lg">
        <SheetHeader>
          <SheetTitle>Sincronización de clientes A+W</SheetTitle>
          <SheetDescription>
            Lee los clientes de A+W y los aplica al ERP. Los datos fiscales existentes no se
            sobrescriben.
          </SheetDescription>
        </SheetHeader>
        <SheetBody>
          <ControlOrigenAw area="clientes" enabled={open} />
          <div className="mt-4">
            {detalleId == null ? (
              <ListaEjecuciones onAbrir={setDetalleId} />
            ) : (
              <DetalleEjecucion id={detalleId} onVolver={() => setDetalleId(null)} />
            )}
          </div>
        </SheetBody>
      </SheetContent>
    </Sheet>
  );
}

function ErrorConsulta({ error, onRetry }: { error: unknown; onRetry: () => void }) {
  if (esApiError(error) && error.status === 403) {
    return (
      <ErrorState
        title="Sin permiso"
        problem={{
          ...error.problem,
          title: 'Sin permiso',
          detail: 'No tienes permiso para sincronizar clientes.',
        }}
      />
    );
  }
  return <ErrorState problem={esApiError(error) ? error.problem : undefined} onRetry={onRetry} />;
}

function ListaEjecuciones({ onAbrir }: { onAbrir: (id: string) => void }) {
  const query = useEjecucionesSync({ limit: 20 });
  const iniciar = useIniciarSincronizacion();
  useInvalidarF1AAlTerminar(query.data?.items ?? SIN_EJECUCIONES);
  // Ocupado mientras se envía, mientras haya una ejecución viva y en el refresco que sigue al envío.
  const ocupado =
    iniciar.isPending ||
    (query.data?.items.some((e) => e.estado === 'Pendiente' || e.estado === 'EnCurso') ?? false) ||
    (iniciar.isSuccess && query.isFetching);

  function handleIniciar() {
    iniciar.mutate(undefined, {
      onSuccess: (r) =>
        toast.success(
          r.estado === 'Pendiente'
            ? 'Sincronización en cola.'
            : `Sincronización ${ESTADO_UI[r.estado]?.[0] ?? r.estado}.`,
        ),
      onError: (e) => toast.error(mensajeErrorSincronizacion(e)),
    });
  }

  return (
    <div className="space-y-3">
      <Button size="sm" onClick={handleIniciar} disabled={ocupado} aria-busy={ocupado}>
        <RefreshCw className={cn('mr-1.5 h-4 w-4', ocupado && 'animate-spin')} />
        {ocupado ? 'Sincronizando…' : 'Iniciar sincronización'}
      </Button>

      {query.isLoading ? (
        <TableSkeleton rows={4} columns={[{ width: 'w-32' }, { width: 'w-48' }]} />
      ) : query.isError ? (
        <ErrorConsulta error={query.error} onRetry={() => query.refetch()} />
      ) : (query.data?.items.length ?? 0) === 0 ? (
        <EmptyState
          title="Sin ejecuciones."
          description="Inicia la primera sincronización para ver su resultado aquí."
        />
      ) : (
        <ul className="divide-y rounded-md border" aria-label="Ejecuciones">
          {query.data!.items.map((e) => (
            <FilaEjecucion key={e.id} e={e} onAbrir={onAbrir} />
          ))}
        </ul>
      )}
    </div>
  );
}

function FilaEjecucion({ e, onAbrir }: { e: EjecucionSyncResumen; onAbrir: (id: string) => void }) {
  return (
    <li>
      <button
        type="button"
        onClick={() => onAbrir(e.id)}
        className="flex w-full items-center justify-between gap-2 px-3 py-2 text-left text-sm hover:bg-surface-muted/40"
      >
        <span className="flex items-center gap-2">
          <EstadoEjecucionBadge estado={e.estado} />
          <span className="text-ink-muted">{e.tipo}</span>
        </span>
        <span className="text-xs text-ink-muted">
          {e.iniciadaEnUtc ? new Date(e.iniciadaEnUtc).toLocaleString('es-MX') : 'En cola'}
        </span>
      </button>
    </li>
  );
}

function DetalleEjecucion({ id, onVolver }: { id: string; onVolver: () => void }) {
  const query = useEjecucionSync(id);
  const reintentar = useReintentarCliente();
  const d = query.data;
  useInvalidarF1AAlTerminar(d != null ? [d] : SIN_EJECUCIONES);

  // Reproceso por referencia (mismo endpoint y permiso que el detalle del cliente).
  function handleReintentar(referencia: string) {
    reintentar.mutate(
      { referencia },
      {
        onSuccess: (r) =>
          r.estado === 'Completa'
            ? toast.success(`Referencia ${referencia} reprocesada.`)
            : toast.warning(
                r.errores[0]?.mensaje ??
                  `La lectura de ${referencia} terminó con estado ${r.estado}.`,
              ),
        onError: (e) => toast.error(mensajeErrorSincronizacion(e)),
      },
    );
  }
  return (
    <div className="space-y-3">
      <Button type="button" variant="ghost" size="sm" onClick={onVolver}>
        <ArrowLeft className="mr-1.5 h-4 w-4" />
        Ejecuciones
      </Button>

      {query.isLoading ? (
        <TableSkeleton rows={4} columns={[{ width: 'w-32' }, { width: 'w-48' }]} />
      ) : query.isError || d == null ? (
        <ErrorConsulta error={query.error} onRetry={() => query.refetch()} />
      ) : (
        <>
          <div className="flex items-center gap-2">
            <EstadoEjecucionBadge estado={d.estado} />
            {(d.estado === 'Pendiente' || d.estado === 'EnCurso') && (
              <span className="text-xs text-ink-muted">Actualizando…</span>
            )}
            {d.actor != null && <span className="text-xs text-ink-muted">por {d.actor}</span>}
          </div>
          {d.errorGeneral != null && d.errorGeneral !== '' && (
            <p role="alert" className="text-sm text-danger-fg">
              {d.errorGeneral}
            </p>
          )}
          <dl className="grid grid-cols-2 gap-2 text-sm sm:grid-cols-4">
            {(
              [
                ['Leídos', d.leidos],
                ['Creados', d.creados],
                ['Actualizados', d.actualizados],
                ['Sin cambios', d.sinCambios],
                ['Pendientes', d.pendientes],
                ['Conflictos', d.conflictos],
                ['Errores', d.errores.length],
              ] as const
            ).map(([t, v]) => (
              <div key={t} className="rounded-md border p-2">
                <dt className="text-xs text-ink-muted">{t}</dt>
                <dd className="font-mono text-base">{v}</dd>
              </div>
            ))}
          </dl>
          {d.errores.length > 0 && (
            <ErroresEjecucionSync
              errores={d.errores}
              truncados={d.erroresTruncados}
              reintentando={reintentar.isPending ? reintentar.variables?.referencia : null}
              onReintentar={handleReintentar}
            />
          )}
        </>
      )}
    </div>
  );
}
