import { PageHeader } from '@/components/erp/PageHeader';
import { RegularizacionValeBadge } from '../components/RegularizacionValeBadge';
import { useEffect, useState } from 'react';
import { Link, useParams } from '@tanstack/react-router';
import { ArrowLeft, FileDown, Printer, RotateCw } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { ErrorState, TableSkeleton } from '@/components/erp';
import { useSalida } from '@/features/almacen/api/useSalidas';
import { useValeBlob } from '@/features/almacen/api/useValeBlob';
import {
  type SalidaDetalle,
} from '@/features/almacen/api/types';
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
  const puedePorVale = useHasPermission(
    PermisosCanonicos.AlmacenSalidasPorVale,
  );
  const [regularizarOpen, setRegularizarOpen] = useState(false);

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-2">
        <Button asChild variant="ghost" size="sm">
          <Link to="/almacen/salidas" search={{}}>
            <ArrowLeft className="mr-2 h-4 w-4" />
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
                Fecha de movimiento: {query.data.fechaMovimiento}
                {query.data.registradoAt && (
                  <>
                    {' '}
                    · Registrado:{' '}
                    <DateTimeDisplay value={query.data.registradoAt} variant="datetime" />
                  </>
                )}
              </>
            }
            acciones={
              <>
                <div className="mt-2 flex flex-wrap gap-2">
                  {query.data.esPorVale &&
                    !query.data.rqRegularizadoraId &&
                    puedePorVale && (
                      <Button onClick={() => setRegularizarOpen(true)}>
                        <RotateCw className="mr-2 h-4 w-4" />
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

          <section className="grid grid-cols-1 gap-x-6 gap-y-2 rounded-md border p-4 md:grid-cols-2">
            <Campo
              label="Sub-almacén"
              valor={
                query.data.subAlmacenNombre
                  ? query.data.subAlmacenClave
                    ? `${query.data.subAlmacenClave} · ${query.data.subAlmacenNombre}`
                    : query.data.subAlmacenNombre
                  : query.data.subAlmacenId
              }
              mono={!query.data.subAlmacenNombre}
            />
            <Campo
              label="Requisición"
              valor={query.data.rqFolio ?? query.data.rqId ?? '—'}
              mono={query.data.rqFolio == null && query.data.rqId != null}
            />
            <Campo
              label="Persona destinataria"
              valor={
                query.data.personaDestinatariaNombre ??
                query.data.personaDestinatariaId ??
                '—'
              }
              mono={
                query.data.personaDestinatariaNombre == null &&
                query.data.personaDestinatariaId != null
              }
            />
            {query.data.esPorVale && query.data.valeBlobRef && (
              <Campo
                label="Vale escaneado"
                valor={query.data.valeBlobRef}
                mono
              />
            )}
            {query.data.esPorVale && query.data.fechaLimiteRegularizacion && (
              <Campo label="Límite de regularización" valor={formatDateTime(query.data.fechaLimiteRegularizacion)} />
            )}
            {query.data.esPorVale && query.data.rqRegularizadoraId && (
              <Campo
                label="RQ regularizadora"
                valor={
                  query.data.rqRegularizadoraFolio ??
                  query.data.rqRegularizadoraId
                }
                mono={query.data.rqRegularizadoraFolio == null}
              />
            )}
            {query.data.observaciones && (
              <div className="md:col-span-2">
                <Campo label="Observaciones" valor={query.data.observaciones} />
              </div>
            )}
          </section>

          <section className="space-y-2">
            <h2 className="text-sm font-semibold uppercase tracking-wide text-muted-foreground">
              Líneas ({query.data.lineas.length})
            </h2>
            <div className="overflow-x-auto rounded-md border">
              <table className="w-full text-sm">
                <thead className="bg-muted/50">
                  <tr>
                    <th className="px-3 py-2 text-left">#</th>
                    <th className="px-3 py-2 text-left">Artículo</th>
                    <th className="px-3 py-2 text-left">Centro de costo</th>
                    <th className="px-3 py-2 text-left">Ubicación</th>
                    <th className="px-3 py-2 text-right">Cantidad</th>
                    <th className="px-3 py-2 text-left">UM</th>
                    <th className="px-3 py-2 text-right">Costo unitario</th>
                    <th className="px-3 py-2 text-right">Monto</th>
                  </tr>
                </thead>
                <tbody>
                  {query.data.lineas.map((l) => (
                    <tr key={l.id} className="border-t">
                      <td className="px-3 py-2">{l.posicion}</td>
                      {l.articuloDescripcion ? (
                        <td className="px-3 py-2">
                          {l.articuloClave && (
                            <span className="font-mono text-xs text-muted-foreground">
                              {l.articuloClave}
                            </span>
                          )}
                          <div>{l.articuloDescripcion}</div>
                          {l.comentarioLinea && (
                            <div className="text-xs text-ink-muted">{l.comentarioLinea}</div>
                          )}
                        </td>
                      ) : (
                        <td className="px-3 py-2 font-mono text-xs">
                          {l.articuloId}
                          {l.comentarioLinea && (
                            <div className="font-sans text-xs text-ink-muted">{l.comentarioLinea}</div>
                          )}
                        </td>
                      )}
                      {/* Centro de costo resuelto por el read-port (ADR-0050):
                          "clave — nombre" / "No catalogado" / "—" si sin CC. */}
                      <td className="px-3 py-2">
                        {l.centroCostoId
                          ? formatCcMaquinaLabel({
                              clave: l.centroCostoClave,
                              nombre: l.centroCostoNombre,
                            })
                          : '—'}
                      </td>
                      {/* Ubicación (bin) de donde salió la línea; mismo texto que el comprobante PDF. */}
                      <td className="px-3 py-2 font-mono text-xs">{ubicacionLabel(l)}</td>
                      <td className="py-2 pl-3 pr-4 text-right tabular-nums">
                        {formatCantidad(l.cantidad, decimalesUnidad.porCodigo(l.unidadMedida))}
                      </td>
                      <td className="px-3 py-2">{l.unidadMedida}{l.unidadCapturada && <span className="block text-xs text-muted-foreground">Captura: {l.cantidadCapturada} {l.unidadCapturada}</span>}</td>
                      <td className="py-2 pl-3 pr-4 text-right tabular-nums">
                        <MoneyDisplay amount={l.costoUnitarioMxn} currency="MXN" strong />
                      </td>
                      <td className="py-2 pl-3 pr-4 text-right tabular-nums">
                        <MoneyDisplay amount={l.montoTotalMxn} currency="MXN" strong />
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
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
      <div className={mono ? 'font-mono text-xs break-all' : 'text-sm'}>{valor}</div>
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
      <FileDown className="mr-2 h-4 w-4" />
      {valeQuery.isLoading || valeQuery.isFetching
        ? 'Cargando vale…'
        : 'Ver vale escaneado'}
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
      const { imprimirComprobanteSalida } = await import(
        '@/features/almacen/components/impresion/imprimir-comprobante-salida'
      );
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
    <Button
      type="button"
      variant="outline"
      onClick={imprimir}
      disabled={generando}
    >
      <Printer className="mr-2 h-4 w-4" />
      {generando ? 'Generando PDF…' : 'Imprimir comprobante'}
    </Button>
  );
}
