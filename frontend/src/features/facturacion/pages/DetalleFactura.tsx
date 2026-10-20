import { ErrorState } from '@/components/erp';
import { ResumenImportes } from '@/components/erp/display/ResumenImportes';
import { PageHeader } from '@/components/erp/PageHeader';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
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
import { ErrorTimbradoAviso } from '@/features/facturacion/components/ErrorTimbradoAviso';
import { IntentosTimbradoPanel } from '@/features/facturacion/components/IntentosTimbradoPanel';
import { SolicitarReembolsoNcDialog } from '@/features/facturacion/components/SolicitarReembolsoNcDialog';
import { TimbradoFallidoBanner } from '@/features/facturacion/components/TimbradoFallidoBanner';
import { TrazabilidadFacturacion } from '@/features/facturacion/components/TrazabilidadFacturacion';
import { descargarArchivo } from '@/features/facturacion/lib/descargas';
import type { FacturasSearch } from '@/features/facturacion/lib/facturas-search-schema';
import { ETIQUETA_MOTIVO_NC, ETIQUETA_TIPO_RELACION } from '@/features/facturacion/lib/glosario';
import { mostrarErrorReceptorFiscal } from '@/features/facturacion/lib/mostrar-error-receptor-fiscal';
import { ChipTimbrado } from '@/features/facturacion/pages/BandejaFacturas';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { Link, useParams, useSearch } from '@tanstack/react-router';
import { Ban, Download, FileCode, Mail, Printer, Receipt, X } from 'lucide-react';
import { useState } from 'react';
import { toast } from 'sonner';

const BASE = '/api/v1/facturacion/facturas';

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
    <div className="space-y-5">
      {query.isError ? (
        <ErrorState
          title="No se pudo cargar la factura"
          problem={esApiError(query.error) ? query.error.problem : undefined}
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
  const [reembolsoNc, setReembolsoNc] = useState<
    NonNullable<typeof f.notasCreditoAplicadas>[number] | null
  >(null);
  const cancelacion = useCancelacionEstatus(f.id);
  const [accion, setAccion] = useState<'cancelar' | 'nc' | null>(null);
  const timbrada = f.estado === 'Timbrado' || f.estado === 'Cancelado';
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
    } catch (error) {
      toast.error(esApiError(error) ? error.problem.title : 'No se pudo descargar el archivo.');
    }
  }

  return (
    <div className="space-y-5">
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
                    disabled={!timbrada}
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
                  <Button variant="outline" size="sm" onClick={() => window.print()}>
                    <Printer className="mr-1 h-4 w-4" />
                    Imprimir
                  </Button>
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
                  {puedeCancelar && vigente && (
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

      {accion === 'nc' && (
        <NcBonificacionForm
          facturaId={f.id}
          totalFactura={f.total}
          onDone={() => setAccion(null)}
        />
      )}
      {accion === 'cancelar' && (
        <CancelarComprobanteForm comprobanteId={f.id} onDone={() => setAccion(null)} />
      )}

      {pendientePedimento && puedeEmitir && <PedimentoForm facturaId={f.id} />}

      <TimbradoFallidoBanner
        comprobanteId={f.id}
        estado={f.estado}
        errorCodigo={f.timbradoErrorCodigo}
        errorMensaje={f.timbradoErrorMensaje}
      />

      {cancelacion.data?.estadoSolicitud != null && (
        <div
          className="rounded-md border border-line bg-warning-note-bg px-3 py-2 text-sm"
          data-print="hidden"
        >
          <span className="font-medium">Solicitud de cancelación:</span>{' '}
          {cancelacion.data.estadoSolicitud}
          {cancelacion.data.motivoSat ? ` · motivo ${cancelacion.data.motivoSat}` : ''}
          {cancelacion.data.estatusSat ? ` · SAT ${cancelacion.data.estatusSat}` : ''}
          {cancelacion.data.mensajeError ? (
            <span className="text-danger-fg"> · {cancelacion.data.mensajeError}</span>
          ) : null}
        </div>
      )}

      <div className="space-y-5">
        <section className="space-y-4">
          <dl className="grid grid-cols-1 gap-4 rounded-lg bg-surface-card p-4 shadow-card-flat sm:grid-cols-4">
            <Dato label="Receptor">
              <div>{f.receptorNombre}</div>
              <div className="font-mono text-xs text-ink-muted">{f.receptorRfc}</div>
            </Dato>
            <Dato label="Moneda">{f.moneda}</Dato>
            <Dato label="Método de pago">{f.metodoPago ?? <span className="text-ink-muted">Sin método de pago</span>}</Dato>
            <Dato label="Fecha de timbrado">
              {f.fechaTimbrado
                ? new Date(f.fechaTimbrado).toLocaleString('es-MX', {
                    timeZone: 'America/Mexico_City',
                  })
                : '—'}
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
                      <TableCell className="px-3 py-2 text-ink-muted">{l.posicion}</TableCell>
                      <TableCell className="px-3 py-2">{l.descripcion}</TableCell>
                      <TableCell className="px-3 py-2 font-mono text-xs">
                        {l.claveProdServSat} · {l.claveUnidadSat}
                      </TableCell>
                      <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                        {l.cantidad.toFixed(2)}
                      </TableCell>
                      <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                        {l.valorUnitario.toFixed(2)}
                      </TableCell>
                      <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                        {l.descuento.toFixed(2)}
                      </TableCell>
                      <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                        {l.importe.toFixed(2)}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
          </section>
        </section>

        <section className="space-y-4">
          <div className="space-y-5">
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
                            {nc.uuid ?? '—'}
                          </TableCell>
                          <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                            {nc.total.toFixed(2)}
                          </TableCell>
                          <TableCell className="px-3 py-2">
                            {nc.reembolsoSolicitado ? (
                              <Badge variant="info">Solicitado</Badge>
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
                            {r.folio ?? '—'}
                          </TableCell>
                          <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                            {r.total != null ? r.total.toFixed(2) : '—'}
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
                          <TableCell className="px-3 py-2">{e.estado}</TableCell>
                          <TableCell className="px-3 py-2 text-right tabular-nums">
                            {e.intentos}
                          </TableCell>
                          <TableCell className="px-3 py-2 text-xs text-ink-muted">
                            {e.enviadoAt ? new Date(e.enviadoAt).toLocaleString('es-MX') : '—'}
                          </TableCell>
                          <TableCell className="px-3 py-2 text-xs text-danger-fg">
                            {e.ultimoError ?? '—'}
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
          toast.success('Reenvío encolado.');
          setDestinatario('');
        },
        onError: (error) => {
          if (mostrarErrorReceptorFiscal(error)) return;
          toast.error(esApiError(error) ? error.problem.title : 'No se pudo reenviar.');
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

function PedimentoForm({ facturaId }: { facturaId: string }) {
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
      className="grid grid-cols-1 gap-2 rounded-md border border-line bg-warning-note-bg p-3 sm:grid-cols-4"
      data-print="hidden"
    >
      <div className="sm:col-span-4 text-sm font-medium">
        Factura retenida por pedimento — aplícalo para timbrar
      </div>
      <div className="sm:col-span-4">
        <ErrorTimbradoAviso error={aplicar.error} />
      </div>
      <div className="sm:col-span-2">
        <Label className="text-xs">Pedimento *</Label>
        <Input
          placeholder="15  47  3807  5000123"
          value={pedimento}
          onChange={(e) => setPedimento(e.target.value)}
        />
      </div>
      <div>
        <Label className="text-xs">Fecha doc. aduanero</Label>
        <Input type="date" value={fecha} onChange={(e) => setFecha(e.target.value)} />
      </div>
      <div>
        <Label className="text-xs">Identificación mercancía</Label>
        <Input value={identificacion} onChange={(e) => setIdentificacion(e.target.value)} />
      </div>
      <div className="sm:col-span-4 flex justify-end">
        <Button size="sm" onClick={enviar} disabled={aplicar.isPending}>
          {aplicar.isPending ? 'Aplicando…' : 'Aplicar pedimento y timbrar'}
        </Button>
      </div>
    </div>
  );
}

function NcBonificacionForm({
  facturaId,
  totalFactura,
  onDone,
}: {
  facturaId: string;
  totalFactura: number;
  onDone: () => void;
}) {
  const [montoTotal, setMontoTotal] = useState('');
  const [tasaIva, setTasaIva] = useState('0.16');
  const [descripcion, setDescripcion] = useState('');
  const idempotencyKey = useFormIdempotencyKey();
  const emitir = useEmitirNcBonificacion();

  function enviar() {
    const monto = Number(montoTotal);
    if (!(monto > 0) || monto > totalFactura) {
      toast.error('El monto debe ser mayor a 0 y no exceder el total de la factura.');
      return;
    }
    emitir.mutate(
      {
        command: {
          facturaVentaId: facturaId,
          montoTotal: monto,
          tasaIva: tasaIva.trim() === '' ? null : Number(tasaIva),
          descripcion: descripcion.trim() || null,
        },
        idempotencyKey,
      },
      {
        onSuccess: (res) => {
          toast.success(`NC ${res.folio} emitida por ${res.total.toFixed(2)}.`);
          onDone();
        },
        onError: (error) => {
          if (mostrarErrorReceptorFiscal(error)) return;
          toast.error(esApiError(error) ? error.problem.title : 'No se pudo emitir la NC.');
        },
      },
    );
  }

  return (
    <div
      className="grid grid-cols-1 gap-2 rounded-md border border-dashed p-3 sm:grid-cols-4 border-line"
      data-print="hidden"
    >
      <div className="sm:col-span-4 text-sm font-medium">
        Nota de crédito por bonificación (relación 01)
      </div>
      <div className="sm:col-span-4">
        <ErrorTimbradoAviso error={emitir.error} />
      </div>
      <div>
        <Label className="text-xs">Monto total *</Label>
        <Input
          type="number"
          step="0.01"
          min="0"
          value={montoTotal}
          onChange={(e) => setMontoTotal(e.target.value)}
        />
      </div>
      <div>
        <Label className="text-xs">Tasa IVA</Label>
        <Input
          type="number"
          step="0.01"
          min="0"
          max="1"
          value={tasaIva}
          onChange={(e) => setTasaIva(e.target.value)}
        />
      </div>
      <div className="sm:col-span-2">
        <Label className="text-xs">Descripción</Label>
        <Input value={descripcion} onChange={(e) => setDescripcion(e.target.value)} />
      </div>
      <div className="sm:col-span-4 flex justify-end gap-2">
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
    <div className="space-y-0.5">
      <dt className="text-xs text-ink-muted">{label}</dt>
      <dd className="text-sm">{children}</dd>
    </div>
  );
}
