import { useState } from 'react';
import { Link } from '@tanstack/react-router';
import { Loader2, Play, Save, TriangleAlert } from 'lucide-react';
import { toast } from 'sonner';
import { ReporteShell } from '@/components/erp/reportes/ReporteShell';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Label } from '@/components/ui/label';
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { esApiError } from '@/lib/api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import {
  CAMPOS_DIFERENCIA_FISCAL,
  useDiferenciasFiscales,
  useEjecutarCorridaDiferencias,
  useMarcarDiferenciaRevisada,
  useUltimaCorridaDiferencias,
  type CorridaDiferenciasFiscales,
  type EstadoDiferenciaFiscal,
  type FilaDiferenciaFiscal,
  type FiltrosDiferenciasFiscales,
  type TipoDiferenciaFiscal,
} from '../api/diferencias-fiscales-aw';

type Vista = 'vivo' | 'corrida';

const SELECT =
  'h-ctl-xl rounded-md border border-line-control bg-surface-card px-3 text-sm text-ink focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand';

function fecha(iso: string) {
  return new Date(iso).toLocaleString('es-MX', { day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' });
}

function mensaje(error: unknown) {
  return esApiError(error) ? (error.problem.detail ?? error.problem.title) : 'No se pudo completar la acción. Reintenta.';
}

/**
 * Reporte de diferencias fiscales A+W vs ERP (U1.8b, F1-FAC-14). El ERP es
 * dueño del dato fiscal: aquí se detecta, se marca revisado o se abre el
 * detalle para corregir en el ERP. Nunca escribe en A+W.
 */
export function DiferenciasFiscalesAwPage() {
  const puedeRevisar = useHasPermission(PermisosCanonicos.DatosMaestrosDiferenciasFiscalesAwRevisar);
  const puedeClientes = useHasPermission(PermisosCanonicos.DatosMaestrosClientesGestionar);
  const puedeProductos = useHasPermission(PermisosCanonicos.DatosMaestrosProductosAwGestionar);

  const [vista, setVista] = useState<Vista>('vivo');
  const [filtros, setFiltros] = useState<FiltrosDiferenciasFiscales>({});
  const [ejecutado, setEjecutado] = useState(false);

  const vivo = useDiferenciasFiscales(filtros, ejecutado && vista === 'vivo');
  const ultima = useUltimaCorridaDiferencias(filtros.incluirRevisadas ?? false);
  const marcar = useMarcarDiferenciaRevisada();
  const corrida = useEjecutarCorridaDiferencias();

  // La corrida guardada trae todo lo pendiente; tipo/campo/estado se filtran aquí.
  const guardado = ultima.data?.reporte;
  const reporteCorrida = guardado && {
    ...guardado,
    filas: guardado.filas.filter(
      (f) =>
        (!filtros.tipo || f.tipo === filtros.tipo) &&
        (!filtros.campo || f.campo === filtros.campo) &&
        (!filtros.estado || f.estado === filtros.estado),
    ),
  };
  const reporte = vista === 'vivo' ? vivo.data : reporteCorrida ?? undefined;
  const cargando = vista === 'vivo' ? vivo.isFetching && !vivo.data : ultima.isLoading;
  const error = vista === 'vivo' ? vivo.error : ultima.error;
  const avisos = reporte?.avisos ?? [];

  function actualizar(parcial: Partial<FiltrosDiferenciasFiscales>) {
    setFiltros((f) => ({ ...f, ...parcial }));
  }

  function ejecutar() {
    setVista('vivo');
    if (ejecutado) void vivo.refetch();
    else setEjecutado(true);
  }

  function guardarCorrida() {
    corrida.mutate(undefined, {
      onSuccess: (c) => {
        toast.success(`Corrida guardada con ${c.diferencias} diferencias pendientes.`);
        setVista('corrida');
      },
      onError: (e) => toast.error(mensaje(e)),
    });
  }

  function marcarRevisada(fila: FilaDiferenciaFiscal) {
    marcar.mutate({ tipo: fila.tipo, entidadId: fila.entidadId, campo: fila.campo, huella: fila.huella }, {
      onSuccess: () =>
        toast.success('Diferencia marcada como revisada', {
          description: 'Volverá a aparecer si cambia el valor en A+W, en la regla o en el ERP.',
        }),
      onError: (e) => toast.error(mensaje(e)),
    });
  }

  const camposVisibles = CAMPOS_DIFERENCIA_FISCAL.filter((c) => !filtros.tipo || c.tipo === filtros.tipo);

  const filtrosUi = (
    <div className="flex flex-wrap items-end gap-3">
      <div className="flex flex-col gap-1.5">
        <Label htmlFor="dif-tipo" className="text-xs font-medium text-ink-strong">Tipo</Label>
        <select
          id="dif-tipo"
          className={SELECT}
          value={filtros.tipo ?? ''}
          onChange={(e) => actualizar({ tipo: (e.target.value || undefined) as TipoDiferenciaFiscal | undefined, campo: undefined })}
        >
          <option value="">Todos</option>
          <option value="Cliente">Clientes</option>
          <option value="Producto">Productos A+W</option>
        </select>
      </div>
      <div className="flex flex-col gap-1.5">
        <Label htmlFor="dif-campo" className="text-xs font-medium text-ink-strong">Campo</Label>
        <select
          id="dif-campo"
          className={SELECT}
          value={filtros.campo ?? ''}
          onChange={(e) => actualizar({ campo: e.target.value || undefined })}
        >
          <option value="">Todos</option>
          {camposVisibles.map((c) => (
            <option key={c.valor} value={c.valor}>{c.etiqueta}</option>
          ))}
        </select>
      </div>
      <div className="flex flex-col gap-1.5">
        <Label htmlFor="dif-estado" className="text-xs font-medium text-ink-strong">Estado</Label>
        <select
          id="dif-estado"
          className={SELECT}
          value={filtros.estado ?? ''}
          onChange={(e) => actualizar({ estado: (e.target.value || undefined) as EstadoDiferenciaFiscal | undefined })}
        >
          <option value="">Todos</option>
          <option value="Diferente">Diferente</option>
          <option value="Faltante">Faltante</option>
          <option value="SinDatoEnOrigen">Sin dato en A+W</option>
        </select>
      </div>
      <div className="flex h-ctl-xl items-center gap-2">
        <Checkbox
          id="dif-revisadas"
          checked={filtros.incluirRevisadas ?? false}
          onCheckedChange={(v) => actualizar({ incluirRevisadas: v === true })}
        />
        <Label htmlFor="dif-revisadas" className="text-sm text-ink">Incluir revisadas</Label>
      </div>
    </div>
  );

  return (
    <div className="flex flex-col gap-4 px-6 py-5 text-sm text-ink">
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-3xl font-semibold tracking-[-0.01em]">Diferencias fiscales A+W</h1>
          <p className="text-ink-muted">
            Clientes y productos cuyo dato fiscal en el ERP no coincide con A+W o con su regla fiscal. Se corrigen en el ERP; A+W no se modifica.
          </p>
        </div>
        <div className="flex gap-2">
          <Button
            variant="secondary"
            onClick={guardarCorrida}
            disabled={!puedeRevisar || corrida.isPending}
            title={puedeRevisar ? undefined : 'Necesitas el permiso para revisar diferencias fiscales'}
          >
            {corrida.isPending ? <Loader2 className="size-3.5 animate-spin" /> : <Save className="size-3.5" strokeWidth={1.8} />}
            Ejecutar y guardar corrida
          </Button>
          <Button onClick={ejecutar} disabled={vivo.isFetching}>
            {vivo.isFetching ? <Loader2 className="size-3.5 animate-spin" /> : <Play className="size-3.5" strokeWidth={1.8} />}
            Ejecutar reporte
          </Button>
        </div>
      </header>

      <UltimaProgramada corrida={ultima.data?.ultimaProgramada ?? null} />

      <Tabs value={vista} onValueChange={(v) => setVista(v as Vista)}>
        <TabsList>
          <TabsTrigger value="vivo">En vivo</TabsTrigger>
          <TabsTrigger value="corrida">Última corrida guardada</TabsTrigger>
        </TabsList>
      </Tabs>

      {vista === 'corrida' && ultima.data?.corrida && (
        <p className="text-xs text-ink-muted">
          Corrida {ultima.data.corrida.origen === 'Programada' ? 'programada' : 'manual'} del {fecha(ultima.data.corrida.iniciadaEn)}
          {ultima.data.corrida.exitosa ? '' : ` · falló: ${ultima.data.corrida.error ?? 'sin detalle'}`}
        </p>
      )}

      {avisos.map((a) => (
        <div key={a} role="note" className="flex items-start gap-2 rounded-md bg-warning-note-bg px-3 py-2.5 text-warning-note-fg">
          <TriangleAlert className="mt-0.5 size-4 shrink-0" strokeWidth={1.6} />
          <span>{a}</span>
        </div>
      ))}

      {reporte?.resumen && (
        <p className="text-xs text-ink-muted tabular-nums">
          {reporte.resumen.clientesComparados} clientes y {reporte.resumen.productosComparados} productos comparados ·{' '}
          {reporte.resumen.pendientes} pendientes · {reporte.resumen.revisadas} revisadas
        </p>
      )}

      {vista === 'corrida' && !ultima.isLoading && !ultima.data?.corrida ? (
        <div className="rounded-lg bg-surface-card px-4 py-6 text-center text-ink-muted shadow-card-flat">
          Aún no hay corridas guardadas. Usa «Ejecutar y guardar corrida» o activa la corrida semanal.
        </div>
      ) : (
        <ReporteShell<FilaDiferenciaFiscal>
          reporte={reporte}
          isLoading={cargando}
          error={esApiError(error) ? error : undefined}
          onRetry={() => void (vista === 'vivo' ? vivo.refetch() : ultima.refetch())}
          filtrosUi={filtrosUi}
          nombreArchivo="diferencias-fiscales-aw"
          etiquetaAccion="Acciones"
          accionFila={(fila) => (
            <div className="flex justify-end gap-2">
              {!fila.revisada && (
                <Button
                  size="sm"
                  variant="secondary"
                  disabled={!puedeRevisar || marcar.isPending}
                  title={puedeRevisar ? undefined : 'Necesitas el permiso para revisar diferencias fiscales'}
                  aria-label={`Marcar revisado ${fila.campoEtiqueta} de ${fila.referencia}`}
                  onClick={() => marcarRevisada(fila)}
                >
                  Marcar revisado
                </Button>
              )}
              <EnlaceCorregir fila={fila} puede={fila.tipo === 'Cliente' ? puedeClientes : puedeProductos} />
            </div>
          )}
        />
      )}
    </div>
  );
}

function EnlaceCorregir({ fila, puede }: { fila: FilaDiferenciaFiscal; puede: boolean }) {
  const etiqueta = `Corregir ${fila.campoEtiqueta} de ${fila.referencia} en el ERP`;
  if (!puede)
    return (
      <span className="text-xs text-ink-muted" title={`Necesitas el permiso para editar ${fila.tipo === 'Cliente' ? 'clientes' : 'productos A+W'}`}>
        Sin acceso para corregir
      </span>
    );
  return fila.tipo === 'Cliente' ? (
    <Link to="/admin/datos-maestros/clientes/$id" params={{ id: fila.entidadId }} aria-label={etiqueta} className="text-sm font-medium text-brand hover:underline">
      Corregir
    </Link>
  ) : (
    <Link to="/admin/datos-maestros/productos-aw/$id" params={{ id: fila.entidadId }} aria-label={etiqueta} className="text-sm font-medium text-brand hover:underline">
      Corregir
    </Link>
  );
}

function UltimaProgramada({ corrida }: { corrida: CorridaDiferenciasFiscales | null }) {
  return (
    <section aria-label="Última corrida programada" className="rounded-lg bg-surface-card px-4 py-3 shadow-card-flat">
      <h2 className="text-xs text-ink-muted">Última corrida programada</h2>
      {corrida ? (
        <p className="tabular-nums">
          {fecha(corrida.iniciadaEn)} ·{' '}
          {corrida.exitosa ? `${corrida.diferencias} diferencias pendientes` : `Falló: ${corrida.error ?? 'sin detalle'}`}
        </p>
      ) : (
        <p className="text-ink-muted">Aún no hay corridas programadas. La corrida semanal se activa en la configuración del servidor.</p>
      )}
    </section>
  );
}
