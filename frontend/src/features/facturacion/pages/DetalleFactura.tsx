import { useState } from 'react';
import { Link, useParams, useSearch } from '@tanstack/react-router';
import { Ban, Download, FileCode, Mail, Printer, Receipt, X } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { CancelarComprobanteForm } from '@/features/facturacion/components/CancelarComprobanteForm';
import { RegistrarCobroCard } from '@/features/facturacion/components/RegistrarCobroCard';
import { TimbradoFallidoBanner } from '@/features/facturacion/components/TimbradoFallidoBanner';
import { IntentosTimbradoPanel } from '@/features/facturacion/components/IntentosTimbradoPanel';
import { TrazabilidadFacturacion } from '@/features/facturacion/components/TrazabilidadFacturacion';
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from '@/components/ui/tabs';
import { ErrorState } from '@/components/erp';
import {
  useComprobante,
  useComprobanteEnvios,
  useReenviarCorreo,
  useCancelacionEstatus,
  useEmitirNcBonificacion,
  useAplicarPedimento,
} from '@/features/facturacion/api/useFacturas';
import { descargarArchivo } from '@/features/facturacion/lib/descargas';
import { ETIQUETA_MOTIVO_NC, ETIQUETA_TIPO_RELACION } from '@/features/facturacion/lib/glosario';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError, useFormIdempotencyKey } from '@/lib/api';
import { ChipTimbrado } from '@/features/facturacion/pages/BandejaFacturas';
import type { FacturasSearch } from '@/features/facturacion/lib/facturas-search-schema';

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
    <div className="space-y-4">
      {query.isError ? (
        <ErrorState
          title="No se pudo cargar la factura"
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

function Contenido({
  f,
}: {
  f: NonNullable<ReturnType<typeof useComprobante>['data']>;
}) {
  const search = useSearch({ strict: false }) as FacturasSearch;
  const envios = useComprobanteEnvios(f.id);
  const puedeEmitir = useHasPermission(PermisosCanonicos.FacturacionFacturasEmitir);
  const puedeCancelar = useHasPermission(
    PermisosCanonicos.FacturacionCancelacionesSolicitar,
  );
  const puedeNc = useHasPermission(
    PermisosCanonicos.FacturacionNotasCreditoBonificacion,
  );
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
        await descargarArchivo(
          `${BASE}/${f.id}/pdf?formato=termica`,
          `${f.folio}-termica.pdf`,
        );
      }
    } catch (error) {
      toast.error(
        esApiError(error) ? error.problem.title : 'No se pudo descargar el archivo.',
      );
    }
  }

  return (
    <div className="space-y-6">
      {/* ── Sub-topbar de acciones (§6.5, no se imprime): sticky con
          cerrar (X) que vuelve a la bandeja preservando filtros ──── */}
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
              className="text-destructive"
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

      {pendientePedimento && puedeEmitir && (
        <PedimentoForm facturaId={f.id} />
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
          <span className="text-xs text-muted-foreground">{f.tipo}</span>
        </div>
        {f.uuid && (
          <p className="font-mono text-xs text-muted-foreground">UUID {f.uuid}</p>
        )}
      </header>

      {/* FAC-UX-PR7: mismas pestañas que la emisión — Encabezado (datos
          fiscales) / Posiciones (conceptos) / Totales (resumen +
          relaciones/anticipos + bitácora). forceMount + print:!block:
          al imprimir se muestran las tres pestañas completas. */}
      <Tabs defaultValue="encabezado">
        <TabsList data-print="hidden">
          <TabsTrigger value="encabezado">Encabezado</TabsTrigger>
          <TabsTrigger value="posiciones">Posiciones ({f.lineas.length})</TabsTrigger>
          <TabsTrigger value="totales">Totales</TabsTrigger>
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

        <TabsContent value="posiciones" forceMount className="print:!block">
      <section className="space-y-2">
        <h2 className="text-sm font-medium">Conceptos ({f.lineas.length})</h2>
        <div className="overflow-x-auto rounded-md border">
          <table className="w-full text-sm">
            <thead className="bg-muted/50">
              <tr>
                <th className="px-3 py-2 text-left">#</th>
                <th className="px-3 py-2 text-left">Descripción</th>
                <th className="px-3 py-2 text-left">Clave SAT</th>
                <th className="px-3 py-2 text-right">Cant.</th>
                <th className="px-3 py-2 text-right">Valor unit.</th>
                <th className="px-3 py-2 text-right">Descuento</th>
                <th className="px-3 py-2 text-right">Importe</th>
              </tr>
            </thead>
            <tbody>
              {f.lineas.map((l) => (
                <tr key={l.posicion} className="border-t">
                  <td className="px-3 py-2 text-muted-foreground">{l.posicion}</td>
                  <td className="px-3 py-2">{l.descripcion}</td>
                  <td className="px-3 py-2 font-mono text-xs">
                    {l.claveProdServSat} · {l.claveUnidadSat}
                  </td>
                  <td className="px-3 py-2 text-right font-mono">
                    {l.cantidad.toFixed(2)}
                  </td>
                  <td className="px-3 py-2 text-right font-mono">
                    {l.valorUnitario.toFixed(2)}
                  </td>
                  <td className="px-3 py-2 text-right font-mono">
                    {l.descuento.toFixed(2)}
                  </td>
                  <td className="px-3 py-2 text-right font-mono">
                    {l.importe.toFixed(2)}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>
        </TabsContent>

        <TabsContent value="totales" forceMount className="print:!block">
          <div className="space-y-6">
      <section className="ml-auto w-full max-w-xs space-y-1 text-sm tabular-nums">
        <Renglon label="Subtotal" valor={f.subtotal} />
        <Renglon label="Descuento" valor={-f.descuento} />
        <Renglon label="IVA trasladado" valor={f.impuestosTrasladados} />
        <Renglon label="Retenciones" valor={-f.retenciones} />
        <div className="flex justify-between border-t pt-1 font-semibold">
          <span>Total</span>
          <span className="font-mono">
            {f.total.toFixed(2)} {f.moneda}
          </span>
        </div>
        {/* [Decisión 13-K]: NC timbradas (amortización de anticipos / NC
            generales) acreditan al saldo — se muestra el neto por cobrar.
            Los ?? cubren un API previo al PR backend (campos ausentes). */}
        {(f.totalAcreditado ?? 0) > 0 && (
          <>
            <Renglon label="Acreditado por NC" valor={-f.totalAcreditado} />
            <div className="flex justify-between border-t pt-1 font-semibold">
              <span>Por cobrar</span>
              <span className="font-mono">
                {f.totalPorCobrar.toFixed(2)} {f.moneda}
              </span>
            </div>
          </>
        )}
      </section>

      {/* ── NC aplicadas ([Decisión 13-K]) ──────────────────────── */}
      {(f.notasCreditoAplicadas ?? []).length > 0 && (
        <section className="space-y-2">
          <h2 className="text-sm font-medium">
            Notas de crédito aplicadas ({f.notasCreditoAplicadas!.length})
          </h2>
          <div className="overflow-x-auto rounded-md border">
            <table className="w-full text-sm">
              <thead className="bg-muted/50">
                <tr>
                  <th className="px-3 py-2 text-left">Folio</th>
                  <th className="px-3 py-2 text-left">Motivo</th>
                  <th className="px-3 py-2 text-left">UUID</th>
                  <th className="px-3 py-2 text-right">Total</th>
                </tr>
              </thead>
              <tbody>
                {f.notasCreditoAplicadas!.map((nc) => (
                  <tr key={nc.id} className="border-t">
                    <td className="px-3 py-2 font-mono text-xs">{nc.folio}</td>
                    <td className="px-3 py-2">
                      {ETIQUETA_MOTIVO_NC[nc.motivo] ?? nc.motivo}
                    </td>
                    <td className="px-3 py-2 font-mono text-xs">{nc.uuid ?? '—'}</td>
                    <td className="px-3 py-2 text-right font-mono">
                      {nc.total.toFixed(2)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      )}

      {/* ── Cadena de relaciones CFDI (B4): anticipos 07 / NC 01 /
          sustitución 04 ─────────────────────────────────────────── */}
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
                    <td className="px-3 py-2 font-mono text-xs">
                      {r.folio ?? '—'}
                    </td>
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

      {/* ── Cobro de mostrador (CAJAS-PR6, [Decisión 12-E]). El monto a
          cobrar es el neto de NC ([Decisión 13-K]); el backend lo valida. */}
      {vigente && (f.totalPorCobrar ?? f.total) > 0 && (
        <section className="space-y-2" data-print="hidden">
          <RegistrarCobroCard
            comprobanteId={f.id}
            folio={f.folio}
            total={f.totalPorCobrar ?? f.total}
            moneda={f.moneda}
          />
        </section>
      )}
      {vigente && (f.totalAcreditado ?? 0) > 0 && (f.totalPorCobrar ?? f.total) <= 0 && (
        <p
          className="rounded-md border border-dashed px-3 py-3 text-center text-xs text-muted-foreground"
          data-print="hidden"
        >
          Sin monto por cobrar: las notas de crédito acreditan el total de la factura.
        </p>
      )}

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
          <div className="h-12 w-full animate-pulse rounded bg-muted" />
        ) : (envios.data ?? []).length === 0 ? (
          <p className="rounded-md border border-dashed px-3 py-3 text-center text-xs text-muted-foreground">
            Sin envíos registrados.
          </p>
        ) : (
          <div className="overflow-x-auto rounded-md border">
            <table className="w-full text-sm">
              <thead className="bg-muted/50">
                <tr>
                  <th className="px-3 py-2 text-left">Destinatario</th>
                  <th className="px-3 py-2 text-left">Estado</th>
                  <th className="px-3 py-2 text-right">Intentos</th>
                  <th className="px-3 py-2 text-left">Enviado</th>
                  <th className="px-3 py-2 text-left">Último error</th>
                </tr>
              </thead>
              <tbody>
                {(envios.data ?? []).map((e) => (
                  <tr key={e.id} className="border-t">
                    <td className="px-3 py-2">{e.destinatario}</td>
                    <td className="px-3 py-2">{e.estado}</td>
                    <td className="px-3 py-2 text-right">{e.intentos}</td>
                    <td className="px-3 py-2 text-xs text-muted-foreground">
                      {e.enviadoAt
                        ? new Date(e.enviadoAt).toLocaleString('es-MX')
                        : '—'}
                    </td>
                    <td className="px-3 py-2 text-xs text-destructive">
                      {e.ultimoError ?? '—'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
          </div>
        </TabsContent>
      </Tabs>
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
          toast.error(
            esApiError(error) ? error.problem.title : 'No se pudo reenviar.',
          );
        },
      },
    );
  }

  return (
    <div className="flex flex-wrap items-end gap-2 rounded-md border border-dashed p-3">
      <div className="flex-1 space-y-1">
        <label className="text-xs text-muted-foreground" htmlFor="reenviar-email">
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
      <Button
        type="button"
        variant="outline"
        onClick={enviar}
        disabled={reenviar.isPending}
      >
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
        onError: (error) =>
          toast.error(
            esApiError(error) ? error.problem.title : 'No se pudo aplicar el pedimento.',
          ),
      },
    );
  }

  return (
    <div
      className="grid grid-cols-1 gap-2 rounded-md border border-amber-300 bg-amber-50/60 p-3 sm:grid-cols-4"
      data-print="hidden"
    >
      <div className="sm:col-span-4 text-sm font-medium">
        Factura retenida por pedimento — aplícalo para timbrar
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
        <Input
          value={identificacion}
          onChange={(e) => setIdentificacion(e.target.value)}
        />
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
        onError: (error) =>
          toast.error(esApiError(error) ? error.problem.title : 'No se pudo emitir la NC.'),
      },
    );
  }

  return (
    <div
      className="grid grid-cols-1 gap-2 rounded-md border border-dashed p-3 sm:grid-cols-4"
      data-print="hidden"
    >
      <div className="sm:col-span-4 text-sm font-medium">
        Nota de crédito por bonificación (relación 01)
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
