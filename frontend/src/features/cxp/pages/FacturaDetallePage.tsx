import { DocumentoAdjuntosSection } from '@/components/erp/adjuntos/DocumentoAdjuntosSection';
import { desgloseRetenciones } from '@/features/cxp/lib/conciliacion-p3';
import { useState } from 'react';
import { Link, useParams } from '@tanstack/react-router';
import { ArrowLeft, CheckCircle2, Paperclip, Send, Unlock, XCircle } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { ErrorState, TableSkeleton } from '@/components/erp';
import { useFactura, useAutorizarFactura } from '@/features/cxp/api/useFacturas';
import { EstadoPasivo, MotivoCancelacionLabels } from '@/features/cxp/api/types';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { EstadoPasivoChip } from '@/features/cxp/components/EstadoPasivoChip';
import { CancelarFacturaSheet } from '@/features/cxp/components/CancelarFacturaSheet';
import { EnviarRevisionSheet } from '@/features/cxp/components/EnviarRevisionSheet';
import { LiberarRevisionSheet } from '@/features/cxp/components/LiberarRevisionSheet';
import { AdjuntarEvidenciaSheet } from '@/features/cxp/components/AdjuntarEvidenciaSheet';
import { EvidenciasList } from '@/features/cxp/components/EvidenciasList';
import { cn } from '@/lib/utils';
import { EditarDatosP8 } from '@/features/cxp/components/EditarDatosP8';

/**
 * <c>P2 — Detalle de Factura de Proveedor</c> (doc 07 §FE-F2-PR1).
 * Vista de cabecera + líneas + sub-topbar con acciones contextuales
 * según estado: Capturada/EnRevision pueden cancelar, Capturada puede
 * autorizar (override manual §A7 pre-F4).
 *
 * <para>El indicador de tolerancia (diferencia contra OC) se muestra
 * inline en la cabecera con color según signo y magnitud.</para>
 *
 * <para>PLATFORM-TODO(&lt;EnviarRevisionSheet&gt;): el endpoint
 * <c>POST /facturas/{id}/enviar-revision</c> existe en backend desde
 * F4-PR2; el sheet se agrega en FE-F3-PR1 (revisión + evidencias).</para>
 */
const FROM = '/_app/cxp/facturas/$id' as const;

export function FacturaDetallePage() {
  const { id } = useParams({ from: FROM });
  const idempotencyKey = useFormIdempotencyKey();
  const query = useFactura(id);
  const autorizar = useAutorizarFactura();
  const puedeAutorizar = useHasPermission(PermisosCanonicos.CuentasPorPagarFacturasAutorizar);
  const puedeEditar = useHasPermission(PermisosCanonicos.CuentasPorPagarFacturasEditar);
  const puedeCancelar = useHasPermission(PermisosCanonicos.CuentasPorPagarFacturasCancelar);
  const puedeEnviarRevision = useHasPermission(
    PermisosCanonicos.CuentasPorPagarFacturasEnviarRevision,
  );
  const puedeLiberarRevision = useHasPermission(
    PermisosCanonicos.CuentasPorPagarFacturasLiberarRevision,
  );
  const [cancelarAbierto, setCancelarAbierto] = useState(false);
  const [enviarRevisionAbierto, setEnviarRevisionAbierto] = useState(false);
  const [liberarRevisionAbierto, setLiberarRevisionAbierto] = useState(false);
  const [adjuntarEvidenciaAbierto, setAdjuntarEvidenciaAbierto] = useState(false);

  function handleAutorizar() {
    if (!query.data) return;
    autorizar.mutate(
      {
        id: query.data.id,
        versionEsperada: query.data.version,
        idempotencyKey,
      },
      {
        onSuccess: () => toast.success('Factura autorizada'),
        onError: (error) => {
          if (esApiError(error)) {
            toast.error(error.problem.title, {
              description: error.traceId ? `Código: ${error.traceId}` : undefined,
            });
            return;
          }
          toast.error('Error al autorizar la factura.');
        },
      },
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-2">
        <Button asChild variant="ghost" size="sm">
          <Link to="/cxp/facturas" search={{}}>
            <ArrowLeft className="mr-2 h-4 w-4" />
            Facturas
          </Link>
        </Button>
      </div>

      {query.isError ? (
        <ErrorState
          title="No se pudo cargar la factura"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={6}
          columns={[
            { width: 'w-40' },
            { width: 'w-32' },
            { width: 'w-32' },
            { width: 'w-32' },
            { width: 'w-32' },
          ]}
        />
      ) : query.data == null ? null : (
        <div className="space-y-6">
          <header className="space-y-2">
            <div className="flex flex-wrap items-center gap-3">
              <h1 className="text-2xl font-semibold tracking-tight font-mono">
                {query.data.serieProveedor
                  ? `${query.data.serieProveedor}-${query.data.folioProveedor ?? ''}`
                  : (query.data.folioProveedor ?? 'Sin folio')}
              </h1>
              <EstadoPasivoChip estado={query.data.estado} />
              {query.data.enRevision && (
                <span
                  className="inline-flex items-center rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-800"
                  data-testid="en-revision-flag"
                >
                  En revisión por área
                </span>
              )}
              <ToleranciaIndicator
                diferencia={query.data.diferenciaContraOc}
                cancelada={query.data.estado === EstadoPasivo.Cancelada}
              />
            </div>
            <p className="text-sm text-muted-foreground">
              Fecha documento: {formatearFecha(query.data.fechaDocumento)} · Contabilización:{' '}
              {formatearFecha(query.data.fechaContabilizacion)} · Vence:{' '}
              {query.data.fechaVencimiento}
            </p>
          </header>

          {/* Sub-topbar con acciones contextuales */}
          <div
            className="flex flex-wrap gap-2 rounded-md border bg-muted/30 px-3 py-2"
            data-print="hidden"
          >
            {query.data.estado === EstadoPasivo.Capturada && puedeAutorizar && (
              <Button
                variant="default"
                size="sm"
                onClick={handleAutorizar}
                disabled={autorizar.isPending}
              >
                <CheckCircle2 className="mr-1 h-3 w-3" />
                Autorizar
              </Button>
            )}
            {query.data.estado === EstadoPasivo.Capturada &&
              !query.data.enRevision &&
              puedeEnviarRevision && (
                <Button variant="outline" size="sm" onClick={() => setEnviarRevisionAbierto(true)}>
                  <Send className="mr-1 h-3 w-3" />
                  Enviar a revisión
                </Button>
              )}
            {query.data.estado === EstadoPasivo.EnRevision && puedeLiberarRevision && (
              <Button variant="outline" size="sm" onClick={() => setLiberarRevisionAbierto(true)}>
                <Unlock className="mr-1 h-3 w-3" />
                Liberar revisión
              </Button>
            )}
            {(query.data.estado === EstadoPasivo.Capturada ||
              query.data.estado === EstadoPasivo.EnRevision) &&
              puedeEnviarRevision && (
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => setAdjuntarEvidenciaAbierto(true)}
                >
                  <Paperclip className="mr-1 h-3 w-3" />
                  Adjuntar evidencia
                </Button>
              )}
            {(query.data.estado === EstadoPasivo.Capturada ||
              query.data.estado === EstadoPasivo.EnRevision) &&
              puedeCancelar && (
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => setCancelarAbierto(true)}
                  className="text-destructive"
                >
                  <XCircle className="mr-1 h-3 w-3" />
                  Cancelar
                </Button>
              )}
            {query.data.estado === EstadoPasivo.Cancelada &&
              query.data.motivoCancelacion != null && (
                <p className="text-sm text-danger-fg">
                  Cancelada por{' '}
                  <strong>{MotivoCancelacionLabels[query.data.motivoCancelacion]}</strong>
                  {query.data.motivoCancelacionTexto && ` — ${query.data.motivoCancelacionTexto}`}
                </p>
              )}
          </div>

          <section className="grid grid-cols-1 gap-x-6 gap-y-2 rounded-md border p-4 md:grid-cols-2">
            <Campo
              label="Proveedor"
              valor={query.data.proveedorNombre ?? query.data.proveedorId}
              mono={query.data.proveedorNombre == null}
            />
            <Campo
              label="Sucursal"
              valor={query.data.sucursalNombre ?? query.data.sucursalId}
              mono={query.data.sucursalNombre == null}
            />
            <Campo label="Orden de compra" valor={query.data.ordenCompraId ?? '—'} mono />
            <Campo label="UUID CFDI" valor={query.data.uuidCfdi ?? '—'} mono />
            <Campo label="Moneda" valor={query.data.moneda} />
            <Campo
              label="Tipo de cambio"
              valor={query.data.tipoCambio != null ? query.data.tipoCambio.toFixed(4) : '—'}
            />
          </section>

          {query.data.estado === EstadoPasivo.Capturada && puedeEditar && (
            <EditarDatosP8 key={`${query.data.id}-${query.data.version}`} factura={query.data} />
          )}
          {query.data.alertaRetenciones && (
            <p role="note" className="rounded-md bg-warning-note-bg px-3 py-2 text-warning-note-fg">
              {query.data.alertaRetenciones}
            </p>
          )}
          <p className="text-sm">
            <span className="text-ink-muted">Obra: </span>
            {query.data.obra || '[OBRA POR CONFIRMAR]'}
          </p>
          <section className="rounded-md border p-4">
            <h2 className="mb-2 text-sm font-semibold uppercase tracking-wide text-muted-foreground">
              Totales
            </h2>
            <dl className="grid grid-cols-2 gap-x-6 gap-y-1 text-sm sm:grid-cols-4">
              <Importe label="Subtotal" v={query.data.subtotal} m={query.data.moneda} />
              <Importe label="Descuentos" v={query.data.descuentos} m={query.data.moneda} />
              <Importe
                label="IVA trasladado"
                v={query.data.impuestosTrasladados}
                m={query.data.moneda}
              />
              <Importe
                label="Retenciones totales"
                v={query.data.retenciones}
                m={query.data.moneda}
              />
              <Importe
                label="ISR retenido"
                v={desgloseRetenciones(query.data.retencionesDetalle).isr}
                m={query.data.moneda}
              />
              <Importe
                label="IVA retenido"
                v={desgloseRetenciones(query.data.retencionesDetalle).iva}
                m={query.data.moneda}
              />
              <Importe
                label="Elegible para pago"
                v={query.data.elegible}
                m={query.data.moneda}
                strong
              />
              <Importe
                label="Retenido por falta de recepción"
                v={query.data.retenido}
                m={query.data.moneda}
              />
              <Importe label="Total" v={query.data.total} m={query.data.moneda} strong />
              <Importe
                label="Anticipo aplicado"
                v={query.data.anticipoAplicadoTotal}
                m={query.data.moneda}
              />
              <Importe label="NC aplicadas" v={query.data.ncAplicadasTotal} m={query.data.moneda} />
              <Importe label="Pagado" v={query.data.importePagado} m={query.data.moneda} />
              <Importe
                label="Saldo pendiente"
                v={query.data.saldoPendiente}
                m={query.data.moneda}
                strong
              />
            </dl>
          </section>

          <DocumentoAdjuntosSection base={`/api/v1/cuentas-por-pagar/facturas/${query.data.id}`} tipo="factura_proveedor" permiso="cuentas_por_pagar.facturas" />
          <section className="space-y-2">
            <h2 className="text-sm font-semibold uppercase tracking-wide text-muted-foreground">
              Líneas ({query.data.lineas.length})
            </h2>
            <div className="overflow-x-auto rounded-md border">
              <table className="w-full text-sm">
                <thead className="bg-muted/50">
                  <tr>
                    <th className="px-3 py-2 text-left">#</th>
                    <th className="px-3 py-2 text-left">Descripción</th>
                    <th className="px-3 py-2 text-right">Cantidad</th>
                    <th className="px-3 py-2 text-left">UM</th>
                    <th className="px-3 py-2 text-right">P. Unitario</th>
                    <th className="px-3 py-2 text-right">Importe</th>
                    <th className="px-3 py-2 text-right">Descuento</th>
                  </tr>
                </thead>
                <tbody>
                  {query.data.lineas.map((l) => (
                    <tr key={l.id} className="border-t">
                      <td className="px-3 py-2">{l.posicion}</td>
                      <td className="px-3 py-2">{l.descripcion}</td>
                      <td className="px-3 py-2 text-right font-mono">{l.cantidad.toFixed(4)}</td>
                      <td className="px-3 py-2">{l.claveUnidad}</td>
                      <td className="px-3 py-2 text-right font-mono">
                        {l.precioUnitario.toFixed(4)}
                      </td>
                      <td className="px-3 py-2 text-right font-mono">{l.importe.toFixed(2)}</td>
                      <td className="px-3 py-2 text-right font-mono">
                        {l.descuento != null ? l.descuento.toFixed(2) : '—'}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>

          <section className="space-y-2">
            <h2 className="text-sm font-semibold uppercase tracking-wide text-muted-foreground">
              Evidencias de autorización
            </h2>
            <EvidenciasList facturaId={query.data.id} />
          </section>

          <CancelarFacturaSheet
            open={cancelarAbierto}
            onOpenChange={setCancelarAbierto}
            factura={query.data}
          />
          <EnviarRevisionSheet
            open={enviarRevisionAbierto}
            onOpenChange={setEnviarRevisionAbierto}
            factura={query.data}
          />
          <LiberarRevisionSheet
            open={liberarRevisionAbierto}
            onOpenChange={setLiberarRevisionAbierto}
            factura={query.data}
          />
          <AdjuntarEvidenciaSheet
            open={adjuntarEvidenciaAbierto}
            onOpenChange={setAdjuntarEvidenciaAbierto}
            factura={query.data}
          />
        </div>
      )}
    </div>
  );
}

interface CampoProps {
  label: string;
  valor: string;
  mono?: boolean;
}

function Campo({ label, valor, mono }: CampoProps) {
  return (
    <div>
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className={cn('text-sm', mono && 'font-mono text-xs')}>{valor}</dd>
    </div>
  );
}

interface ImporteProps {
  label: string;
  v: number;
  m: string;
  strong?: boolean;
}

function Importe({ label, v, m, strong }: ImporteProps) {
  return (
    <div>
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className={cn('font-mono', strong ? 'text-base font-semibold' : 'text-sm')}>
        {v.toFixed(2)} {m}
      </dd>
    </div>
  );
}

interface ToleranciaIndicatorProps {
  diferencia: number;
  cancelada: boolean;
}

/** El estado conciliado viene del servidor; no se inventa otro umbral en la UI. */
function ToleranciaIndicator({ diferencia, cancelada }: ToleranciaIndicatorProps) {
  if (cancelada) return null;
  if (diferencia === 0) {
    return <Badge variant="success">Sin diferencia</Badge>;
  }
  return (
    <Badge variant="warning" className="tabular-nums">
      Diferencia aceptada: {diferencia.toFixed(2)}
    </Badge>
  );
}

function formatearFecha(iso: string): string {
  try {
    return new Date(iso).toLocaleDateString('es-MX', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
    });
  } catch {
    return iso;
  }
}
