import { useEffect, useState } from 'react';
import { Link, useParams } from '@tanstack/react-router';
import { ArrowLeft, Check, FileDown, Printer, RotateCw } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { ErrorState, TableSkeleton } from '@/components/erp';
import { useSalida } from '@/features/almacen/api/useSalidas';
import { useValeBlob } from '@/features/almacen/api/useValeBlob';
import {
  EstadoMovimiento,
  EstadoMovimientoLabels,
  type SalidaDetalle,
} from '@/features/almacen/api/types';
import { esApiError } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { RegularizarValeSheet } from '@/features/almacen/components/RegularizarValeSheet';
import { formatCcMaquinaLabel } from '@/features/centros-costo/lib/cc-maquina-label';
import { cn } from '@/lib/utils';

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
          <header className="space-y-1">
            <div className="flex flex-wrap items-center gap-3">
              <h1 className="text-2xl font-semibold tracking-tight font-mono">
                {query.data.folio}
              </h1>
              <EstadoMovimientoBadge estado={query.data.estado} />
              {query.data.esPorVale && query.data.rqRegularizadoraId && (
                <span className="inline-flex items-center gap-1 rounded-full bg-emerald-100 px-2 py-0.5 text-xs font-medium text-emerald-800">
                  <Check className="h-3 w-3" />
                  Vale regularizado
                </span>
              )}
              {query.data.esPorVale && !query.data.rqRegularizadoraId && (
                <span className="inline-flex items-center rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-800">
                  Vale pendiente de regularizar
                </span>
              )}
            </div>
            <p className="text-sm text-muted-foreground">
              Fecha de movimiento: {query.data.fechaMovimiento}
              {query.data.registradoAt && (
                <>
                  {' '}
                  · Registrado:{' '}
                  {new Date(query.data.registradoAt).toLocaleString('es-MX')}
                </>
              )}
            </p>
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
          </header>

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
                    <th className="px-3 py-2 text-left">CC-Máquina</th>
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
                        </td>
                      ) : (
                        <td className="px-3 py-2 font-mono text-xs">
                          {l.articuloId}
                        </td>
                      )}
                      {/* CC-Máquina resuelto por el read-port (ADR-0050):
                          "clave — nombre" / "No catalogado" / "—" si sin CC. */}
                      <td className="px-3 py-2">
                        {l.centroCostoId
                          ? formatCcMaquinaLabel({
                              clave: l.centroCostoClave,
                              nombre: l.centroCostoNombre,
                            })
                          : '—'}
                      </td>
                      <td className="px-3 py-2 text-right font-mono">
                        {l.cantidad.toLocaleString('es-MX', {
                          minimumFractionDigits: 2,
                          maximumFractionDigits: 4,
                        })}
                      </td>
                      <td className="px-3 py-2">{l.unidadMedida}</td>
                      <td className="px-3 py-2 text-right font-mono">
                        {formatearMonto(l.costoUnitarioMxn)}
                      </td>
                      <td className="px-3 py-2 text-right font-mono">
                        {formatearMonto(l.montoTotalMxn)}
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

function EstadoMovimientoBadge({ estado }: { estado: EstadoMovimiento }) {
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
        estado === EstadoMovimiento.Borrador && 'bg-slate-200 text-slate-700',
        estado === EstadoMovimiento.Validado && 'bg-blue-100 text-blue-800',
        estado === EstadoMovimiento.Registrado &&
          'bg-emerald-100 text-emerald-800',
        estado === EstadoMovimiento.Cancelado && 'bg-rose-100 text-rose-800',
      )}
    >
      {EstadoMovimientoLabels[estado]}
    </span>
  );
}

function formatearMonto(v: number): string {
  return new Intl.NumberFormat('es-MX', {
    style: 'currency',
    currency: 'MXN',
    minimumFractionDigits: 2,
  }).format(v);
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
