import { ImprimirPdfButton } from '@/components/erp/reportes/ImprimirPdfButton';
import { problemaConsulta } from '@/components/erp/feedback/problema-consulta';
import { formatMoney } from '@/lib/money';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { formatCantidad } from '@/lib/format-cantidad';
import { DatosClienteFactura } from '../components/DatosClienteFactura';
import { ErrorState } from '@/components/erp';
import { ResumenImportes } from '@/components/erp/display/ResumenImportes';
import { PageHeader } from '@/components/erp/PageHeader';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Checkbox } from '@/components/ui/checkbox';
import { Input } from '@/components/ui/input';
import { TextAreaField } from '@/components/erp/forms/TextAreaField';
import { Label } from '@/components/ui/label';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { EmbarqueCeResumen } from '@/features/comercio-exterior/components/EmbarqueCeResumen';
import {
  useAplicarPedimento,
  useCancelacionEstatus,
  useComprobante,
  useComprobanteEnvios,
  useEmitirNcBonificacion,
  useReenviarCorreo,
} from '@/features/facturacion/api/useFacturas';
import { CancelarComprobanteForm } from '@/features/facturacion/components/CancelarComprobanteForm';
import { CobrarEnCajaLink } from '@/features/facturacion/components/CobrarEnCajaLink';
import { CobroFacturaPanel } from '@/features/facturacion/components/CobroFacturaPanel';
import { AnticipoPicker } from '@/features/facturacion/components/AnticipoPicker';
import { ErrorTimbradoAviso } from '@/features/facturacion/components/ErrorTimbradoAviso';
import { EstadoCancelacionBanner } from '@/features/facturacion/components/EstadoCancelacionBanner';
import { EstadoReembolsoNcBadge } from '@/features/facturacion/components/EstadoReembolsoNcBadge';
import { IntentosTimbradoPanel } from '@/features/facturacion/components/IntentosTimbradoPanel';
import { SolicitarReembolsoNcDialog } from '@/features/facturacion/components/SolicitarReembolsoNcDialog';
import { SustituirFacturaPanel } from '@/features/facturacion/components/SustituirFacturaPanel';
import { NotasCreditoPendientesAviso } from '@/features/facturacion/components/NotasCreditoPendientesAviso';
import { EfectosComprobanteSeccion } from '@/features/facturacion/components/EfectosComprobanteSeccion';
import { TimbradoFallidoBanner } from '@/features/facturacion/components/TimbradoFallidoBanner';
import { TrazabilidadFacturacion } from '@/features/facturacion/components/TrazabilidadFacturacion';
import { descargarArchivo, obtenerArchivo } from '@/features/facturacion/lib/descargas';
import type { FacturasSearch } from '@/features/facturacion/lib/facturas-search-schema';
import { ETIQUETA_MOTIVO_NC, ETIQUETA_TIPO_RELACION } from '@/features/facturacion/lib/glosario';
import { mostrarErrorReceptorFiscal } from '@/features/facturacion/lib/mostrar-error-receptor-fiscal';
import { ChipTimbrado } from '@/features/facturacion/pages/BandejaFacturas';
import { useFormIdempotencyKey } from '@/lib/api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { Link, useParams, useSearch } from '@tanstack/react-router';
import { Ban, Download, FileCode, Info, Mail, Receipt, RefreshCw, TriangleAlert, X } from 'lucide-react';
import { useState } from 'react';
import type { AnticipoEstadoCuenta } from '@/features/facturacion/api/types';
import { useAplicarAnticipoAFactura } from '@/features/facturacion/api/useAnticipos';
import { toast } from 'sonner';

const BASE = '/api/v1/facturacion/facturas';
const NC_BASE = '/api/v1/facturacion/notas-credito';

/**
 * <c>Detalle del comprobante</c> (FE-F2). Cabecera fiscal + conceptos +
 * cadena de relaciones CFDI (B4) + bitácora de envío (B6) + descargas
 * XML/PDF (B7/B8) + reenviar correo + impresión (CSS print). Patrón
 * "listado → detalle → en el detalle bandejas".
 */
export function DetalleFactura() {
  const { id } = useParams({ from: '/_app/facturacion/facturas/$id' });
  const query = useComprobante(id);

  return (
    <div className="@container min-w-0 space-y-5">
      {query.isError ? (
        <ErrorState
          title="No se pudo cargar la factura"
          problem={problemaConsulta(query.error)}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading || query.data == null ? (
        <div className="space-y-3">
          <div className="h-8 w-64 animate-pulse rounded bg-surface-muted" />
          <div className="h-40 w-full animate-pulse rounded bg-surface-muted" />
        </div>
      ) : (
        <Contenido f={query.data} />
      )}
    </div>
  );
}

function Contenido({ f }: { f: NonNullable<ReturnType<typeof useComprobante>['data']> }) {
  const search = useSearch({ strict: false }) as FacturasSearch;
  const envios = useComprobanteEnvios(f.id);
  const puedeEmitir = useHasPermission(PermisosCanonicos.FacturacionFacturasEmitir);
  const puedeCancelar = useHasPermission(PermisosCanonicos.FacturacionCancelacionesSolicitar);
  const puedeNc = useHasPermission(PermisosCanonicos.FacturacionNotasCreditoBonificacion);
  const puedeReembolsar = useHasPermission(
    PermisosCanonicos.FacturacionNotasCreditoReembolsoSolicitar,
  );
  const puedeVerNc = useHasPermission(PermisosCanonicos.FacturacionNotasCreditoLeer);
  const [reembolsoNc, setReembolsoNc] = useState<
    NonNullable<typeof f.notasCreditoAplicadas>[number] | null
  >(null);
  const cancelacion = useCancelacionEstatus(f.id);
  // F6.5: con una solicitud pendiente de autorización no se puede pedir otra.
  const cancelacionPendiente = cancelacion.data?.estadoSolicitud === 'PendienteAutorizacion';
  const [accion, setAccion] = useState<'cancelar' | 'nc' | 'anticipo' | 'sustituir' | null>(null);
  const puedeAplicarAnticipo = useHasPermission(PermisosCanonicos.FacturacionAnticiposVincular);
  const timbrada = f.estado === 'Timbrado' || f.estado === 'Cancelado';
  // M20 (prueba S3-4): el XML existe desde el timbre y se descarga siempre, también con la cancelación en proceso o ya cancelada.
  const conXml = timbrada || f.estado === 'CancelacionPendiente';
  // Solo una factura vigente (Timbrado) admite NC / cancelación.
  const vigente = f.estado === 'Timbrado';
  const pendientePedimento = f.estado === 'PendientePedimento';

  async function descargar(tipo: 'xml' | 'pdf' | 'termica') {
    try {
      if (tipo === 'xml') {
        await descargarArchivo(`${BASE}/${f.id}/xml`, `${f.folio}.xml`);
      } else if (tipo === 'pdf') {
        await descargarArchivo(`${BASE}/${f.id}/pdf`, `${f.folio}.pdf`);
      } else {
        await descargarArchivo(`${BASE}/${f.id}/pdf?formato=termica`, `${f.folio}-termica.pdf`);
      }
    } catch {
      toast.error('No se pudo descargar el archivo. Revisa tu conexión y vuelve a intentarlo.');
    }
  }

  // FMT-4: XML y PDF (formato de Millet) de cada NC aplicada, como en el detalle del anticipo.
  async function descargarNc(ncId: string, folio: string, tipo: 'xml' | 'pdf') {
    try {
      await descargarArchivo(`${NC_BASE}/${ncId}/${tipo}`, `${folio}.${tipo}`);
    } catch {
      toast.error('No se pudo descargar el archivo. Revisa tu conexión y vuelve a intentarlo.');
    }
  }

  return (
    <div className="@container min-w-0 space-y-5">
      <DatosClienteFactura factura={f} puedeEditar={puedeEmitir} />
      {(f.noCertificadoEmisor || f.cadenaOriginalTimbre) && <dl className="space-y-2 text-sm">
        <div><dt>Certificado del emisor</dt><dd className="font-mono">{f.noCertificadoEmisor || <span className="text-ink-muted">—</span>}</dd></div>
        <div><dt>Cadena original del timbre</dt><dd className="break-all font-mono text-xs">{f.cadenaOriginalTimbre || <span className="text-ink-muted">—</span>}</dd></div>
      </dl>}
      <dl className="flex flex-wrap gap-5 text-sm tabular-nums">
        <div><dt>Total de kilos</dt><dd>{f.lineas.some(l => l.pesoKg != null) ? formatCantidad(f.totalKg) : <span className="text-ink-muted">—</span>}</dd></div>
        <div><dt>Total de pies²</dt><dd>{f.lineas.some(l => l.areaPie2 != null) ? formatCantidad(f.totalPie2) : <span className="text-ink-muted">—</span>}</dd></div>
        <div><dt>Total de piezas</dt><dd>{formatCantidad(f.totalPiezas)}</dd></div>
      </dl>
      {f.datosEmbarqueCe && <EmbarqueCeResumen datos={f.datosEmbarqueCe} />}
      {/* ── Sub-topbar de acciones (§6.5, no se imprime): sticky con
          cerrar (X) que vuelve a la bandeja preservando filtros ──── */}
      <div className="space-y-1">
        <div className="flex flex-wrap items-center gap-3">
          <PageHeader
            mono
            titulo={<>{f.folio}</>}
            descripcion={
              <>
                {f.receptorNombre} · <span className="font-mono">{f.receptorRfc}</span>
              </>
            }
            estado={<ChipTimbrado estado={f.estado} />}
            acciones={
              <div className="flex flex-wrap items-center gap-2" data-print="hidden">
                <div className="flex flex-wrap items-center gap-2">
                  <CobrarEnCajaLink comprobanteId={f.id} folio={f.folio} />
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={() => descargar('xml')}
                    disabled={!conXml}
                    title={!conXml ? 'El XML se genera al timbrar el comprobante.' : undefined}
                  >
                    <FileCode className="mr-1 h-4 w-4" />
                    XML
                  </Button>
                  <Button variant="outline" size="sm" onClick={() => descargar('pdf')}>
                    <Download className="mr-1 h-4 w-4" />
                    PDF
                  </Button>
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={() => descargar('termica')}
                    title="PDF térmico simplificado (solo español)"
                  >
                    <Download className="mr-1 h-4 w-4" />
                    PDF térmica
                  </Button>
                  <ImprimirPdfButton generarPdf={() => obtenerArchivo(`${BASE}/${f.id}/pdf`)} />
                  {puedeAplicarAnticipo && vigente && f.clienteId && (f.totalPorCobrar ?? 0) > 0 && (
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => setAccion((a) => (a === 'anticipo' ? null : 'anticipo'))}
                    >
                      <Receipt className="mr-1 h-4 w-4" />
                      Aplicar anticipo
                    </Button>
                  )}
                  {puedeNc && vigente && (
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => setAccion((a) => (a === 'nc' ? null : 'nc'))}
                    >
                      <Receipt className="mr-1 h-4 w-4" />
                      NC bonificación
                    </Button>
                  )}
                  {puedeEmitir && puedeCancelar && vigente && !cancelacionPendiente && !f.sustituidaPor && (
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => setAccion((a) => (a === 'sustituir' ? null : 'sustituir'))}
                      title="Emite una factura nueva con relación 04 y pide la cancelación de esta con motivo 01"
                    >
                      <RefreshCw className="mr-1 h-4 w-4" />
                      Sustituir
                    </Button>
                  )}
                  {puedeCancelar && vigente && !cancelacionPendiente && (
                    <Button
                      variant="outline"
                      size="sm"
                      className="text-danger-fg"
                      onClick={() => setAccion((a) => (a === 'cancelar' ? null : 'cancelar'))}
                    >
                      <Ban className="mr-1 h-4 w-4" />
                      Cancelar
                    </Button>
                  )}
                </div>
                <Button variant="ghost" size="sm" asChild aria-label="Cerrar detalle">
                  <Link to="/facturacion/facturas" search={search}>
                    <X className="h-4 w-4" />
                  </Link>
                </Button>
              </div>
            }
          />
          <span className="text-xs text-ink-muted">{f.tipo}</span>
        </div>
        {f.uuid && <p className="font-mono text-xs text-ink-muted">UUID {f.uuid}</p>}
      </div>

      {accion === 'anticipo' && f.clienteId && (
        <AplicarAnticipoForm
          facturaId={f.id}
          clienteId={f.clienteId}
          moneda={f.moneda}
          porCobrar={f.totalPorCobrar ?? 0}
          onDone={() => setAccion(null)}
        />
      )}
      {accion === 'nc' && (
        <NcBonificacionForm
          facturaId={f.id}
          totalFactura={f.total}
          moneda={f.moneda}
          puedeReembolsar={puedeReembolsar}
          onDone={() => setAccion(null)}
        />
      )}
      {accion === 'cancelar' && (
        <CancelarComprobanteForm comprobanteId={f.id} sugerirSustituir onDone={() => setAccion(null)} />
      )}
      {accion === 'sustituir' && <SustituirFacturaPanel facturaId={f.id} onDone={() => setAccion(null)} />}

      {pendientePedimento && puedeEmitir && <PedimentoForm facturaId={f.id} motivo={f.motivoPedimento} />}

      <TimbradoFallidoBanner
        comprobanteId={f.id}
        estado={f.estado}
        errorCodigo={f.timbradoErrorCodigo}
        errorMensaje={f.timbradoErrorMensaje}
      />

      <NotasCreditoPendientesAviso notas={f.notasCreditoPendientes ?? []} />

      <EfectosComprobanteSeccion comprobanteId={f.id} />

      <AvisoSustitucion f={f} />

      <EstadoCancelacionBanner
        comprobanteId={f.id}
        cancelacion={cancelacion.data}
        puedeRetirar={puedeCancelar}
      />

      <div className="@container min-w-0 space-y-5">
        <section className="space-y-4">
          <dl className="grid grid-cols-1 gap-4 rounded-lg bg-surface-card p-4 shadow-card-flat @xl:grid-cols-2 @3xl:grid-cols-4">
            <Dato label="Receptor">
              <div>{f.receptorNombre}</div>
              <div className="font-mono text-xs text-ink-muted">{f.receptorRfc}</div>
            </Dato>
            <Dato label="Moneda">{f.moneda}</Dato>
            <Dato label="Método de pago">{f.metodoPago ?? <span className="text-ink-muted">Sin método de pago</span>}</Dato>
            <Dato label="Fecha de timbrado">
              <DateTimeDisplay value={f.fechaTimbrado} variant="datetime" />
            </Dato>
            <Dato label="Versión">{f.version}</Dato>
          </dl>
        </section>

        <section className="space-y-4">
          <section className="space-y-2">
            <h2 className="text-sm font-medium">Partidas ({f.lineas.length})</h2>
            <div className="overflow-x-auto rounded-md border border-line">
              <Table className="w-full text-sm">
                <TableHeader className="bg-surface-muted/50">
                  <TableRow>
                    <TableHead className="px-3 py-2 text-left">#</TableHead>
                    <TableHead className="px-3 py-2 text-left">Descripción</TableHead>
                    <TableHead className="px-3 py-2 text-left">Clave SAT</TableHead>
                    <TableHead className="px-3 py-2 text-right tabular-nums">Cant.</TableHead>
                    <TableHead className="px-3 py-2 text-right tabular-nums">Valor unit.</TableHead>
                    <TableHead className="px-3 py-2 text-right tabular-nums">Descuento</TableHead>
                    <TableHead className="px-3 py-2 text-right tabular-nums">Importe</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {f.lineas.map((l) => (
                    <TableRow key={l.posicion} className="border-t border-line">
                      <TableCell className="px-3 py-2 text-right text-ink-muted tabular-nums">{l.posicion}</TableCell>
                      <TableCell className="px-3 py-2">{l.descripcion}
                        {l.referencia && <p className="text-xs text-ink-muted">{l.referencia}</p>}
                        {(l.pesoKg != null || l.areaPie2 != null) && <p className="text-xs text-ink-muted tabular-nums">Peso: {l.pesoKg ?? '—'} kg · Área: {l.areaPie2 ?? '—'} pies²</p>}
                      </TableCell>
                      <TableCell className="px-3 py-2 font-mono text-xs">
                        {l.claveProdServSat} · {l.claveUnidadSat}
                      </TableCell>
                      <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                        {formatCantidad(l.cantidad)}
                      </TableCell>
                      <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                        {formatMoney({ amount: l.valorUnitario, currency: f.moneda })}
                      </TableCell>
                      <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                        {formatMoney({ amount: l.descuento, currency: f.moneda })}
                      </TableCell>
                      <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                        {formatMoney({ amount: l.importe, currency: f.moneda })}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
          </section>
        </section>

        <section className="space-y-4">
          <div className="@container min-w-0 space-y-5">
            <ResumenImportes
              moneda={f.moneda}
              subtotal={f.subtotal}
              descuento={f.descuento}
              ivaTrasladado={f.impuestosTrasladados}
              otrasRetenciones={[{ etiqueta: 'Retenciones', monto: f.retenciones }]}
              total={f.total}
              estadoPago={
                (f.totalAcreditado ?? 0) > 0
                  ? [
                      { etiqueta: 'Acreditado por NC', monto: -f.totalAcreditado },
                      { etiqueta: 'Por cobrar', monto: f.totalPorCobrar, destacado: true },
                    ]
                  : undefined
              }
            />

            {/* ── NC aplicadas ([Decisión 13-K]) ──────────────────────── */}
            {(f.notasCreditoAplicadas ?? []).length > 0 && (
              <section className="space-y-2">
                <h2 className="text-sm font-medium">
                  Notas de crédito aplicadas ({f.notasCreditoAplicadas!.length})
                </h2>
                <div className="overflow-x-auto rounded-md border border-line">
                  <Table className="w-full text-sm">
                    <TableHeader className="bg-surface-muted/50">
                      <TableRow>
                        <TableHead className="px-3 py-2 text-left">Folio</TableHead>
                        <TableHead className="px-3 py-2 text-left">Motivo</TableHead>
                        <TableHead className="px-3 py-2 text-left">UUID</TableHead>
                        <TableHead className="px-3 py-2 text-right tabular-nums">Total</TableHead>
                        <TableHead className="px-3 py-2 text-left">Reembolso</TableHead>
                        <TableHead className="px-3 py-2 text-right" data-print="hidden">
                          Archivos
                        </TableHead>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {f.notasCreditoAplicadas!.map((nc) => (
                        <TableRow key={nc.id} className="border-t border-line">
                          <TableCell className="px-3 py-2 font-mono text-xs">{nc.folio}</TableCell>
                          <TableCell className="px-3 py-2">
                            {ETIQUETA_MOTIVO_NC[nc.motivo] ?? nc.motivo}
                          </TableCell>
                          <TableCell className="px-3 py-2 font-mono text-xs">
                            {nc.uuid || <span className="text-ink-muted">—</span>}
                          </TableCell>
                          <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                            {formatMoney({ amount: nc.total, currency: f.moneda })}
                          </TableCell>
                          <TableCell className="px-3 py-2">
                            {nc.reembolsoSolicitado || nc.estadoReembolso ? (
                              <EstadoReembolsoNcBadge
                                folio={nc.folio}
                                estado={nc.estadoReembolso ?? null}
                                pagadoEl={nc.reembolsoPagadoEl}
                                referencia={nc.reembolsoReferencia}
                                motivo={nc.reembolsoMotivoResolucion}
                              />
                            ) : puedeReembolsar &&
                              nc.motivo !== 'Amortizacion' &&
                              nc.motivo !== 'Ranura' ? (
                              <Button
                                size="sm"
                                variant="secondary"
                                data-print="hidden"
                                onClick={() => setReembolsoNc(nc)}
                              >
                                Solicitar reembolso
                              </Button>
                            ) : null}
                          </TableCell>
                          <TableCell className="px-3 py-2 text-right" data-print="hidden">
                            {puedeVerNc && nc.uuid ? (
                              <span className="inline-flex gap-1">
                                <Button
                                  variant="ghost"
                                  size="sm"
                                  aria-label={`XML de la nota de crédito ${nc.folio}`}
                                  onClick={() => descargarNc(nc.id, nc.folio, 'xml')}
                                >
                                  XML
                                </Button>
                                <Button
                                  variant="ghost"
                                  size="sm"
                                  aria-label={`PDF de la nota de crédito ${nc.folio}`}
                                  onClick={() => descargarNc(nc.id, nc.folio, 'pdf')}
                                >
                                  PDF
                                </Button>
                              </span>
                            ) : (
                              <span className="text-xs text-ink-muted">—</span>
                            )}
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </div>
                {reembolsoNc && (
                  <SolicitarReembolsoNcDialog
                    notaCreditoId={reembolsoNc.id}
                    folio={reembolsoNc.folio}
                    total={reembolsoNc.total}
                    moneda={f.moneda}
                    onCerrar={() => setReembolsoNc(null)}
                  />
                )}
              </section>
            )}

            {/* ── Cadena de relaciones CFDI (B4): anticipos 07 / NC 01 /
          sustitución 04 ─────────────────────────────────────────── */}
            {f.relaciones.length > 0 && (
              <section className="space-y-2">
                <h2 className="text-sm font-medium">
                  Cadena de relaciones CFDI ({f.relaciones.length})
                </h2>
                <div className="overflow-x-auto rounded-md border border-line">
                  <Table className="w-full text-sm">
                    <TableHeader className="bg-surface-muted/50">
                      <TableRow>
                        <TableHead className="px-3 py-2 text-left">Relación</TableHead>
                        <TableHead className="px-3 py-2 text-left">UUID relacionado</TableHead>
                        <TableHead className="px-3 py-2 text-left">Folio</TableHead>
                        <TableHead className="px-3 py-2 text-right tabular-nums">Total</TableHead>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {f.relaciones.map((r) => (
                        <TableRow key={r.uuidRelacionado} className="border-t border-line">
                          <TableCell className="px-3 py-2">
                            {ETIQUETA_TIPO_RELACION[r.tipoRelacion] ?? r.tipoRelacion}
                          </TableCell>
                          <TableCell className="px-3 py-2 font-mono text-xs">
                            {r.uuidRelacionado}
                          </TableCell>
                          <TableCell className="px-3 py-2 font-mono text-xs">
                            {r.folio || <span className="text-ink-muted">—</span>}
                          </TableCell>
                          <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                            {r.total != null ? formatMoney({ amount: r.total, currency: f.moneda }) : <span className="text-ink-muted">—</span>}
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </div>
              </section>
            )}

            <CobroFacturaPanel factura={f} />

            {/* ── Trazabilidad documento-céntrica (ANT-PR3, doc 13) ───── */}
            <TrazabilidadFacturacion raiz="comprobante" id={f.id} />

            {/* ── Historial de intentos de timbrado (01-G G4, F13-PR2) ── */}
            <IntentosTimbradoPanel comprobanteId={f.id} />

            {/* ── Bitácora de envío (B6) + reenviar ───────────────────── */}
            <section className="space-y-2" data-print="hidden">
              <div className="flex items-center justify-between">
                <h2 className="text-sm font-medium">Bitácora de envío</h2>
              </div>
              {puedeEmitir && timbrada && <ReenviarCorreo facturaId={f.id} />}
              {envios.isLoading ? (
                <div className="h-12 w-full animate-pulse rounded bg-surface-muted" />
              ) : (envios.data ?? []).length === 0 ? (
                <p className="rounded-md border border-dashed px-3 py-3 text-center text-xs text-ink-muted border-line">
                  Sin envíos registrados.
                </p>
              ) : (
                <div className="overflow-x-auto rounded-md border border-line">
                  <Table className="w-full text-sm">
                    <TableHeader className="bg-surface-muted/50">
                      <TableRow>
                        <TableHead className="px-3 py-2 text-left">Destinatario</TableHead>
                        <TableHead className="px-3 py-2 text-left">Estado</TableHead>
                        <TableHead className="px-3 py-2 text-right tabular-nums">
                          Intentos
                        </TableHead>
                        <TableHead className="px-3 py-2 text-left">Enviado</TableHead>
                        <TableHead className="px-3 py-2 text-left">Último error</TableHead>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {(envios.data ?? []).map((e) => (
                        <TableRow key={e.id} className="border-t border-line">
                          <TableCell className="px-3 py-2">{e.destinatario}</TableCell>
                          <TableCell className="px-3 py-2"><Badge variant={e.estado === 'Enviado' ? 'success' : e.estado === 'Fallido' ? 'danger' : 'warning'}>{e.estado === 'Enviado' ? 'Enviado' : e.estado === 'Fallido' ? 'Fallido' : 'Pendiente'}</Badge></TableCell>
                          <TableCell className="px-3 py-2 text-right tabular-nums">
                            {e.intentos}
                          </TableCell>
                          <TableCell className="px-3 py-2 text-xs text-ink-muted">
                            <DateTimeDisplay value={e.enviadoAt} variant="datetime" />
                          </TableCell>
                          <TableCell className="px-3 py-2 text-xs text-danger-fg">
                            {e.ultimoError ? 'No se pudo entregar el correo. Revisa el destinatario y solicita el reenvío.' : <span className="text-ink-muted">—</span>}
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </div>
              )}
            </section>
          </div>
        </section>
      </div>
    </div>
  );
}

function ReenviarCorreo({ facturaId }: { facturaId: string }) {
  const [destinatario, setDestinatario] = useState('');
  const idempotencyKey = useFormIdempotencyKey();
  const reenviar = useReenviarCorreo();

  function enviar() {
    const email = destinatario.trim();
    if (email.length === 0) {
      toast.error('Captura el correo del destinatario.');
      return;
    }
    reenviar.mutate(
      { id: facturaId, destinatario: email, idempotencyKey },
      {
        onSuccess: () => {
          toast.success('Correo pendiente de envío.');
          setDestinatario('');
        },
        onError: (error) => {
          if (mostrarErrorReceptorFiscal(error)) return;
          toast.error('No se pudo reenviar. Revisa tu conexión y vuelve a intentarlo.');
        },
      },
    );
  }

  return (
    <div className="flex flex-wrap items-end gap-2 rounded-md border border-dashed p-3 border-line">
      <div className="flex-1 space-y-1">
        <label className="text-xs text-ink-muted" htmlFor="reenviar-email">
          Reenviar CFDI por correo
        </label>
        <Input
          id="reenviar-email"
          type="email"
          placeholder="cliente@dominio.com"
          value={destinatario}
          onChange={(e) => setDestinatario(e.target.value)}
        />
      </div>
      <Button type="button" variant="outline" onClick={enviar} disabled={reenviar.isPending}>
        <Mail className="mr-1 h-4 w-4" />
        {reenviar.isPending ? 'Enviando…' : 'Reenviar'}
      </Button>
    </div>
  );
}

function PedimentoForm({ facturaId, motivo }: { facturaId: string; motivo?: string | null }) {
  const [pedimento, setPedimento] = useState('');
  const [fecha, setFecha] = useState('');
  const [identificacion, setIdentificacion] = useState('');
  const idempotencyKey = useFormIdempotencyKey();
  const aplicar = useAplicarPedimento();

  function enviar() {
    if (pedimento.trim() === '') {
      toast.error('Captura el número de pedimento.');
      return;
    }
    aplicar.mutate(
      {
        id: facturaId,
        command: {
          pedimento: pedimento.trim(),
          fechaDocAduanero: fecha.trim() || null,
          identificacionMercancia: identificacion.trim() || null,
        },
        idempotencyKey,
      },
      {
        onSuccess: (res) => {
          toast.success(`Pedimento aplicado · ${res.estado}.`);
        },
        onError: (error) => {
          if (mostrarErrorReceptorFiscal(error)) return;
          // El aviso (ErrorTimbradoAviso) muestra el motivo en lenguaje claro; sin toast técnico duplicado.
        },
      },
    );
  }

  return (
    <div
      className="grid grid-cols-1 gap-2 rounded-md border border-line bg-warning-note-bg p-3 @xl:grid-cols-2 @4xl:grid-cols-4"
      data-print="hidden"
    >
      <div className="col-span-full text-sm font-medium">
        Factura retenida por pedimento — aplícalo para timbrar
      </div>
      {motivo && (
        <p className="col-span-full text-xs text-ink-muted" data-testid="motivo-pedimento">
          {motivo}
        </p>
      )}
      <div className="col-span-full">
        <ErrorTimbradoAviso error={aplicar.error} />
      </div>
      <div className="@xl:col-span-2">
        <Label htmlFor="pedimento-numero" className="text-xs">Pedimento *</Label>
        <Input
          id="pedimento-numero" placeholder="15  47  3807  5000123"
          value={pedimento}
          onChange={(e) => setPedimento(e.target.value)}
        />
      </div>
      <div>
        <Label htmlFor="pedimento-fecha" className="text-xs">Fecha doc. aduanero</Label>
        <Input id="pedimento-fecha" type="date" value={fecha} onChange={(e) => setFecha(e.target.value)} />
      </div>
      <div>
        <Label htmlFor="pedimento-identificacion" className="text-xs">Identificación mercancía</Label>
        <Input id="pedimento-identificacion" value={identificacion} onChange={(e) => setIdentificacion(e.target.value)} />
      </div>
      <div className="col-span-full flex justify-end">
        <Button size="sm" onClick={enviar} disabled={aplicar.isPending}>
          {aplicar.isPending ? 'Aplicando…' : 'Aplicar pedimento y timbrar'}
        </Button>
      </div>
    </div>
  );
}

/**
 * F1-CXC-05 P2 (compensación): aplica el saldo de un anticipo del cliente a esta
 * factura ya timbrada. Emite la NC de amortización (relación 07) y CxC la
 * descuenta del saldo; el importe no pasa del saldo del anticipo ni de lo que
 * falta por cobrar.
 */
function AplicarAnticipoForm({
  facturaId,
  clienteId,
  moneda,
  porCobrar,
  onDone,
}: {
  facturaId: string;
  clienteId: string;
  moneda: string;
  porCobrar: number;
  onDone: () => void;
}) {
  const [anticipo, setAnticipo] = useState<AnticipoEstadoCuenta | null>(null);
  const [importe, setImporte] = useState('');
  const [comentarios, setComentarios] = useState<string | null>(null);
  const idempotencyKey = useFormIdempotencyKey();
  const aplicar = useAplicarAnticipoAFactura(facturaId);
  const tope = anticipo ? Math.min(anticipo.saldo, porCobrar) : porCobrar;

  function elegir(item: AnticipoEstadoCuenta | null) {
    setAnticipo(item);
    setImporte(item ? Math.min(item.saldo, porCobrar).toFixed(2) : '');
  }

  function enviar() {
    const monto = Number(importe);
    if (!anticipo) {
      toast.error('Selecciona el anticipo a aplicar.');
      return;
    }
    if (!(monto > 0) || monto > tope) {
      toast.error(`El importe debe ser mayor a 0 y no exceder ${formatMoney({ amount: tope, currency: moneda })}.`);
      return;
    }
    aplicar.mutate(
      { anticipoId: anticipo.anticipoId, importe: monto, comentarios, idempotencyKey },
      {
        onSuccess: (res) => {
          toast.success(`Anticipo aplicado con la NC ${res.folio} por ${formatMoney({ amount: res.importe, currency: moneda })}.`);
          onDone();
        },
        onError: () =>
          toast.error('No se pudo aplicar el anticipo. Revisa tu conexión y vuelve a intentarlo.'),
      },
    );
  }

  return (
    <div
      className="grid grid-cols-1 gap-2 rounded-md border border-dashed p-3 @xl:grid-cols-2 @4xl:grid-cols-4 border-line"
      data-print="hidden"
    >
      <div className="col-span-full text-sm font-medium">
        Aplicar anticipo a esta factura (NC de amortización, relación 07)
      </div>
      <div className="col-span-full">
        <ErrorTimbradoAviso error={aplicar.error} />
      </div>
      <div className="@xl:col-span-2">
        <Label className="text-xs">Anticipo *</Label>
        <AnticipoPicker clienteId={clienteId} value={anticipo?.anticipoId} onChange={elegir} />
      </div>
      <div>
        <Label className="text-xs" htmlFor="aplicar-anticipo-importe">
          Importe *
        </Label>
        <Input
          id="aplicar-anticipo-importe"
          type="number"
          step="0.01"
          min="0"
          value={importe}
          onChange={(e) => setImporte(e.target.value)}
        />
      </div>
      <p className="self-end text-xs text-ink-muted">Por cobrar: {formatMoney({ amount: porCobrar, currency: moneda })}</p>
      <div className="col-span-full">
        <Label className="text-xs" htmlFor="aplicar-anticipo-comentarios">
          Comentarios (se imprimen en la nota de crédito)
        </Label>
        <TextAreaField
          value={comentarios}
          onChange={setComentarios}
          minRows={2}
          maxLength={1000}
          textareaProps={{ id: 'aplicar-anticipo-comentarios' }}
        />
      </div>
      <div className="col-span-full flex justify-end gap-2">
        <Button variant="ghost" size="sm" onClick={onDone} disabled={aplicar.isPending}>
          Cancelar
        </Button>
        <Button size="sm" onClick={enviar} disabled={aplicar.isPending}>
          {aplicar.isPending ? 'Aplicando…' : 'Aplicar anticipo'}
        </Button>
      </div>
    </div>
  );
}

function NcBonificacionForm({
  facturaId,
  totalFactura,
  moneda,
  puedeReembolsar,
  onDone,
}: {
  facturaId: string;
  totalFactura: number;
  moneda: string;
  puedeReembolsar: boolean;
  onDone: () => void;
}) {
  const [montoTotal, setMontoTotal] = useState('');
  const [tasaIva, setTasaIva] = useState('0.16');
  const [descripcion, setDescripcion] = useState('');
  const [comentarios, setComentarios] = useState<string | null>(null);
  // F6.3: «Devolver dinero al cliente» con la cuenta destino; al timbrar, la solicitud llega a Tesorería.
  const [devolver, setDevolver] = useState(false);
  const [banco, setBanco] = useState('');
  const [clabe, setClabe] = useState('');
  const [titular, setTitular] = useState('');
  const idempotencyKey = useFormIdempotencyKey();
  const emitir = useEmitirNcBonificacion();

  function enviar() {
    const monto = Number(montoTotal);
    if (!(monto > 0) || monto > totalFactura) {
      toast.error('El monto debe ser mayor a 0 y no exceder el total de la factura.');
      return;
    }
    if (devolver && (!banco.trim() || !titular.trim() || !/^[0-9]{18}$/.test(clabe.trim()))) {
      toast.error('Para devolver el dinero captura banco, titular y una CLABE de 18 dígitos.');
      return;
    }
    emitir.mutate(
      {
        command: {
          facturaVentaId: facturaId,
          montoTotal: monto,
          tasaIva: tasaIva.trim() === '' ? null : Number(tasaIva),
          descripcion: descripcion.trim() || null,
          comentarios,
          reembolso: devolver
            ? { banco: banco.trim(), clabe: clabe.trim(), titular: titular.trim() }
            : null,
        },
        idempotencyKey,
      },
      {
        onSuccess: (res) => {
          toast.success(`NC ${res.folio} emitida por ${formatMoney({ amount: res.total, currency: moneda })}.`);
          onDone();
        },
        onError: (error) => {
          if (mostrarErrorReceptorFiscal(error)) return;
          toast.error('No se pudo emitir la NC. Revisa tu conexión y vuelve a intentarlo.');
        },
      },
    );
  }

  return (
    <div
      className="grid grid-cols-1 gap-2 rounded-md border border-dashed p-3 @xl:grid-cols-2 @4xl:grid-cols-4 border-line"
      data-print="hidden"
    >
      <div className="col-span-full text-sm font-medium">
        Nota de crédito por bonificación (relación 01)
      </div>
      <div className="col-span-full">
        <ErrorTimbradoAviso error={emitir.error} />
      </div>
      <div>
        <Label htmlFor="nc-monto" className="text-xs">Monto total *</Label>
        <Input
          type="number"
          step="0.01"
          min="0"
          id="nc-monto" value={montoTotal}
          onChange={(e) => setMontoTotal(e.target.value)}
        />
      </div>
      <div>
        <Label htmlFor="nc-tasa" className="text-xs">Tasa IVA</Label>
        <Input
          type="number"
          step="0.01"
          min="0"
          max="1"
          id="nc-tasa" value={tasaIva}
          onChange={(e) => setTasaIva(e.target.value)}
        />
      </div>
      <div className="@xl:col-span-2">
        <Label htmlFor="nc-descripcion" className="text-xs">Descripción</Label>
        <Input id="nc-descripcion" value={descripcion} onChange={(e) => setDescripcion(e.target.value)} />
      </div>
      <div className="col-span-full">
        <Label className="text-xs" htmlFor="nc-bonificacion-comentarios">
          Comentarios (se imprimen en la nota de crédito)
        </Label>
        <TextAreaField
          value={comentarios}
          onChange={setComentarios}
          minRows={2}
          maxLength={1000}
          textareaProps={{ id: 'nc-bonificacion-comentarios' }}
        />
      </div>
      {puedeReembolsar && (
        <div className="col-span-full flex items-center gap-2">
          <Checkbox
            id="nc-devolver-dinero"
            checked={devolver}
            onCheckedChange={(checked) => setDevolver(checked === true)}
          />
          <Label htmlFor="nc-devolver-dinero" className="text-sm">
            Devolver dinero al cliente (al timbrar se pide el reembolso a Tesorería)
          </Label>
        </div>
      )}
      {devolver && (
        <>
          <div>
            <Label className="text-xs" htmlFor="nc-reembolso-banco">Banco *</Label>
            <Input id="nc-reembolso-banco" value={banco} onChange={(e) => setBanco(e.target.value)} />
          </div>
          <div>
            <Label className="text-xs" htmlFor="nc-reembolso-clabe">CLABE *</Label>
            <Input
              id="nc-reembolso-clabe"
              inputMode="numeric"
              maxLength={18}
              value={clabe}
              onChange={(e) => setClabe(e.target.value.replace(/\D/g, ''))}
            />
          </div>
          <div className="@xl:col-span-2">
            <Label className="text-xs" htmlFor="nc-reembolso-titular">Titular *</Label>
            <Input id="nc-reembolso-titular" value={titular} onChange={(e) => setTitular(e.target.value)} />
          </div>
        </>
      )}
      <div className="col-span-full flex justify-end gap-2">
        <Button variant="ghost" size="sm" onClick={onDone} disabled={emitir.isPending}>
          Cancelar
        </Button>
        <Button size="sm" onClick={enviar} disabled={emitir.isPending}>
          {emitir.isPending ? 'Emitiendo…' : 'Emitir NC'}
        </Button>
      </div>
    </div>
  );
}

function Dato({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="min-w-0 space-y-0.5">
      <dt className="text-xs text-ink-muted">{label}</dt>
      <dd className="text-sm [overflow-wrap:anywhere]">{children}</dd>
    </div>
  );
}

/**
 * FAC-08b: «Sustituida por B» en la original y «Sustituye a A» en la sustituta (relación 04).
 * Si el Contador General no autorizó la sustitución, avisa que las dos siguen vigentes
 * hasta que se cancele la sustituta (supuesto a validar).
 */
function AvisoSustitucion({ f }: { f: NonNullable<ReturnType<typeof useComprobante>['data']> }) {
  const otra = f.sustituidaPor ?? f.sustituyeA;
  if (!otra) return null;
  const esOriginal = Boolean(f.sustituidaPor);
  return (
    <div className="space-y-2" data-testid="aviso-sustitucion">
      <p role="note" className="flex items-start gap-1.5 rounded-md bg-info-bg px-3 py-2 text-sm text-info-fg">
        <Info aria-hidden="true" className="mt-0.5 size-4 shrink-0" strokeWidth={1.6} />
        <span>
          {esOriginal ? 'Sustituida por ' : 'Sustituye a '}
          <Link to="/facturacion/facturas/$id" params={{ id: otra.id }} className="font-mono underline">
            {otra.folio}
          </Link>{' '}
          (relación 04 · {otra.estado === 'Timbrado' ? 'vigente' : otra.estado.toLowerCase()}).
        </span>
      </p>
      {f.sustitucionRechazadaMotivo && (
        <p
          role="note"
          className="flex items-start gap-1.5 rounded-md bg-warning-note-bg px-3 py-2.5 text-sm text-warning-note-fg"
        >
          <TriangleAlert aria-hidden="true" className="mt-0.5 size-4 shrink-0" strokeWidth={1.6} />
          <span>
            <span className="font-medium">Sustitución rechazada.</span> El Contador General no autorizó
            cancelar la original: «{f.sustitucionRechazadaMotivo}». El ERP pidió la cancelación de la
            sustituta con motivo 03; mientras no se cancele, las dos facturas están vigentes: no cobres dos veces.
          </span>
        </p>
      )}
    </div>
  );
}
