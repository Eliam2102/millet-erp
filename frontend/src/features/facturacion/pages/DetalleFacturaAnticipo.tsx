import { ErrorState } from '@/components/erp';
import { ResumenImportes } from '@/components/erp/display/ResumenImportes';
import { PageHeader } from '@/components/erp/PageHeader';
import { Button } from '@/components/ui/button';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import type { FacturaAnticipoDetalleResponse } from '@/features/facturacion/api/types';
import { useFacturaAnticipoDetalle } from '@/features/facturacion/api/useAnticipos';
import { useCancelacionEstatus } from '@/features/facturacion/api/useFacturas';
import { CancelarComprobanteForm } from '@/features/facturacion/components/CancelarComprobanteForm';
import { CobrarEnCajaLink } from '@/features/facturacion/components/CobrarEnCajaLink';
import { IntentosTimbradoPanel } from '@/features/facturacion/components/IntentosTimbradoPanel';
import { TimbradoFallidoBanner } from '@/features/facturacion/components/TimbradoFallidoBanner';
import { TrazabilidadFacturacion } from '@/features/facturacion/components/TrazabilidadFacturacion';
import { descargarArchivo } from '@/features/facturacion/lib/descargas';
import type { FacturasAnticipoSearch } from '@/features/facturacion/lib/facturas-anticipo-search-schema';
import { ETIQUETA_TIPO_RELACION } from '@/features/facturacion/lib/glosario';
import { ChipTimbrado } from '@/features/facturacion/pages/BandejaFacturas';
import { ChipEstadoAnticipo } from '@/features/facturacion/pages/BandejaFacturasAnticipo';
import { esApiError } from '@/lib/api';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { Link, useParams, useSearch } from '@tanstack/react-router';
import { Ban, Download, FileCode, Printer, X } from 'lucide-react';
import { useState } from 'react';
import { toast } from 'sonner';

const BASE = '/api/v1/facturacion/anticipos/facturas';
const NC_BASE = '/api/v1/facturacion/notas-credito';

/**
 * <c>Detalle de la factura de anticipo</c> (ANT-PR2, doc 13 §6 — patrón P3,
 * molde <c>DetalleFactura</c>). Cabecera fiscal + concepto único + saldo y
 * cadena del anticipo (13-A) + descargas XML/PDF (13-B) + cancelar (13-G) +
 * banner de reintento/descarte (#530/#535) + bitácora de intentos (13-I).
 */
export function DetalleFacturaAnticipo() {
  const { id } = useParams({ from: '/_app/facturacion/anticipos/facturas/$id' });
  const query = useFacturaAnticipoDetalle(id);

  return (
    <div className="space-y-5">
      {query.isError ? (
        <ErrorState
          title="No se pudo cargar la factura de anticipo"
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

function Contenido({ f }: { f: FacturaAnticipoDetalleResponse }) {
  const search = useSearch({ strict: false }) as FacturasAnticipoSearch;
  const puedeCancelar = useHasPermission(PermisosCanonicos.FacturacionCancelacionesSolicitar);
  // 13-I: la bitácora de intentos usa el permiso del endpoint (facturas.leer).
  const puedeVerIntentos = useHasPermission(PermisosCanonicos.FacturacionFacturasLeer);
  const puedeVerNc = useHasPermission(PermisosCanonicos.FacturacionNotasCreditoLeer);
  const cancelacion = useCancelacionEstatus(f.id);
  const [cancelando, setCancelando] = useState(false);
  const timbrada = f.estado === 'Timbrado' || f.estado === 'Cancelado';
  const vigente = f.estado === 'Timbrado';

  async function descargar(tipo: 'xml' | 'pdf') {
    try {
      await descargarArchivo(`${BASE}/${f.id}/${tipo}`, `${f.folio}.${tipo}`);
    } catch (error) {
      toast.error(esApiError(error) ? error.problem.title : 'No se pudo descargar el archivo.');
    }
  }

  async function descargarNc(ncId: string, folio: string, tipo: 'xml' | 'pdf') {
    try {
      await descargarArchivo(`${NC_BASE}/${ncId}/${tipo}`, `${folio}.${tipo}`);
    } catch (error) {
      toast.error(esApiError(error) ? error.problem.title : 'No se pudo descargar el archivo.');
    }
  }

  return (
    <div className="space-y-5">
      {/* ── Sub-topbar de acciones (§6.5, no se imprime) ─────────── */}
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
                  <Button variant="outline" size="sm" onClick={() => window.print()}>
                    <Printer className="mr-1 h-4 w-4" />
                    Imprimir
                  </Button>
                  {puedeCancelar && vigente && (
                    <Button
                      variant="outline"
                      size="sm"
                      className="text-danger-fg"
                      onClick={() => setCancelando((v) => !v)}
                    >
                      <Ban className="mr-1 h-4 w-4" />
                      Cancelar
                    </Button>
                  )}
                </div>
                <Button variant="ghost" size="sm" asChild aria-label="Cerrar detalle">
                  <Link to="/facturacion/anticipos/facturas" search={search}>
                    <X className="h-4 w-4" />
                  </Link>
                </Button>
              </div>
            }
          />
          <span className="text-xs text-ink-muted">{f.tipoAnticipo}</span>
        </div>
        {f.uuid && <p className="font-mono text-xs text-ink-muted">UUID {f.uuid}</p>}
      </div>

      {cancelando && (
        <CancelarComprobanteForm comprobanteId={f.id} onDone={() => setCancelando(false)} />
      )}

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
          <section className="space-y-3">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Partida</TableHead>
                  <TableHead>Clave SAT</TableHead>
                  <TableHead className="text-right">Importe</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow>
                  <TableCell>{f.descripcion}</TableCell>
                  <TableCell className="font-mono">84111506 · E48</TableCell>
                  <TableCell className="text-right tabular-nums">{f.subtotal.toFixed(2)}</TableCell>
                </TableRow>
              </TableBody>
            </Table>
            <ResumenImportes
              moneda={f.moneda}
              subtotal={f.subtotal}
              ivaTrasladado={f.impuestosTrasladados}
              total={f.total}
            />
          </section>
        </section>

        <section className="space-y-4">
          <div className="space-y-5">
            {/* ── Saldo amortizable (13-A) ─────────────────────────── */}
            {f.anticipo != null && (
              <section className="space-y-3">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <h2 className="text-sm font-medium">Saldo del anticipo</h2>
                  <div className="flex items-center gap-2" data-print="hidden">
                    <ChipEstadoAnticipo estado={f.anticipo.estado} />
                    <Link
                      to="/facturacion/anticipos/$clienteId"
                      params={{ clienteId: f.anticipo.clienteId }}
                      className="text-sm font-medium text-brand hover:underline"
                    >
                      Estado de cuenta del cliente
                    </Link>
                  </div>
                </div>
                <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
                  <Kpi label="Cobrado" valor={f.anticipo.montoCobrado} moneda={f.moneda} />
                  <Kpi label="Amortizado" valor={f.anticipo.montoAmortizado} moneda={f.moneda} />
                  <Kpi label="Saldo" valor={f.anticipo.saldo} moneda={f.moneda} />
                  <Kpi label="Disponible" valor={f.anticipo.saldoDisponible} moneda={f.moneda} />
                </div>
                {f.anticipo.pedidoOrigenRef && (
                  <p className="text-xs text-ink-muted">
                    Pedido de origen: {f.anticipo.pedidoOrigenRef}
                    {f.anticipo.obraNombre ? ` · Obra: ${f.anticipo.obraNombre}` : ''}
                  </p>
                )}

                {/* ── Vinculaciones: factura final + NC (13-A) ──────── */}
                <h3 className="text-sm font-medium">
                  Aplicaciones ({f.anticipo.vinculaciones.length})
                </h3>
                {f.anticipo.vinculaciones.length === 0 ? (
                  <p className="rounded-md border border-dashed px-3 py-3 text-center text-xs text-ink-muted border-line">
                    Este anticipo aún no se aplica a ninguna factura.
                  </p>
                ) : (
                  <div className="overflow-x-auto rounded-md border border-line">
                    <Table className="w-full text-sm">
                      <TableHeader className="bg-surface-muted/50">
                        <TableRow>
                          <TableHead className="px-3 py-2 text-left">Factura final</TableHead>
                          <TableHead className="px-3 py-2 text-right tabular-nums">
                            Importe
                          </TableHead>
                          <TableHead className="px-3 py-2 text-left">NC de amortización</TableHead>
                          <TableHead
                            className="px-3 py-2 text-right tabular-nums"
                            data-print="hidden"
                          >
                            NC archivos
                          </TableHead>
                        </TableRow>
                      </TableHeader>
                      <TableBody>
                        {f.anticipo.vinculaciones.map((v) => (
                          <TableRow key={v.facturaVentaId} className="border-t border-line">
                            <TableCell className="px-3 py-2">
                              <Link
                                to="/facturacion/facturas/$id"
                                params={{ id: v.facturaVentaId }}
                                className="font-mono text-xs font-medium text-brand hover:underline"
                              >
                                {v.facturaFolio ?? v.facturaVentaId.slice(0, 8)}
                              </Link>
                              {v.facturaEstado && (
                                <span className="ml-2 text-xs text-ink-muted">
                                  {v.facturaEstado}
                                </span>
                              )}
                            </TableCell>
                            <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                              {v.importe.toFixed(2)}
                            </TableCell>
                            <TableCell className="px-3 py-2">
                              {v.ncFolio ? (
                                <>
                                  <span className="font-mono text-xs">{v.ncFolio}</span>
                                  {v.ncEstado && (
                                    <span className="ml-2 text-xs text-ink-muted">
                                      {v.ncEstado}
                                    </span>
                                  )}
                                </>
                              ) : (
                                <span className="text-xs text-ink-muted">
                                  Pendiente (compromiso M2)
                                </span>
                              )}
                            </TableCell>
                            <TableCell
                              className="px-3 py-2 text-right tabular-nums"
                              data-print="hidden"
                            >
                              {puedeVerNc && v.ncAmortizacionId != null && v.ncFolio != null ? (
                                <span className="inline-flex gap-1">
                                  <Button
                                    variant="ghost"
                                    size="sm"
                                    onClick={() =>
                                      descargarNc(v.ncAmortizacionId!, v.ncFolio!, 'xml')
                                    }
                                  >
                                    XML
                                  </Button>
                                  <Button
                                    variant="ghost"
                                    size="sm"
                                    onClick={() =>
                                      descargarNc(v.ncAmortizacionId!, v.ncFolio!, 'pdf')
                                    }
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
                )}
              </section>
            )}

            {/* ── Cadena de relaciones CFDI ────────────────────────── */}
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

            {/* ── Trazabilidad documento-céntrica (ANT-PR3, doc 13) ── */}
            <TrazabilidadFacturacion raiz="comprobante" id={f.id} />

            {/* ── Historial de intentos de timbrado (13-I) ─────────── */}
            {puedeVerIntentos && <IntentosTimbradoPanel comprobanteId={f.id} />}
          </div>
        </section>
      </div>
    </div>
  );
}

function Kpi({ label, valor, moneda }: { label: string; valor: number; moneda: string }) {
  return (
    <div className="rounded-md border p-3 border-line">
      <div className="text-xs text-ink-muted">{label}</div>
      <div className="font-mono text-lg font-semibold tabular-nums">
        {valor.toFixed(2)} <span className="text-xs font-normal">{moneda}</span>
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
