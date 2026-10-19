import { useState } from 'react';
import { Link, useNavigate } from '@tanstack/react-router';
import { toast } from 'sonner';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { esApiError } from '@/lib/api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PageHeader } from '@/components/erp/PageHeader';
import { mensajeErrorSincronizacion, useEjecucionSync, useReintentarCliente } from '@/modules/datos-maestros/api/clientes';
import { ErroresEjecucionSync } from '@/modules/datos-maestros/components/ErroresEjecucionSync';
import { lecturaAwApagada, MOTIVO_LECTURA_AW_APAGADA, etiquetaAwOrigen, useAwOrigen } from '@/modules/datos-maestros/api/aw-origen';
import {
  fechaCorridaAw,
  leerResumenProductos,
  obtenerReporteDiferenciasAw,
  TIPO_CAMBIO_AW,
  useConflictosAw,
  useIniciarCorridaAw,
} from '@/modules/datos-maestros/api/sincronizacion-aw';
import { EstadoCorridaBadge } from '@/modules/datos-maestros/components/EstadoCorridaBadge';

function Kpi({ etiqueta, valor }: { etiqueta: string; valor: number | string | undefined }) {
  return (
    <div className="flex flex-col gap-1.5 rounded-lg bg-surface-card px-4 py-3.5 shadow-card">
      <span className="text-xs text-ink-muted">{etiqueta}</span>
      <span className="text-3xl font-semibold tabular-nums">{valor ?? 0}</span>
    </div>
  );
}

/** Detalle de una corrida de clientes y productos (ADM-06): contadores, conflictos, reporte de diferencias. */
export function CorridaAwDetalle({ id }: { id: string }) {
  const navigate = useNavigate();
  const puedeProductos = useHasPermission(PermisosCanonicos.DatosMaestrosProductosAwGestionar);
  const ejecucion = useEjecucionSync(id);
  const origen = useAwOrigen(true);
  const clientes = useConflictosAw('clientes', 'Todos', true, id);
  const productos = useConflictosAw('productos-aw', 'Todos', puedeProductos, id);
  const iniciar = useIniciarCorridaAw();
  const reintentar = useReintentarCliente();
  const [descargando, setDescargando] = useState(false);

  const e = ejecucion.data;
  const fecha = fechaCorridaAw(e?.iniciadaEnUtc);
  const resumen = leerResumenProductos(e?.resumenProductos);
  const conflictos = [...(clientes.data ?? []), ...(productos.data ?? [])];

  function volverACorrer() {
    iniciar.mutate(undefined, {
      onSuccess: (r) => { void navigate({ to: '/admin/datos-maestros/sincronizacion-aw/$id', params: { id: r.id } }); },
      onError: (err) => toast.error(mensajeErrorSincronizacion(err)),
    });
  }

  function reintentarReferencia(referencia: string) {
    reintentar.mutate({ referencia }, {
      onSuccess: (r) => (r.estado === 'Completa'
        ? toast.success(`Referencia ${referencia} reprocesada.`)
        : toast.warning(r.errores[0]?.mensaje ?? `La lectura de ${referencia} terminó con estado ${r.estado}.`)),
      onError: (err) => toast.error(mensajeErrorSincronizacion(err)),
    });
  }

  async function descargar() {
    setDescargando(true);
    try {
      const reporte = await obtenerReporteDiferenciasAw(id);
      const { descargarReporteExcel } = await import('@/components/erp/reportes/exporters/reporte-excel-exporter');
      await descargarReporteExcel(reporte, `diferencias-aw-${id.slice(0, 8)}.xlsx`);
      if (reporte.truncado) toast.warning('El reporte se recortó a 10,000 filas.');
    } catch (err) {
      toast.error(esApiError(err) ? (err.problem.detail ?? err.problem.title) : 'No se pudo generar el reporte.');
    } finally {
      setDescargando(false);
    }
  }

  return (
    <div className="space-y-5 text-sm text-ink">
      <nav aria-label="Migas" className="text-xs text-ink-muted">
        Administración / Datos maestros /{' '}
        <Link to="/admin/datos-maestros/sincronizacion-aw" className="text-brand">Sincronización con A+W</Link>
        {` / Corrida del ${fecha}`}
      </nav>

      {ejecucion.isError && <p role="alert" className="text-danger-fg">No se pudo cargar la corrida.</p>}
      {e && (
        <>
          <PageHeader
            titulo={`Corrida de clientes y productos · ${fecha}`}
            estado={<EstadoCorridaBadge estado={e.estado} />}
            descripcion={origen.data ? etiquetaAwOrigen(origen.data, 'clientes') : undefined}
          />

          <div data-print="hidden" className="flex flex-wrap gap-2">
            <Button variant="secondary" disabled={descargando} onClick={() => { void descargar(); }}>
              Descargar reporte de diferencias
            </Button>
            <Button variant="ghost" disabled={iniciar.isPending || !puedeProductos || lecturaAwApagada(origen.data)}
              title={lecturaAwApagada(origen.data) ? MOTIVO_LECTURA_AW_APAGADA
                : puedeProductos ? undefined : 'Necesitas permiso de productos A+W para correr clientes y productos juntos.'}
              onClick={volverACorrer}>
              Volver a correr
            </Button>
          </div>

          <section aria-label="Clientes" className="space-y-2">
            <h2 className="text-lg font-semibold">Clientes</h2>
            <div className="grid grid-cols-4 gap-3">
              <Kpi etiqueta="Leídos" valor={e.leidos} />
              <Kpi etiqueta="Creados + Actualizados" valor={e.creados + e.actualizados} />
              <Kpi etiqueta="Sin cambios" valor={e.sinCambios} />
              <Kpi etiqueta="Errores" valor={e.erroresTruncados ? `${e.errores.length}+` : e.errores.length} />
            </div>
            {e.errores.length > 0 && (
              <ErroresEjecucionSync
                errores={e.errores}
                truncados={e.erroresTruncados}
                reintentando={reintentar.isPending ? reintentar.variables?.referencia : null}
                onReintentar={reintentarReferencia}
              />
            )}
          </section>

          <section aria-label="Productos" className="space-y-2">
            <h2 className="text-lg font-semibold">Productos</h2>
            {resumen?.error ? (
              <p role="alert" className="text-danger-fg">{resumen.mensaje ?? resumen.error}</p>
            ) : resumen == null ? (
              <p className="text-ink-muted">Sin resumen de productos para esta corrida.</p>
            ) : (
              <div className="grid grid-cols-4 gap-3">
                <Kpi etiqueta="Leídos" valor={resumen?.leidos} />
                <Kpi etiqueta="Creados + Actualizados" valor={(resumen?.creados ?? 0) + (resumen?.actualizados ?? 0)} />
                <Kpi etiqueta="Sin cambios" valor={resumen?.sinCambios} />
                <Kpi etiqueta="Errores" valor={resumen?.errores} />
              </div>
            )}
          </section>

          <section aria-label="Conflictos de esta corrida" className="rounded-lg bg-surface-card p-4 shadow-card-flat">
            <h2 className="text-lg font-semibold">Conflictos de esta corrida</h2>
            {conflictos.length === 0 ? (
              <p className="py-3 text-ink-muted">Esta corrida no dejó conflictos.</p>
            ) : (
              <Table>
                <TableHeader>
                  <TableRow><TableHead>Registro</TableHead><TableHead>Tipo</TableHead><TableHead>Causa</TableHead><TableHead>Estado</TableHead></TableRow>
                </TableHeader>
                <TableBody>
                  {conflictos.map((c) => (
                    <TableRow key={c.id}>
                      <TableCell className="font-mono">{c.referenciaExterna}</TableCell>
                      <TableCell><Badge variant={TIPO_CAMBIO_AW[c.tipo].variante}>{TIPO_CAMBIO_AW[c.tipo].texto}</Badge></TableCell>
                      <TableCell>{c.causa}</TableCell>
                      <TableCell>{c.estado === 'Pendiente' ? 'Pendiente' : 'Resuelto'}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
          </section>
        </>
      )}
    </div>
  );
}
