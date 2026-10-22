import { KpiCard } from '@/components/erp/display/KpiCard';
import { useState } from 'react';
import { Link, useNavigate } from '@tanstack/react-router';
import { toast } from 'sonner';
import { PaginacionTabla } from '@/components/erp/data/PaginacionTabla';
import { Badge } from '@/components/ui/badge';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { Button } from '@/components/ui/button';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { esApiError } from '@/lib/api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PageHeader } from '@/components/erp/PageHeader';
import {
  mensajeErrorSincronizacion,
  useEjecucionSync,
  useReintentarCliente,
} from '@/modules/datos-maestros/api/clientes';
import { ErroresEjecucionSync } from '@/modules/datos-maestros/components/ErroresEjecucionSync';
import {
  lecturaAwApagada,
  MOTIVO_LECTURA_AW_APAGADA,
  etiquetaAwOrigen,
  useAwOrigen,
} from '@/modules/datos-maestros/api/aw-origen';
import {
  fechaCorridaAw,
  leerResumenProductos,
  obtenerReporteDiferenciasAw,
  TIPO_CAMBIO_AW,
  useConflictosAw,
  useIniciarCorridaAw,
} from '@/modules/datos-maestros/api/sincronizacion-aw';
import { EstadoCorridaBadge } from '@/modules/datos-maestros/components/EstadoCorridaBadge';
import { usePaginacionLocal } from '@/modules/datos-maestros/lib/use-paginacion-local';

/** Detalle de una corrida de clientes y productos (ADM-06): contadores, conflictos, reporte de diferencias. */
export function CorridaAwDetalle({ id }: { id: string }) {
  const navigate = useNavigate();
  const puedeProductos = useHasPermission(PermisosCanonicos.DatosMaestrosProductosAwGestionar);
  const puedeVerOrigen = useHasPermission(PermisosCanonicos.DatosMaestrosClientesOrigenVer);
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
  const paginaConflictos = usePaginacionLocal(conflictos, id);
  // Coincidencia de RFC con un cliente del ERP es conflicto, no error: va en su pestaña.
  const coincidencias = (e?.errores ?? []).filter((x) => x.codigo === 'conflicto_correlacion');
  const erroresFila = (e?.errores ?? []).filter((x) => x.codigo !== 'conflicto_correlacion');

  function volverACorrer() {
    iniciar.mutate(undefined, {
      onSuccess: (r) => {
        void navigate({ to: '/admin/datos-maestros/sincronizacion-aw/$id', params: { id: r.id } });
      },
      onError: (err) => toast.error(mensajeErrorSincronizacion(err)),
    });
  }

  function reintentarReferencia(referencia: string) {
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
        onError: (err) => toast.error(mensajeErrorSincronizacion(err)),
      },
    );
  }

  async function descargar() {
    setDescargando(true);
    try {
      const reporte = await obtenerReporteDiferenciasAw(id);
      const { descargarReporteExcel } =
        await import('@/components/erp/reportes/exporters/reporte-excel-exporter');
      await descargarReporteExcel(reporte, `diferencias-aw-${id.slice(0, 8)}.xlsx`);
      if (reporte.truncado) toast.warning('El reporte se recortó a 10,000 filas.');
    } catch (err) {
      toast.error(
        esApiError(err)
          ? (err.problem.detail ?? err.problem.title)
          : 'No se pudo generar el reporte.',
      );
    } finally {
      setDescargando(false);
    }
  }

  return (
    <div className="space-y-5 text-sm text-ink">
      <nav aria-label="Migas" className="text-xs text-ink-muted">
        Administración / Datos maestros /{' '}
        <Link to="/admin/datos-maestros/sincronizacion-aw" className="text-brand">
          Sincronización con A+W
        </Link>
        {` / Corrida del ${fecha}`}
      </nav>

      {ejecucion.isError && (
        <p role="alert" className="text-danger-fg">
          No se pudo cargar la corrida.
        </p>
      )}
      {e && (
        <>
          <PageHeader
            titulo={`Corrida de clientes y productos · ${fecha}`}
            estado={<EstadoCorridaBadge estado={e.estado} />}
            descripcion={origen.data ? etiquetaAwOrigen(origen.data, 'clientes') : undefined}
          />

          <div data-print="hidden" className="flex flex-wrap gap-2">
            <Button
              variant="secondary"
              disabled={descargando}
              onClick={() => {
                void descargar();
              }}
            >
              Descargar reporte de diferencias
            </Button>
            <Button
              variant="ghost"
              disabled={iniciar.isPending || !puedeProductos || lecturaAwApagada(origen.data)}
              title={
                lecturaAwApagada(origen.data)
                  ? MOTIVO_LECTURA_AW_APAGADA
                  : puedeProductos
                    ? undefined
                    : 'Necesitas permiso de productos A+W para correr clientes y productos juntos.'
              }
              onClick={volverACorrer}
            >
              Volver a correr
            </Button>
          </div>

          <section aria-label="Clientes" className="space-y-2">
            <h2 className="text-lg font-semibold">Clientes</h2>
            <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
              <KpiCard etiqueta="Leídos" valor={e.leidos} />
              <KpiCard etiqueta="Creados + Actualizados" valor={e.creados + e.actualizados} />
              <KpiCard etiqueta="Sin cambios" valor={e.sinCambios} />
              <KpiCard
                etiqueta="Errores"
                valor={e.erroresTruncados ? `${erroresFila.length}+` : erroresFila.length}
              />
            </div>
          </section>

          <section aria-label="Productos" className="space-y-2">
            <h2 className="text-lg font-semibold">Productos</h2>
            {resumen?.error ? (
              <p role="alert" className="text-danger-fg">
                {resumen.mensaje ?? resumen.error}
              </p>
            ) : resumen == null ? (
              <p className="text-ink-muted">Sin resumen de productos para esta corrida.</p>
            ) : (
              <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
                <KpiCard etiqueta="Leídos" valor={resumen?.leidos} />
                <KpiCard
                  etiqueta="Creados + Actualizados"
                  valor={(resumen?.creados ?? 0) + (resumen?.actualizados ?? 0)}
                />
                <KpiCard etiqueta="Sin cambios" valor={resumen?.sinCambios} />
                <KpiCard etiqueta="Errores" valor={resumen?.errores} />
              </div>
            )}
          </section>

          <Tabs
            key={id}
            defaultValue={erroresFila.length > 0 ? 'errores' : 'conflictos'}
            className="rounded-lg bg-surface-card p-4 shadow-card-flat"
          >
            <TabsList aria-label="Errores y conflictos de la corrida">
              <TabsTrigger value="errores">
                Errores ({erroresFila.length}
                {e.erroresTruncados ? '+' : ''})
              </TabsTrigger>
              <TabsTrigger value="conflictos">
                Conflictos ({conflictos.length + coincidencias.length})
              </TabsTrigger>
            </TabsList>
            <TabsContent value="errores" className="pt-3">
              {erroresFila.length === 0 ? (
                <p className="py-3 text-ink-muted">Esta corrida no dejó errores.</p>
              ) : (
                <ErroresEjecucionSync
                  titulo="Errores por referencia"
                  errores={erroresFila}
                  truncados={e.erroresTruncados}
                  reintentando={reintentar.isPending ? reintentar.variables?.referencia : null}
                  onReintentar={reintentarReferencia}
                  origen={puedeVerOrigen ? { fecha } : undefined}
                />
              )}
            </TabsContent>
            <TabsContent value="conflictos" className="space-y-4 pt-3">
              {conflictos.length === 0 && coincidencias.length === 0 ? (
                <p className="py-3 text-ink-muted">Esta corrida no dejó conflictos.</p>
              ) : (
                conflictos.length > 0 && (
                  <div>
                    <Table>
                      <TableHeader>
                        <TableRow>
                          <TableHead>Registro</TableHead>
                          <TableHead>Tipo</TableHead>
                          <TableHead>Causa</TableHead>
                          <TableHead>Estado</TableHead>
                        </TableRow>
                      </TableHeader>
                      <TableBody>
                        {paginaConflictos.visibles.map((c) => (
                          <TableRow key={c.id}>
                            <TableCell className="font-mono">{c.referenciaExterna}</TableCell>
                            <TableCell>
                              <Badge variant={TIPO_CAMBIO_AW[c.tipo].variante}>
                                {TIPO_CAMBIO_AW[c.tipo].texto}
                              </Badge>
                            </TableCell>
                            <TableCell>{c.causa}</TableCell>
                            <TableCell>{c.estado}</TableCell>
                          </TableRow>
                        ))}
                      </TableBody>
                    </Table>
                    {conflictos.length > 10 && (
                      <PaginacionTabla
                        total={conflictos.length}
                        offset={paginaConflictos.offset}
                        limit={10}
                        tamanos={[]}
                        onChange={paginaConflictos.onChange}
                      />
                    )}
                  </div>
                )
              )}
              {coincidencias.length > 0 && (
                <ErroresEjecucionSync
                  titulo="Coincidencias con clientes del ERP"
                  errores={coincidencias}
                  truncados={e.erroresTruncados}
                  reintentando={reintentar.isPending ? reintentar.variables?.referencia : null}
                  onReintentar={reintentarReferencia}
                />
              )}
            </TabsContent>
          </Tabs>
        </>
      )}
    </div>
  );
}
