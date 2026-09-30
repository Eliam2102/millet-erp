import { useState } from 'react';
import { ArrowLeft, RefreshCw } from 'lucide-react';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { EmptyState, ErrorState, TableSkeleton } from '@/components/erp';
import {
  mensajeErrorSincronizacion,
  useEjecucionesSync,
  useEjecucionSync,
  useIniciarSincronizacion,
  useReintentarCliente,
} from '@/modules/datos-maestros/api';
import type {
  EjecucionSyncResumen,
  EstadoEjecucionSync,
} from '@/modules/datos-maestros/api/types';
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
  EnCurso: ['En curso', 'border-sky-300 text-sky-700 dark:text-sky-300'],
  Completa: ['Completa', ''],
  Parcial: ['Parcial (con errores)', 'border-amber-300 bg-amber-500/10 text-amber-700 dark:text-amber-300'],
  Fallida: ['Fallida', 'border-destructive/40 bg-destructive/10 text-destructive'],
  Cancelada: ['Cancelada', 'text-muted-foreground'],
};

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
      <SheetContent className="w-full overflow-y-auto sm:max-w-2xl">
        <SheetHeader>
          <SheetTitle>Sincronización de clientes A+W</SheetTitle>
          <SheetDescription>
            Lee los clientes de A+W y los aplica al ERP. Los datos fiscales
            existentes no se sobrescriben.
          </SheetDescription>
        </SheetHeader>
        <div className="mt-4">
          {detalleId == null ? (
            <ListaEjecuciones onAbrir={setDetalleId} />
          ) : (
            <DetalleEjecucion id={detalleId} onVolver={() => setDetalleId(null)} />
          )}
        </div>
      </SheetContent>
    </Sheet>
  );
}

function ErrorConsulta({ error, onRetry }: { error: unknown; onRetry: () => void }) {
  if (esApiError(error) && error.status === 403) {
    return (
      <ErrorState
        title="Sin permiso"
        problem={{ ...error.problem, title: 'Sin permiso', detail: 'No tienes permiso para sincronizar clientes.' }}
      />
    );
  }
  return <ErrorState problem={esApiError(error) ? error.problem : undefined} onRetry={onRetry} />;
}

function ListaEjecuciones({ onAbrir }: { onAbrir: (id: string) => void }) {
  const query = useEjecucionesSync({ limit: 20 });
  const iniciar = useIniciarSincronizacion();

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
      <Button size="sm" onClick={handleIniciar} disabled={iniciar.isPending}>
        <RefreshCw className="mr-1.5 h-4 w-4" />
        {iniciar.isPending ? 'Enviando…' : 'Iniciar sincronización'}
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

function FilaEjecucion({
  e,
  onAbrir,
}: {
  e: EjecucionSyncResumen;
  onAbrir: (id: string) => void;
}) {
  return (
    <li>
      <button
        type="button"
        onClick={() => onAbrir(e.id)}
        className="flex w-full items-center justify-between gap-2 px-3 py-2 text-left text-sm hover:bg-muted/40"
      >
        <span className="flex items-center gap-2">
          <EstadoEjecucionBadge estado={e.estado} />
          <span className="text-muted-foreground">{e.tipo}</span>
        </span>
        <span className="text-xs text-muted-foreground">
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

  // Reproceso por referencia (mismo endpoint y permiso que el detalle del cliente).
  function handleReintentar(referencia: string) {
    reintentar.mutate(
      { referencia },
      {
        onSuccess: (r) =>
          r.estado === 'Completa'
            ? toast.success(`Referencia ${referencia} reprocesada.`)
            : toast.warning(`La lectura de ${referencia} terminó con estado ${r.estado}.`),
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
              <span className="text-xs text-muted-foreground">Actualizando…</span>
            )}
            {d.actor != null && (
              <span className="text-xs text-muted-foreground">por {d.actor}</span>
            )}
          </div>
          {d.errorGeneral != null && d.errorGeneral !== '' && (
            <p role="alert" className="text-sm text-destructive">
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
                <dt className="text-xs text-muted-foreground">{t}</dt>
                <dd className="font-mono text-base">{v}</dd>
              </div>
            ))}
          </dl>
          {d.errores.length > 0 && (
            <table className="w-full text-sm">
              <caption className="mb-1 text-left text-xs text-muted-foreground">
                Errores y conflictos por referencia
              </caption>
              <thead>
                <tr className="text-left text-xs text-muted-foreground">
                  <th className="py-1 font-normal">Referencia</th>
                  <th className="py-1 font-normal">Código</th>
                  <th className="py-1 font-normal">Mensaje</th>
                  <th className="py-1 font-normal">
                    <span className="sr-only">Acciones</span>
                  </th>
                </tr>
              </thead>
              <tbody>
                {d.errores.map((er, i) => (
                  <tr key={`${er.referencia}-${i}`} className="border-t align-top">
                    <td className="py-1 font-mono">{er.referencia}</td>
                    <td className="py-1 font-mono text-xs">{er.codigo}</td>
                    <td className="py-1">{er.mensaje}</td>
                    <td className="py-1 text-right">
                      <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        disabled={reintentar.isPending}
                        onClick={() => handleReintentar(er.referencia)}
                        aria-label={`Reintentar referencia ${er.referencia}`}
                      >
                        <RefreshCw className="h-3.5 w-3.5" />
                      </Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </>
      )}
    </div>
  );
}
