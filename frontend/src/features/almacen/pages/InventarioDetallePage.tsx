import { useState } from 'react';
import {
  Link,
  Outlet,
  useMatchRoute,
  useNavigate,
  useParams,
} from '@tanstack/react-router';
import { ArrowLeft, ClipboardCheck, GitCompareArrows, Play, Send } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import { ErrorState, TableSkeleton } from '@/components/erp';
import {
  useConteo,
  useEnviarConteoAConciliacion,
  useIniciarConteo,
} from '@/features/almacen/api/useConteos';
import {
  EstadoConteo,
  EstadoConteoLabels,
  TipoConteo,
  TipoConteoLabels,
} from '@/features/almacen/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { cn } from '@/lib/utils';

const FROM = '/_app/almacen/inventarios/$id' as const;

/**
 * <c>P8 — Detalle de conteo</c> (doc 07 §FE-F5-PR1). Cabecera +
 * acciones según estado:
 *
 * <list type="bullet">
 *   <item><b>Planificado</b>: Iniciar (toma snapshot, pasa a EnCurso).</item>
 *   <item><b>EnCurso</b>: link a pantalla de captura
 *     <c>/almacen/inventarios/$id/captura</c> (sin sesgo, A6) +
 *     "Enviar a conciliación" cuando todas las líneas están capturadas.</item>
 *   <item><b>EnConciliacion</b>: el aprobador trabaja en pantalla
 *     dedicada (llega en FE-F5-PR2).</item>
 *   <item><b>Aprobado / Aplicado / Rechazado</b>: terminal informativo.</item>
 * </list>
 */
export function InventarioDetallePage() {
  const { id } = useParams({ from: FROM });
  const navigate = useNavigate();
  const query = useConteo(id);
  const matchRoute = useMatchRoute();

  const puedeCrear = useHasPermission(
    PermisosCanonicos.AlmacenInventariosCrear,
  );
  const puedeCapturar = useHasPermission(
    PermisosCanonicos.AlmacenInventariosCapturar,
  );
  const puedeAprobar = useHasPermission(
    PermisosCanonicos.AlmacenInventariosAprobarNivel1,
  );

  const iniciar = useIniciarConteo();
  const enviarAConciliacion = useEnviarConteoAConciliacion();

  const [iniciarOpen, setIniciarOpen] = useState(false);
  const [enviarOpen, setEnviarOpen] = useState(false);

  // Las pantallas dedicadas (captura sin sesgo P9 y aprobación P10) son
  // rutas hijas de $id; cuando una está activa, el detalle cede el render
  // completo al Outlet — son full-width por diseño, no paneles anidados.
  const rutaHijaActiva =
    matchRoute({ to: '/almacen/inventarios/$id/captura' }) ||
    matchRoute({ to: '/almacen/inventarios/$id/aprobacion' });
  if (rutaHijaActiva) return <Outlet />;

  function ejecutarIniciar() {
    if (!query.data) return;
    iniciar.mutate(
      { conteoId: query.data.id },
      {
        onSuccess: () => {
          toast.success('Snapshot tomado. Conteo en curso.');
          setIniciarOpen(false);
        },
        onError: manejarError,
      },
    );
  }

  function ejecutarEnviar() {
    if (!query.data) return;
    enviarAConciliacion.mutate(
      { conteoId: query.data.id },
      {
        onSuccess: () => {
          toast.success('Conteo enviado a conciliación');
          setEnviarOpen(false);
        },
        onError: manejarError,
      },
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-2">
        <Button asChild variant="ghost" size="sm">
          <Link to="/almacen/inventarios" search={{}}>
            <ArrowLeft className="mr-2 h-4 w-4" />
            Inventarios
          </Link>
        </Button>
      </div>

      {query.isError ? (
        <ErrorState
          title="No se pudo cargar el conteo"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={4}
          columns={[
            { width: 'w-40' },
            { width: 'w-48' },
            { width: 'w-32' },
            { width: 'w-32' },
            { width: 'w-32' },
          ]}
        />
      ) : query.data == null ? null : (
        <div className="space-y-6">
          <header className="space-y-2">
            <div className="flex flex-wrap items-center gap-3">
              <h1 className="text-2xl font-semibold tracking-tight">
                Conteo {TipoConteoLabels[query.data.tipo]}
              </h1>
              <EstadoBadge estado={query.data.estado} />
            </div>
            <p className="text-sm text-muted-foreground">
              Planificado: {query.data.fechaPlanificada}
              {query.data.fechaInicio && (
                <>
                  {' '}
                  · Iniciado:{' '}
                  {new Date(query.data.fechaInicio).toLocaleString('es-MX')}
                </>
              )}
              {query.data.snapshotCapturadoAt && (
                <>
                  {' '}
                  · Snapshot:{' '}
                  {new Date(
                    query.data.snapshotCapturadoAt,
                  ).toLocaleString('es-MX')}
                </>
              )}
            </p>

            <Acciones
              estado={query.data.estado}
              tipo={query.data.tipo}
              capturadas={query.data.cantidadCapturadas}
              total={query.data.cantidadLineas}
              puedeCrear={puedeCrear}
              puedeCapturar={puedeCapturar}
              puedeAprobar={puedeAprobar}
              onIniciar={() => setIniciarOpen(true)}
              onCapturar={() =>
                navigate({
                  to: '/almacen/inventarios/$id/captura',
                  params: { id: query.data!.id },
                })
              }
              onAprobar={() =>
                navigate({
                  to: '/almacen/inventarios/$id/aprobacion',
                  params: { id: query.data!.id },
                })
              }
              onEnviar={() => setEnviarOpen(true)}
            />
          </header>

          <section className="grid grid-cols-1 gap-x-6 gap-y-2 rounded-md border p-4 md:grid-cols-2">
            <Campo
              label="Sub-almacén"
              valor={
                query.data.subAlmacenId == null
                  ? '(todos)'
                  : (query.data.subAlmacenClave ?? query.data.subAlmacenId)
              }
              mono={query.data.subAlmacenId != null}
            />
            <Campo
              label="Filtro familia"
              valor={query.data.filtroFamilia ?? '(sin filtro)'}
            />
            <Campo
              label="Responsable (contador)"
              valor={query.data.responsableNombre ?? query.data.responsableId}
              mono={query.data.responsableNombre == null}
            />
            {query.data.aprobadorId && (
              <Campo
                label="Aprobador"
                valor={query.data.aprobadorNombre ?? query.data.aprobadorId}
                mono={query.data.aprobadorNombre == null}
              />
            )}
          </section>

          <section className="rounded-md border p-4">
            <h2 className="text-sm font-semibold uppercase tracking-wide text-muted-foreground">
              Progreso
            </h2>
            <p className="mt-2 text-2xl font-mono">
              {query.data.cantidadCapturadas} / {query.data.cantidadLineas}
              <span className="ml-2 text-sm text-muted-foreground">
                líneas capturadas
              </span>
            </p>
          </section>
        </div>
      )}

      <AlertDialog open={iniciarOpen} onOpenChange={setIniciarOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>¿Iniciar conteo?</AlertDialogTitle>
            <AlertDialogDescription>
              Se tomará el snapshot del inventario teórico actual y el conteo
              pasará a <b>En curso</b>. A partir de este momento el contador
              puede capturar.
              {query.data?.tipo === TipoConteo.Anual && (
                <span className="mt-2 block text-amber-700">
                  <b>Conteo anual</b>: las salidas se bloquean durante la
                  captura (A18).
                </span>
              )}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={iniciar.isPending}>
              Cancelar
            </AlertDialogCancel>
            <AlertDialogAction
              disabled={iniciar.isPending}
              onClick={ejecutarIniciar}
            >
              {iniciar.isPending ? 'Iniciando…' : 'Iniciar'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      <AlertDialog open={enviarOpen} onOpenChange={setEnviarOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>¿Enviar a conciliación?</AlertDialogTitle>
            <AlertDialogDescription>
              El conteo pasará a <b>En conciliación</b>. El aprobador
              comparará teórico vs real y decidirá los ajustes. Asegúrate
              de haber capturado todas las líneas.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={enviarAConciliacion.isPending}>
              Cancelar
            </AlertDialogCancel>
            <AlertDialogAction
              disabled={enviarAConciliacion.isPending}
              onClick={ejecutarEnviar}
            >
              {enviarAConciliacion.isPending ? 'Enviando…' : 'Enviar'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}

function Acciones({
  estado,
  tipo,
  capturadas,
  total,
  puedeCrear,
  puedeCapturar,
  puedeAprobar,
  onIniciar,
  onCapturar,
  onAprobar,
  onEnviar,
}: {
  estado: EstadoConteo;
  tipo: TipoConteo;
  capturadas: number;
  total: number;
  puedeCrear: boolean;
  puedeCapturar: boolean;
  puedeAprobar: boolean;
  onIniciar: () => void;
  onCapturar: () => void;
  onAprobar: () => void;
  onEnviar: () => void;
}) {
  void tipo;
  if (estado === EstadoConteo.Planificado && puedeCrear) {
    return (
      <div className="flex gap-2">
        <Button onClick={onIniciar}>
          <Play className="mr-2 h-4 w-4" />
          Iniciar (tomar snapshot)
        </Button>
      </div>
    );
  }
  if (estado === EstadoConteo.EnCurso) {
    const completo = total > 0 && capturadas === total;
    return (
      <div className="flex gap-2">
        {puedeCapturar && (
          <Button onClick={onCapturar} variant="default">
            <ClipboardCheck className="mr-2 h-4 w-4" />
            Capturar (sin sesgo)
          </Button>
        )}
        {puedeCapturar && (
          <Button
            onClick={onEnviar}
            disabled={!completo}
            variant="outline"
            title={
              completo
                ? undefined
                : `Captura faltante: ${capturadas} / ${total}`
            }
          >
            <Send className="mr-2 h-4 w-4" />
            Enviar a conciliación
          </Button>
        )}
      </div>
    );
  }
  if (estado === EstadoConteo.EnConciliacion) {
    if (puedeAprobar) {
      return (
        <div className="flex gap-2">
          <Button onClick={onAprobar}>
            <GitCompareArrows className="mr-2 h-4 w-4" />
            Aprobar / comparar
          </Button>
        </div>
      );
    }
    return (
      <p className="text-sm text-muted-foreground">
        El aprobador está comparando teórico vs real en una pantalla
        dedicada.
      </p>
    );
  }
  if (estado === EstadoConteo.Aprobado && puedeAprobar) {
    return (
      <div className="flex gap-2">
        <Button onClick={onAprobar}>
          <GitCompareArrows className="mr-2 h-4 w-4" />
          Ver / aplicar conteo
        </Button>
      </div>
    );
  }
  return null;
}

function Campo({
  label,
  valor,
  mono,
}: {
  label: string;
  valor: string;
  mono?: boolean;
}) {
  return (
    <div>
      <div className="text-xs uppercase tracking-wide text-muted-foreground">
        {label}
      </div>
      <div className={mono ? 'font-mono text-xs break-all' : 'text-sm'}>
        {valor}
      </div>
    </div>
  );
}

function EstadoBadge({ estado }: { estado: EstadoConteo }) {
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
        estado === EstadoConteo.Planificado && 'bg-slate-200 text-slate-700',
        estado === EstadoConteo.EnCurso && 'bg-blue-100 text-blue-800',
        estado === EstadoConteo.EnConciliacion &&
          'bg-amber-100 text-amber-800',
        estado === EstadoConteo.Aprobado && 'bg-violet-100 text-violet-800',
        estado === EstadoConteo.Aplicado && 'bg-emerald-100 text-emerald-800',
        estado === EstadoConteo.Rechazado && 'bg-rose-100 text-rose-800',
      )}
    >
      {EstadoConteoLabels[estado]}
    </span>
  );
}

function manejarError(error: unknown) {
  if (esApiError(error)) {
    toast.error(error.problem.title, {
      description: error.traceId ? `Código: ${error.traceId}` : undefined,
    });
    return;
  }
  toast.error('Error inesperado.');
}
