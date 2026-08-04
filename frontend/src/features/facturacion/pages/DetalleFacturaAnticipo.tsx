import { useState } from 'react';
import { Link, useParams, useSearch } from '@tanstack/react-router';
import { Ban, Download, FileCode, Printer, X } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from '@/components/ui/tabs';
import { ErrorState } from '@/components/erp';
import { CancelarComprobanteForm } from '@/features/facturacion/components/CancelarComprobanteForm';
import { TimbradoFallidoBanner } from '@/features/facturacion/components/TimbradoFallidoBanner';
import { IntentosTimbradoPanel } from '@/features/facturacion/components/IntentosTimbradoPanel';
import { TrazabilidadFacturacion } from '@/features/facturacion/components/TrazabilidadFacturacion';
import { useFacturaAnticipoDetalle } from '@/features/facturacion/api/useAnticipos';
import { useCancelacionEstatus } from '@/features/facturacion/api/useFacturas';
import { descargarArchivo } from '@/features/facturacion/lib/descargas';
import { ETIQUETA_TIPO_RELACION } from '@/features/facturacion/lib/glosario';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { ChipTimbrado } from '@/features/facturacion/pages/BandejaFacturas';
import { ChipEstadoAnticipo } from '@/features/facturacion/pages/BandejaFacturasAnticipo';
import type { FacturasAnticipoSearch } from '@/features/facturacion/lib/facturas-anticipo-search-schema';
import type { FacturaAnticipoDetalleResponse } from '@/features/facturacion/api/types';

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
    <div className="space-y-4">
      {query.isError ? (
        <ErrorState
          title="No se pudo cargar la factura de anticipo"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading || query.data == null ? (
        <div className="space-y-3">
          <div className="h-8 w-64 animate-pulse rounded bg-muted" />
          <div className="h-40 w-full animate-pulse rounded bg-muted" />
        </div>
      ) : (
        <Contenido f={query.data} />
      )}
    </div>
  );
}

function Contenido({ f }: { f: FacturaAnticipoDetalleResponse }) {
  const search = useSearch({ strict: false }) as FacturasAnticipoSearch;
  const puedeCancelar = useHasPermission(
    PermisosCanonicos.FacturacionCancelacionesSolicitar,
  );
  // 13-I: la bitácora de intentos usa el permiso del endpoint (facturas.leer).
  const puedeVerIntentos = useHasPermission(PermisosCanonicos.FacturacionFacturasLeer);
  const puedeVerNc = useHasPermission(PermisosCanonicos.FacturacionNotasCreditoLeer);
  const cancelacion = useCancelacionEstatus(f.id);
  const [cancelando, setCancelando] = useState(false);
  const timbrada = f.estado === 'Timbrado' || f.estado === 'Cancelado';
  const vigente = f.estado === 'Timbrado';

  async function descargar(tipo: 'xml' | 'pdf') {
    try {
      await descargarArchivo(
        `${BASE}/${f.id}/${tipo}`,
        `${f.folio}.${tipo}`,
      );
    } catch (error) {
      toast.error(
        esApiError(error) ? error.problem.title : 'No se pudo descargar el archivo.',
      );
    }
  }

  async function descargarNc(ncId: string, folio: string, tipo: 'xml' | 'pdf') {
    try {
      await descargarArchivo(`${NC_BASE}/${ncId}/${tipo}`, `${folio}.${tipo}`);
    } catch (error) {
      toast.error(
        esApiError(error) ? error.problem.title : 'No se pudo descargar el archivo.',
      );
    }
  }

  return (
    <div className="space-y-6">
      {/* ── Sub-topbar de acciones (§6.5, no se imprime) ─────────── */}
      <div
        className="sticky top-0 z-10 flex flex-wrap items-center justify-between gap-2 border-b bg-background/95 pb-2 backdrop-blur"
        data-print="hidden"
      >
        <div className="flex flex-wrap items-center gap-2">
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
              className="text-destructive"
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
          className="rounded-md border border-amber-300 bg-amber-50/60 px-3 py-2 text-sm"
          data-print="hidden"
        >
          <span className="font-medium">Solicitud de cancelación:</span>{' '}
          {cancelacion.data.estadoSolicitud}
          {cancelacion.data.motivoSat ? ` · motivo ${cancelacion.data.motivoSat}` : ''}
          {cancelacion.data.estatusSat ? ` · SAT ${cancelacion.data.estatusSat}` : ''}
          {cancelacion.data.mensajeError ? (
            <span className="text-destructive"> · {cancelacion.data.mensajeError}</span>
          ) : null}
        </div>
      )}

      <header className="space-y-1">
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="font-mono text-2xl font-semibold">{f.folio}</h1>
          <ChipTimbrado estado={f.estado} />
          <span className="text-xs text-muted-foreground">{f.tipoAnticipo}</span>
        </div>
        {f.uuid && (
          <p className="font-mono text-xs text-muted-foreground">UUID {f.uuid}</p>
        )}
      </header>

      <Tabs defaultValue="encabezado">
        <TabsList data-print="hidden">
          <TabsTrigger value="encabezado">Encabezado</TabsTrigger>
          <TabsTrigger value="concepto">Concepto</TabsTrigger>
          <TabsTrigger value="saldo">Saldo y cadena</TabsTrigger>
        </TabsList>

        <TabsContent value="encabezado" forceMount className="print:!block">
          <section className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <Dato label="Receptor">
              <div>{f.receptorNombre}</div>
              <div className="font-mono text-xs text-muted-foreground">
                {f.receptorRfc}
              </div>
            </Dato>
            <Dato label="Moneda">{f.moneda}</Dato>
            <Dato label="Fecha de timbrado">
              {f.fechaTimbrado ? new Date(f.fechaTimbrado).toLocaleString('es-MX') : '—'}
            </Dato>
            <Dato label="Versión">{f.version}</Dato>
          </section>
        </TabsContent>

        <TabsContent value="concepto" forceMount className="print:!block">
          <section className="space-y-3">
            <div className="rounded-md border p-3 text-sm">
              <div className="font-medium">{f.descripcion}</div>
              <div className="mt-1 text-xs text-muted-foreground">
                Concepto único de anticipo (clave SAT 84111506 · E48)
              </div>
            </div>
            <section className="ml-auto w-full max-w-xs space-y-1 text-sm tabular-nums">
              <Renglon label="Subtotal" valor={f.subtotal} />
              <Renglon label="IVA trasladado" valor={f.impuestosTrasladados} />
              <div className="flex justify-between border-t pt-1 font-semibold">
                <span>Total</span>
                <span className="font-mono">
                  {f.total.toFixed(2)} {f.moneda}
                </span>
              </div>
            </section>
          </section>
        </TabsContent>

        <TabsContent value="saldo" forceMount className="print:!block">
          <div className="space-y-6">
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
                      className="text-sm font-medium text-primary hover:underline"
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
                  <p className="text-xs text-muted-foreground">
                    Pedido de origen: {f.anticipo.pedidoOrigenRef}
                    {f.anticipo.obraNombre ? ` · Obra: ${f.anticipo.obraNombre}` : ''}
                  </p>
                )}

                {/* ── Vinculaciones: factura final + NC (13-A) ──────── */}
                <h3 className="text-sm font-medium">
                  Aplicaciones ({f.anticipo.vinculaciones.length})
                </h3>
                {f.anticipo.vinculaciones.length === 0 ? (
                  <p className="rounded-md border border-dashed px-3 py-3 text-center text-xs text-muted-foreground">
                    Este anticipo aún no se aplica a ninguna factura.
                  </p>
                ) : (
                  <div className="overflow-x-auto rounded-md border">
                    <table className="w-full text-sm">
                      <thead className="bg-muted/50">
                        <tr>
                          <th className="px-3 py-2 text-left">Factura final</th>
                          <th className="px-3 py-2 text-right">Importe</th>
                          <th className="px-3 py-2 text-left">NC de amortización</th>
                          <th className="px-3 py-2 text-right" data-print="hidden">
                            NC archivos
                          </th>
                        </tr>
                      </thead>
                      <tbody>
                        {f.anticipo.vinculaciones.map((v) => (
                          <tr key={v.facturaVentaId} className="border-t">
                            <td className="px-3 py-2">
                              <Link
                                to="/facturacion/facturas/$id"
                                params={{ id: v.facturaVentaId }}
                                className="font-mono text-xs font-medium text-primary hover:underline"
                              >
                                {v.facturaFolio ?? v.facturaVentaId.slice(0, 8)}
                              </Link>
                              {v.facturaEstado && (
                                <span className="ml-2 text-xs text-muted-foreground">
                                  {v.facturaEstado}
                                </span>
                              )}
                            </td>
                            <td className="px-3 py-2 text-right font-mono">
                              {v.importe.toFixed(2)}
                            </td>
                            <td className="px-3 py-2">
                              {v.ncFolio ? (
                                <>
                                  <span className="font-mono text-xs">{v.ncFolio}</span>
                                  {v.ncEstado && (
                                    <span className="ml-2 text-xs text-muted-foreground">
                                      {v.ncEstado}
                                    </span>
                                  )}
                                </>
                              ) : (
                                <span className="text-xs text-muted-foreground">
                                  Pendiente (compromiso M2)
                                </span>
                              )}
                            </td>
                            <td className="px-3 py-2 text-right" data-print="hidden">
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
                                <span className="text-xs text-muted-foreground">—</span>
                              )}
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
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
                <div className="overflow-x-auto rounded-md border">
                  <table className="w-full text-sm">
                    <thead className="bg-muted/50">
                      <tr>
                        <th className="px-3 py-2 text-left">Relación</th>
                        <th className="px-3 py-2 text-left">UUID relacionado</th>
                        <th className="px-3 py-2 text-left">Folio</th>
                        <th className="px-3 py-2 text-right">Total</th>
                      </tr>
                    </thead>
                    <tbody>
                      {f.relaciones.map((r) => (
                        <tr key={r.uuidRelacionado} className="border-t">
                          <td className="px-3 py-2">
                            {ETIQUETA_TIPO_RELACION[r.tipoRelacion] ?? r.tipoRelacion}
                          </td>
                          <td className="px-3 py-2 font-mono text-xs">
                            {r.uuidRelacionado}
                          </td>
                          <td className="px-3 py-2 font-mono text-xs">{r.folio ?? '—'}</td>
                          <td className="px-3 py-2 text-right font-mono">
                            {r.total != null ? r.total.toFixed(2) : '—'}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </section>
            )}

            {/* ── Trazabilidad documento-céntrica (ANT-PR3, doc 13) ── */}
            <TrazabilidadFacturacion raiz="comprobante" id={f.id} />

            {/* ── Historial de intentos de timbrado (13-I) ─────────── */}
            {puedeVerIntentos && <IntentosTimbradoPanel comprobanteId={f.id} />}
          </div>
        </TabsContent>
      </Tabs>
    </div>
  );
}

function Kpi({ label, valor, moneda }: { label: string; valor: number; moneda: string }) {
  return (
    <div className="rounded-md border p-3">
      <div className="text-xs text-muted-foreground">{label}</div>
      <div className="font-mono text-lg font-semibold tabular-nums">
        {valor.toFixed(2)} <span className="text-xs font-normal">{moneda}</span>
      </div>
    </div>
  );
}

function Dato({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="space-y-0.5">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="text-sm">{children}</dd>
    </div>
  );
}

function Renglon({ label, valor }: { label: string; valor: number }) {
  return (
    <div className="flex justify-between">
      <span className="text-muted-foreground">{label}</span>
      <span className="font-mono">{valor.toFixed(2)}</span>
    </div>
  );
}
