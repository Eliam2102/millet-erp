import { ResumenImportes } from '@/components/erp/display/ResumenImportes';
import {
  Table,
  TableHeader,
  TableBody,
  TableRow,
  TableCell,
  TableHead,
} from '@/components/ui/table';
import { PageHeader } from '@/components/erp/PageHeader';
import { RegularizacionValeBadge } from '../components/RegularizacionValeBadge';
import { useEffect, useState } from 'react';
import { Link, useParams } from '@tanstack/react-router';
import { ArrowLeft, FileDown, Printer, RotateCw } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { ErrorState, DetalleSkeleton } from '@/components/erp';
import { useSalida } from '@/features/almacen/api/useSalidas';
import { useValeBlob } from '@/features/almacen/api/useValeBlob';
import { type SalidaDetalle } from '@/features/almacen/api/types';
import { esApiError } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { RegularizarValeSheet } from '@/features/almacen/components/RegularizarValeSheet';
import { formatCcMaquinaLabel } from '@/features/centros-costo/lib/cc-maquina-label';
import { ubicacionLabel } from '@/features/almacen/components/impresion/salida-display';
import { EstadoAlmacenBadge } from '@/features/almacen/components/EstadoAlmacenBadge';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { formatCantidad } from '@/lib/format-cantidad';
import { formatDateTime } from '@/lib/datetime';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { useDecimalesUnidad } from '@/components/erp/forms/useDecimalesUnidad';

const FROM = '/_app/almacen/salidas/$id' as const;

/**
 * <c>P4 — Detalle de salida</c> (doc 07 §FE-F3-PR1). Cabecera + líneas.
 * Si la salida es por vale, muestra el botón de regularización (vincular
 * RQ aprobada posterior, A14) cuando aún no se ha regularizado.
 *
 * <para>El comprobante PDF descargable + UI de regularización quedan
 * para iteración posterior (PLATFORM-TODO &lt;ComprobanteSalidaPdf&gt;).</para>
 */
export function SalidaDetallePage() {
  const { id } = useParams({ from: FROM });
  const query = useSalida(id);
  const decimalesUnidad = useDecimalesUnidad();
  const puedePorVale = useHasPermission(PermisosCanonicos.AlmacenSalidasPorVale);
  const [regularizarOpen, setRegularizarOpen] = useState(false);

  return (
    <div className="space-y-5">
      <div className="flex items-center gap-2">
        <Button asChild variant="ghost" size="sm">
          <Link to="/almacen/salidas" search={{}}>
            <ArrowLeft className="mr-2 size-3.5" />
            Salidas
          </Link>
        </Button>
      </div>

      {query.isError ? (
        <ErrorState
          title="No se pudo cargar la salida"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <DetalleSkeleton />
      ) : query.data == null ? null : (
        <div className="space-y-5">
          <PageHeader
            titulo={query.data.folio}
            estado={
              <>
                <EstadoAlmacenBadge tipo="movimiento" estado={query.data.estado} />
                {query.data.esPorVale && <RegularizacionValeBadge salida={query.data} />}
              </>
            }
            descripcion={
              <>
                Fecha de movimiento:{' '}
                <DateTimeDisplay value={query.data.fechaMovimiento} variant="media" />
                {query.data.registradoAt && (
                  <>
                    {' '}
                    · Registrado:{' '}
                    <DateTimeDisplay value={query.data.registradoAt} variant="media" />
                  </>
                )}
              </>
            }
            acciones={
              <>
                <div className="mt-2 flex flex-wrap gap-2">
                  {query.data.esPorVale && !query.data.rqRegularizadoraId && puedePorVale && (
                    <Button onClick={() => setRegularizarOpen(true)}>
                      <RotateCw className="mr-2 size-3.5" />
                      Regularizar vale
                    </Button>
                  )}
                  {query.data.esPorVale && query.data.valeBlobRef && (
                    <VerValeButton salidaId={query.data.id} />
                  )}
                  <ImprimirComprobanteButton salida={query.data} />
                </div>
              </>
            }
            mono
          />

          <dl className="grid grid-cols-1 gap-4 rounded-lg bg-surface-card p-4 shadow-card-flat sm:grid-cols-4">
            <Campo
              label="Sub-almacén"
              valor={
                query.data.subAlmacenNombre
                  ? query.data.subAlmacenClave
                    ? `${query.data.subAlmacenClave} · ${query.data.subAlmacenNombre}`
                    : query.data.subAlmacenNombre
                  : '—'
              }
            />
            <Campo label="Requisición" valor={query.data.rqFolio ?? '—'} mono={query.data.rqFolio != null} />
            <Campo label="Persona destinataria" valor={query.data.personaDestinatariaNombre ?? '—'} />
            {query.data.esPorVale && query.data.valeBlobRef && (
              <Campo label="Vale escaneado" valor={query.data.valeBlobRef} mono />
            )}
            {query.data.esPorVale && query.data.fechaLimiteRegularizacion && (
              <Campo
                label="Límite de regularización"
                valor={formatDateTime(query.data.fechaLimiteRegularizacion)}
              />
            )}
            {query.data.esPorVale && query.data.rqRegularizadoraId && (
              <Campo
                label="RQ regularizadora"
                valor={query.data.rqRegularizadoraFolio ?? '—'}
                mono={query.data.rqRegularizadoraFolio != null}
              />
            )}
            {query.data.observaciones && (
              <div className="md:col-span-2">
                <Campo label="Observaciones" valor={query.data.observaciones} />
              </div>
            )}
          </dl>

          <section className="space-y-2">
            <h2 className="text-sm font-semibold uppercase tracking-wide text-ink-muted">
              Partidas ({query.data.lineas.length})
            </h2>
            <div className="overflow-x-auto rounded-md border">
              <Table className="w-full text-sm">
                <TableHeader className="bg-surface-muted/50">
                  <TableRow>
                    <TableHead className="px-3 py-2 text-left">#</TableHead>
                    <TableHead className="px-3 py-2 text-left">Artículo</TableHead>
                    <TableHead className="px-3 py-2 text-left">Centro de costo</TableHead>
                    <TableHead className="px-3 py-2 text-left">Ubicación</TableHead>
                    <TableHead className="px-3 py-2 text-right">Cantidad</TableHead>
                    <TableHead className="px-3 py-2 text-left">UM</TableHead>
                    <TableHead className="px-3 py-2 text-right">Costo unitario</TableHead>
                    <TableHead className="px-3 py-2 text-right">Monto</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {query.data.lineas.map((l) => (
                    <TableRow key={l.id} className="border-t">
                      <TableCell className="px-3 py-2">{l.posicion}</TableCell>
                      {l.articuloDescripcion ? (
                        <TableCell className="px-3 py-2">
                          {l.articuloClave && (
                            <span className="font-mono text-xs text-ink-muted">
                              {l.articuloClave}
                            </span>
                          )}
                          <div>{l.articuloDescripcion}</div>
                          {l.comentarioLinea && (
                            <div className="text-xs text-ink-muted">{l.comentarioLinea}</div>
                          )}
                        </TableCell>
                      ) : (
                        <TableCell className="px-3 py-2 font-mono text-xs">
                          {l.articuloClave ?? '—'}
                          {l.comentarioLinea && (
                            <div className="font-sans text-xs text-ink-muted">
                              {l.comentarioLinea}
                            </div>
                          )}
                        </TableCell>
                      )}
                      {/* Centro de costo resuelto por el read-port (ADR-0050):
                          "clave — nombre" / "No catalogado" / "—" si sin CC. */}
                      <TableCell className="px-3 py-2">
                        {l.centroCostoId
                          ? formatCcMaquinaLabel({
                              clave: l.centroCostoClave,
                              nombre: l.centroCostoNombre,
                            })
                          : '—'}
                      </TableCell>
                      {/* Ubicación (bin) de donde salió la línea; mismo texto que el comprobante PDF. */}
                      <TableCell className="px-3 py-2 font-mono text-xs">
                        {ubicacionLabel(l)}
                      </TableCell>
                      <TableCell className="py-2 pl-3 pr-4 text-right tabular-nums">
                        {formatCantidad(l.cantidad, decimalesUnidad.porCodigo(l.unidadMedida))}
                      </TableCell>
                      <TableCell className="px-3 py-2">
                        {l.unidadMedida}
                        {l.anchoMm != null && l.altoMm != null && (
                          <span className="block text-xs tabular-nums text-ink-muted">
                            Medida {l.anchoMm} × {l.altoMm} mm
                          </span>
                        )}
                        {l.unidadCapturada && (
                          <span className="block text-xs text-ink-muted">
                            Captura: {l.cantidadCapturada} {l.unidadCapturada}
                          </span>
                        )}
                      </TableCell>
                      <TableCell className="py-2 pl-3 pr-4 text-right tabular-nums">
                        <MoneyDisplay amount={l.costoUnitarioMxn} currency="MXN" strong />
                      </TableCell>
                      <TableCell className="py-2 pl-3 pr-4 text-right tabular-nums">
                        <MoneyDisplay amount={l.montoTotalMxn} currency="MXN" strong />
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
            <ResumenImportes
              moneda="MXN"
              total={query.data.lineas.reduce((total, linea) => total + linea.montoTotalMxn, 0)}
            />
          </section>
        </div>
      )}

      {query.data && (
        <RegularizarValeSheet
          open={regularizarOpen}
          onOpenChange={setRegularizarOpen}
          salidaId={query.data.id}
          folio={query.data.folio}
        />
      )}
    </div>
  );
}

function Campo({ label, valor, mono }: { label: string; valor: string; mono?: boolean }) {
  return (
    <div>
      <dt className="text-xs text-ink-muted">{label}</dt>
      <dd className={mono ? 'font-mono text-xs break-all' : 'text-sm'}>{valor}</dd>
    </div>
  );
}

/**
 * Botón "Ver vale" — descarga el blob authenticated y lo abre en
 * nueva pestaña (browser previsualiza PDF/imagen inline). Revoca el
 * object URL al desmontar para liberar memoria.
 */
function VerValeButton({ salidaId }: { salidaId: string }) {
  const valeQuery = useValeBlob(salidaId);

  useEffect(() => {
    const url = valeQuery.data;
    if (!url) return;
    return () => URL.revokeObjectURL(url);
  }, [valeQuery.data]);

  function abrir() {
    if (valeQuery.data) {
      window.open(valeQuery.data, '_blank', 'noopener,noreferrer');
      return;
    }
    if (valeQuery.isError) {
      toast.error('No se pudo cargar el vale.', {
        description: esApiError(valeQuery.error)
          ? valeQuery.error.problem.detail
          : 'Reintenta en unos segundos.',
      });
      return;
    }
    void valeQuery.refetch();
  }

  return (
    <Button
      type="button"
      variant="outline"
      onClick={abrir}
      disabled={valeQuery.isLoading || valeQuery.isFetching}
    >
      <FileDown className="mr-2 size-3.5" />
      {valeQuery.isLoading || valeQuery.isFetching ? 'Cargando vale…' : 'Ver vale escaneado'}
    </Button>
  );
}

/**
 * Botón "Imprimir comprobante" — genera client-side un PDF nativo del
 * comprobante de salida (ADR-0036) y dispara la descarga. Lazy-load
 * de <c>@react-pdf/renderer</c>.
 */
function ImprimirComprobanteButton({ salida }: { salida: SalidaDetalle }) {
  const [generando, setGenerando] = useState(false);

  async function imprimir() {
    setGenerando(true);
    try {
      const { imprimirComprobanteSalida } =
        await import('@/features/almacen/components/impresion/imprimir-comprobante-salida');
      await imprimirComprobanteSalida({
        salida,
        generadoEn: new Date().toISOString(),
      });
    } catch (e) {
      console.error('Error generando comprobante:', e);
      toast.error('No se pudo generar el comprobante PDF.');
    } finally {
      setGenerando(false);
    }
  }

  return (
    <Button type="button" variant="outline" onClick={imprimir} disabled={generando}>
      <Printer className="mr-2 size-3.5" />
      {generando ? 'Generando PDF…' : 'Imprimir comprobante'}
    </Button>
  );
}
