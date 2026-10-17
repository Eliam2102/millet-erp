import { PageHeader } from '@/components/erp/PageHeader';
import { TrazabilidadComprasDialog } from '@/components/erp/trazabilidad/TrazabilidadComprasDialog';
import { TipoDocumentoTrazabilidad } from '@/components/erp/trazabilidad/types';
import { DocumentoAdjuntosSection } from '@/components/erp/adjuntos/DocumentoAdjuntosSection';
import { AplicarDocumentoFacturaSheet } from '@/features/cxp/components/AplicarDocumentoFacturaSheet';
import { desgloseRetenciones } from '@/features/cxp/lib/conciliacion-p3';
import { useState } from 'react';
import { Link, useParams, useSearch } from '@tanstack/react-router';
import { ArrowLeft, CheckCircle2, Paperclip, Send, Unlock, XCircle } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { ErrorState, DetalleSkeleton } from '@/components/erp';
import { useFactura, useAutorizarFactura, useMarcarRetencionesRevisadas } from '@/features/cxp/api/useFacturas';
import { MotivoBajaDialog } from '@/components/erp/adjuntos/MotivoBajaDialog';
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
import { formatCantidad } from '@/lib/format-cantidad';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { Importe } from '@/features/cxp/components/DetalleCampos';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { ConfirmarAccionDialog } from '@/components/erp/feedback/ConfirmarAccionDialog';

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
const MOTIVO_AUTORIZAR_EN_REVISION =
  'En revisión por área; se podrá autorizar al liberar la revisión.';

export function FacturaDetallePage() {
  const { id } = useParams({ from: FROM });
  const search = useSearch({ from: FROM });
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
  const [aplicacion, setAplicacion] = useState<'anticipo' | 'nc' | null>(null);
  const puedeAplicarAnticipo = useHasPermission(PermisosCanonicos.CuentasPorPagarAnticiposCapturar);
  const puedeAplicarNc = useHasPermission(PermisosCanonicos.CuentasPorPagarNotasCreditoCapturar);
  const [cancelarAbierto, setCancelarAbierto] = useState(false);
  const [enviarRevisionAbierto, setEnviarRevisionAbierto] = useState(false);
  const [liberarRevisionAbierto, setLiberarRevisionAbierto] = useState(false);
  const [adjuntarEvidenciaAbierto, setAdjuntarEvidenciaAbierto] = useState(false);
  const revisarRetenciones = useMarcarRetencionesRevisadas();
  const puedeRevisarRetenciones = useHasPermission(PermisosCanonicos.CuentasPorPagarRetencionesAdministrar);
  const [autorizarAbierto, setAutorizarAbierto] = useState(false);
  const [revisarAbierto, setRevisarAbierto] = useState(false);
  const [errorRevision, setErrorRevision] = useState<string | null>(null);
  const retencionesPendientes = !!query.data?.alertaRetenciones && !query.data?.retencionesRevisadasEn;

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
        onSettled: () => setAutorizarAbierto(false),
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
      {query.data && <TrazabilidadComprasDialog tipo={TipoDocumentoTrazabilidad.FacturaProveedor} id={id} />}
      <div className="flex items-center gap-2">
        <Button asChild variant="ghost" size="sm">
          <Link to="/cxp/facturas" search={search}>
            <ArrowLeft className="mr-2 size-3.5" />
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
        <DetalleSkeleton />
      ) : query.data == null ? null : (
        <div className="space-y-6">
          <PageHeader
            titulo={
              <>
                {query.data.serieProveedor ? `${query.data.serieProveedor}-${query.data.folioProveedor ?? ''}` : (query.data.folioProveedor ?? 'Sin folio')}
              </>
            }
            estado={
              <>
                <EstadoPasivoChip estado={query.data.estado} />
                {query.data.enRevision && (
                  <Badge variant="warning" data-testid="en-revision-flag">
                    En revisión por área
                  </Badge>
                )}
                <ToleranciaIndicator
                  diferencia={query.data.diferenciaContraOc}
                  cancelada={query.data.estado === EstadoPasivo.Cancelada}
                />
              </>
            }
            descripcion={
              <>
                Fecha documento: <DateTimeDisplay value={query.data.fechaDocumento} variant="media" /> ·
                Contabilización: <DateTimeDisplay value={query.data.fechaContabilizacion} variant="media" />{' '}
                · Vence: <DateTimeDisplay value={query.data.fechaVencimiento} variant="media" />
              </>
            }
            mono
          />

          {/* Sub-topbar con acciones contextuales */}
          <div
            className="flex flex-wrap gap-2 rounded-md border bg-muted/30 px-3 py-2"
            data-print="hidden"
          >
            {query.data.estado !== EstadoPasivo.Cancelada && query.data.estado !== EstadoPasivo.Pagada && query.data.saldoPendiente > 0 && <>
              {puedeAplicarAnticipo && <Button variant="secondary" size="sm" onClick={() => setAplicacion('anticipo')}>Aplicar anticipo</Button>}
              {puedeAplicarNc && <Button variant="secondary" size="sm" onClick={() => setAplicacion('nc')}>Aplicar NC</Button>}
            </>}
            {(query.data.estado === EstadoPasivo.Capturada || query.data.estado === EstadoPasivo.EnRevision) &&
              puedeAutorizar && (
                <>
                  <Button
                    variant="default"
                    size="sm"
                    onClick={() => setAutorizarAbierto(true)}
                    disabled={autorizar.isPending || retencionesPendientes || query.data.estado === EstadoPasivo.EnRevision}
                    aria-describedby={
                      retencionesPendientes
                        ? 'alerta-retenciones'
                        : query.data.estado === EstadoPasivo.EnRevision
                          ? 'motivo-autorizar-revision'
                          : undefined
                    }
                    title={
                      query.data.estado === EstadoPasivo.EnRevision
                        ? MOTIVO_AUTORIZAR_EN_REVISION
                        : retencionesPendientes
                          ? 'Fiscal debe revisar las retenciones antes de autorizar.'
                          : undefined
                    }
                  >
                    <CheckCircle2 className="mr-1 size-3.5" aria-hidden="true" />
                    Autorizar
                  </Button>
                  {query.data.estado === EstadoPasivo.EnRevision && (
                    <span id="motivo-autorizar-revision" className="sr-only">
                      {MOTIVO_AUTORIZAR_EN_REVISION}
                    </span>
                  )}
                </>
              )}
            {(query.data.estado === EstadoPasivo.Capturada || query.data.estado === EstadoPasivo.Autorizada) &&
              !query.data.enRevision &&
              puedeEnviarRevision && (
                <Button variant="outline" size="sm" onClick={() => setEnviarRevisionAbierto(true)}>
                  <Send className="mr-1 size-3.5" />
                  Enviar a revisión
                </Button>
              )}
            {query.data.estado === EstadoPasivo.EnRevision && puedeLiberarRevision && (
              <Button variant="outline" size="sm" onClick={() => setLiberarRevisionAbierto(true)}>
                <Unlock className="mr-1 size-3.5" />
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
                  <Paperclip className="mr-1 size-3.5" />
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
                  className="text-danger-fg"
                >
                  <XCircle className="mr-1 size-3.5" />
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

          <dl className="grid grid-cols-1 gap-x-6 gap-y-2 rounded-md border p-4 @xl:grid-cols-2 @3xl:grid-cols-4 @5xl:grid-cols-6">
            <Campo
              label="Proveedor"
              valor={query.data.proveedorNombre?.trim() || '[PROVEEDOR POR CONFIRMAR]'}
              mono={query.data.proveedorNombre == null}
            />
            <Campo
              label="Sucursal"
              valor={query.data.sucursalNombre?.trim() || '[SUCURSAL POR CONFIRMAR]'}
              mono={query.data.sucursalNombre == null}
            />
            <div>
              <dt className="text-xs text-ink-muted">Orden de compra</dt>
              <dd className="font-mono text-xs">
                {query.data.ordenCompraId ? (
                  <Link to="/compras/ordenes/$id" params={{ id: query.data.ordenCompraId }} className="text-brand underline">
                    {query.data.ordenCompraFolio?.trim() || '[FOLIO POR CONFIRMAR]'}
                  </Link>
                ) : '—'}
              </dd>
            </div>
            <Campo label="UUID CFDI" valor={query.data.uuidCfdi ?? '—'} mono />
            <Campo label="Moneda" valor={query.data.moneda} />
            <Campo
              label="Tipo de cambio"
              valor={query.data.tipoCambio != null ? formatCantidad(query.data.tipoCambio, 4) : '—'}
            />
          </dl>

          {query.data.estado === EstadoPasivo.Capturada && puedeEditar && (
            <EditarDatosP8 key={`${query.data.id}-${query.data.version}`} factura={query.data} />
          )}
          {query.data.alertaRetenciones && (
            <div
              id="alerta-retenciones"
              role="note"
              className="flex flex-wrap items-center gap-3 rounded-md bg-warning-note-bg px-3 py-2 text-warning-note-fg"
            >
              <span className="flex-1">
                {query.data.alertaRetenciones}
                {query.data.retencionesRevisadasEn ? (
                  <span className="mt-1 block text-xs">
                    Revisadas por Fiscal el <DateTimeDisplay value={query.data.retencionesRevisadasEn} variant="media" />:{' '}
                    {query.data.retencionesRevisionMotivo}
                  </span>
                ) : (
                  <span className="mt-1 block text-xs font-medium">
                    No se puede autorizar para pago hasta que Fiscal las revise.
                  </span>
                )}
              </span>
              {retencionesPendientes && puedeRevisarRetenciones && query.data.estado === EstadoPasivo.Capturada && (
                <Button variant="secondary" size="sm" onClick={() => { setErrorRevision(null); setRevisarAbierto(true); }}>
                  Marcar como revisadas
                </Button>
              )}
            </div>
          )}
          {revisarAbierto && (
            <MotivoBajaDialog
              nombreArchivo={query.data.folioProveedor ?? ''}
              titulo="Marcar retenciones como revisadas"
              descripcion="Confirma que Fiscal revisó las retenciones de esta factura. Queda registrado quién y cuándo."
              etiqueta="Qué revisaste"
              accion="Marcar como revisadas"
              variante="default"
              error={errorRevision}
              onCerrar={() => setRevisarAbierto(false)}
              onConfirmar={async (motivo) => {
                try {
                  await revisarRetenciones.mutateAsync({
                    id: query.data!.id,
                    versionEsperada: query.data!.version,
                    motivo,
                    idempotencyKey,
                  });
                  setRevisarAbierto(false);
                  toast.success('Retenciones marcadas como revisadas');
                } catch (e) {
                  setErrorRevision(esApiError(e) ? (e.problem.detail ?? e.message) : 'No se pudo guardar la revisión.');
                }
              }}
            />
          )}
          <p className="text-sm">
            <span className="text-ink-muted">Obra: </span>
            {query.data.obra || '[OBRA POR CONFIRMAR]'}
          </p>
          <section className="rounded-md border p-4">
            <h2 className="mb-2 text-sm font-semibold uppercase tracking-wide text-muted-foreground">
              Totales
            </h2>
            <div className="space-y-4">
            <div className="space-y-1">
              <h3 className="text-xs font-medium uppercase tracking-wide text-ink-muted">Fiscal</h3>
              <dl className="grid grid-cols-2 gap-x-6 gap-y-2 text-sm sm:grid-cols-4">
                <Importe label="Subtotal" v={query.data.subtotal} m={query.data.moneda} />
                <Importe label="Descuentos" v={query.data.descuentos} m={query.data.moneda} />
                <Importe label="IVA trasladado" v={query.data.impuestosTrasladados} m={query.data.moneda} />
                <Importe label="Retenciones totales" v={query.data.retenciones} m={query.data.moneda} />
                <Importe label="ISR retenido" v={desgloseRetenciones(query.data.retencionesDetalle).isr} m={query.data.moneda} />
                <Importe label="IVA retenido" v={desgloseRetenciones(query.data.retencionesDetalle).iva} m={query.data.moneda} />
                <Importe label="Total" v={query.data.total} m={query.data.moneda} strong />
              </dl>
            </div>
            <div className="space-y-1">
              <h3 className="text-xs font-medium uppercase tracking-wide text-ink-muted">Aplicaciones</h3>
              <dl className="grid grid-cols-2 gap-x-6 gap-y-2 text-sm sm:grid-cols-4">
                <Importe label="Anticipo aplicado" v={query.data.anticipoAplicadoTotal} m={query.data.moneda} />
                <Importe label="NC aplicadas" v={query.data.ncAplicadasTotal} m={query.data.moneda} />
                <Importe label="Cargos aplicados" v={query.data.cargosAplicadosTotal ?? 0} m={query.data.moneda} />
                <Importe label="Pagado" v={query.data.importePagado} m={query.data.moneda} />
              </dl>
            </div>
            <div className="space-y-1">
              <h3 className="text-xs font-medium uppercase tracking-wide text-ink-muted">Resultado</h3>
              <dl className="grid grid-cols-2 gap-x-6 gap-y-2 text-sm sm:grid-cols-4">
                <Importe label="Elegible para pago" v={query.data.elegible} m={query.data.moneda} strong />
                <Importe label="Retenido por falta de recepción" v={query.data.retenido} m={query.data.moneda} />
                <Importe label="Saldo pendiente" v={query.data.saldoPendiente} m={query.data.moneda} strong grande />
              </dl>
            </div>
            </div>
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
                      <td className="py-2 pl-3 pr-4 text-right tabular-nums">{formatCantidad(l.cantidad, 4)}</td>
                      <td className="px-3 py-2">{l.claveUnidad}</td>
                      <td className="py-2 pl-3 pr-4 text-right tabular-nums">
                        {formatCantidad(l.precioUnitario, 4)}
                      </td>
                      <td className="py-2 pl-3 pr-4 text-right">
                        <MoneyDisplay amount={l.importe} currency={query.data.moneda} strong />
                      </td>
                      <td className="py-2 pl-3 pr-4 text-right">
                        <MoneyDisplay amount={l.descuento} currency={query.data.moneda} />
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

          <ConfirmarAccionDialog
            open={autorizarAbierto}
            onOpenChange={setAutorizarAbierto}
            titulo="Autorizar factura para pago"
            resumen={
              <>
                Autorizas la factura {query.data.serieProveedor ? `${query.data.serieProveedor}-` : ''}
                {query.data.folioProveedor ?? 'sin folio'}
                {query.data.proveedorNombre?.trim() ? ` de ${query.data.proveedorNombre.trim()}` : ''} por{' '}
                <MoneyDisplay amount={query.data.total} currency={query.data.moneda} strong />. Pasa a
                Tesorería como pasivo listo para pago.
              </>
            }
            verbo="Autorizar para pago"
            tono="primario"
            pendiente={autorizar.isPending}
            onConfirmar={handleAutorizar}
          />
          <AplicarDocumentoFacturaSheet factura={query.data} tipo={aplicacion} onClose={() => setAplicacion(null)} />
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
      <dd className={cn('text-sm', mono && 'break-all font-mono text-xs')}>{valor}</dd>
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
      Diferencia aceptada: {formatCantidad(diferencia, 2)}
    </Badge>
  );
}

