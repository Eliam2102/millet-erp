import { problemaConsulta } from '@/components/erp/feedback/problema-consulta';
import { formatMoney } from '@/lib/money';
import { formatCantidad } from '@/lib/format-cantidad';
import { AvisoEnLinea, ErrorState } from '@/components/erp';
import { ResumenImportes } from '@/components/erp/display/ResumenImportes';
import { PageHeader } from '@/components/erp/PageHeader';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { facturacionKeys } from '@/features/facturacion/api/keys';
import { ComportamientoFiscal, OrigenSalidaFacturable } from '@/features/facturacion/api/types';
import {
  useEmbarqueCeSalida,
  usePedido,
  usePedidoComprobantes,
  usePedidoSalidas,
} from '@/features/facturacion/api/usePedidos';
import { useGenerarProforma, useUltimaProforma } from '@/features/facturacion/api/useProformas';
import type {
  ProformaPedidoResponse,
  SalidaFacturableItem,
} from '@/features/facturacion/api/types';
import { ClasePedidoBadge } from '@/features/facturacion/components/ClasePedidoBadge';
import { BadgeCredito } from '@/features/facturacion/components/BadgeCredito';
import { AvisoCobrarEnCaja } from '@/features/facturacion/components/CobrarEnCajaLink';
import { EmitirFacturaForm } from '@/features/facturacion/components/emitir-factura/EmitirFacturaForm';
import { EstadoFacturacionBadge } from '@/features/facturacion/components/EstadoFacturacionBadge';
import { ProformaPedidoCard } from '@/features/facturacion/components/ProformaPedidoCard';
import { SalidasPedido } from '@/features/facturacion/components/SalidasPedido';
import { VisorProformaDialog } from '@/features/facturacion/components/VisorProformaDialog';
import { useNuevoPedido } from '@/features/facturacion/components/nuevo-pedido-context';
import { TrazabilidadFacturacion } from '@/features/facturacion/components/TrazabilidadFacturacion';
import {
  ETIQUETA_COMPORTAMIENTO_FISCAL,
  ETIQUETA_ORIGEN_PEDIDO,
} from '@/features/facturacion/lib/glosario';
import type { PedidosSearch } from '@/features/facturacion/lib/pedidos-search-schema';
import { ChipTimbrado } from '@/features/facturacion/pages/BandejaFacturas';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { useQueryClient } from '@tanstack/react-query';
import { Link, useParams, useSearch } from '@tanstack/react-router';
import { Eye, FileText, Pencil, Receipt, X } from 'lucide-react';
import { useState } from 'react';
import { toast } from 'sonner';

/**
 * <c>Detalle de un pedido facturable</c> (FE-F1-PR3, B1/B5). Cabecera +
 * bandeja de líneas + historial de comprobantes del pedido (cancelados +
 * vigente).
 *
 * <para>FAC-UX-PR2: "Facturar" ya NO abre un Sheet — el propio detalle
 * cambia a modo emisión in-place con el form de pestañas
 * (<c>&lt;EmitirFacturaForm/&gt;</c>) prellenado desde el pedido.
 * Cancelar (con confirm si hay cambios) regresa a la vista de lectura.</para>
 */
export function DetallePedido() {
  const { id } = useParams({ from: '/_app/facturacion/pedidos/$id' });
  const search = useSearch({ strict: false }) as PedidosSearch;
  const query = usePedido(id);
  const comprobantes = usePedidoComprobantes(id);

  return (
    <div className="space-y-5">
      {/* Sub-topbar del detalle (§6.5): cerrar vuelve a la bandeja
          preservando los filtros activos. */}
      <div
        className="sticky top-0 z-10 flex items-center justify-end gap-2 border-b bg-surface-page pb-2 border-line"
        data-print="hidden"
      >
        <Button variant="ghost" size="sm" asChild aria-label="Cerrar detalle">
          <Link to="/facturacion/pedidos" search={search}>
            <X className="h-4 w-4" />
          </Link>
        </Button>
      </div>

      {query.isError ? (
        <ErrorState
          title="No se pudo cargar el pedido"
          problem={problemaConsulta(query.error)}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading || query.data == null ? (
        <div className="space-y-3">
          <div className="h-8 w-64 animate-pulse rounded bg-surface-muted" />
          <div className="h-40 w-full animate-pulse rounded bg-surface-muted" />
        </div>
      ) : (
        <Contenido
          p={query.data}
          comprobantes={comprobantes.data ?? []}
          comprobantesLoading={comprobantes.isLoading}
        />
      )}
    </div>
  );
}

function Contenido({
  p,
  comprobantes,
  comprobantesLoading,
}: {
  p: NonNullable<ReturnType<typeof usePedido>['data']>;
  comprobantes: NonNullable<ReturnType<typeof usePedidoComprobantes>['data']>;
  comprobantesLoading: boolean;
}) {
  const queryClient = useQueryClient();
  const nuevoPedido = useNuevoPedido();
  const [modoEmision, setModoEmision] = useState(false);
  // FAC-08: parcialidad que se está facturando (null = el pedido completo).
  const [salidaEnEmision, setSalidaEnEmision] = useState<SalidaFacturableItem | null>(null);
  const [emitida, setEmitida] = useState<{ id: string; folio: string } | null>(null);
  const puedeEmitir = useHasPermission(PermisosCanonicos.FacturacionFacturasEmitir);
  const puedeEditar = useHasPermission(PermisosCanonicos.FacturacionPedidosCapturar);
  const puedeLeerFacturas = useHasPermission(PermisosCanonicos.FacturacionFacturasLeer);
  const facturable = p.estado === 'Importado';
  // FAC-08: un pedido con salidas parciales se factura por salida, no completo.
  const salidas = usePedidoSalidas(p.id, puedeLeerFacturas);
  const haySalidas = (salidas.data?.salidas.length ?? 0) > 0;
  // FAC-08 Fase 1: la factura de la salida de un contenedor usa la compuerta de ese contenedor.
  const salidaContenedorId =
    salidaEnEmision?.origen === OrigenSalidaFacturable.ContenedorExportacion ? salidaEnEmision.id : null;
  const embarqueSalida = useEmbarqueCeSalida(p.id, salidaContenedorId);
  const embarqueCeEmision =
    salidaContenedorId != null && embarqueSalida.isSuccess ? embarqueSalida.data : p.embarqueCe;
  // U1.8a: proforma sin valor fiscal (no consume folio) antes de timbrar.
  const ultimaProforma = useUltimaProforma(p.id, puedeLeerFacturas);
  const generarProforma = useGenerarProforma();
  const [descargandoProforma, setDescargandoProforma] = useState(false);
  // Proforma abierta en el visor (null = cerrado).
  const [proformaEnVisor, setProformaEnVisor] = useState<ProformaPedidoResponse | null>(null);

  async function descargarProforma(proforma: ProformaPedidoResponse) {
    setDescargandoProforma(true);
    try {
      const { descargarProformaCfdi } =
        await import('@/components/erp/reportes/exporters/proforma-cfdi-exporter');
      await descargarProformaCfdi(proforma);
    } catch {
      toast.error('No se pudo generar el PDF de la proforma. Vuelve a intentar la descarga.');
    } finally {
      setDescargandoProforma(false);
    }
  }

  function onGenerarProforma() {
    generarProforma.mutate(
      { pedidoId: p.id, idempotencyKey: crypto.randomUUID() },
      {
        onSuccess: (proforma) => {
          toast.success(`Proforma ${proforma.folio} generada`, {
            description: 'Sin valor fiscal: no consume folio.',
          });
          setProformaEnVisor(proforma);
        },
        onError: () => {
          toast.error('No se pudo generar la proforma.', {
            description: 'Revisa los datos del pedido y tu conexión antes de volver a intentarlo.',
          });
        },
      },
    );
  }
  const mostrarSat = p.lineas.some((l) => l.claveProdServSat?.trim() || l.claveUnidadSat?.trim());
  const mostrarPedimento = p.lineas.some((l) => l.requierePedimento);
  // Solo los manuales en Importado son editables (PUT con If-Match).
  const editable = p.estado === 'Importado' && p.origen === 'Manual';
  // F6.1: pedido manual legacy sin clase → no se puede facturar hasta clasificarlo.
  const manualSinClase = p.origen === 'Manual' && p.claseManual == null;
  const esEmergencia = p.esEmergencia === true || p.claseManual === 'Emergencia';
  const bloqueoEmision = manualSinClase
    ? 'Clasifica el pedido (venta directa o emergencia) antes de facturarlo.'
    : undefined;

  function cerrarEmision() {
    setModoEmision(false);
    setSalidaEnEmision(null);
  }

  // FAC-08: con parcialidad, las partidas son las de la salida (mismas
  // posiciones y cantidades, en orden). El descuento sale del importe que la
  // salida ya calculó, así la suma de las facturas cuadra con el pedido.
  const lineasEmision = salidaEnEmision
    ? [...salidaEnEmision.lineas]
        .sort((a, b) => a.posicion - b.posicion)
        .flatMap((sl) => {
          const l = p.lineas.find((x) => x.posicion === sl.posicion);
          if (l == null) return [];
          const bruto = sl.cantidad * l.precio;
          return [{ l, cantidad: sl.cantidad, descuento: Math.max(0, redondear2(bruto - sl.importe)) }];
        })
    : p.lineas.map((l) => ({ l, cantidad: l.cantidad, descuento: l.descuento }));

  if (modoEmision && salidaContenedorId != null && embarqueSalida.isPending) {
    return (
      <div className="space-y-3" aria-busy="true">
        <div className="h-9 w-72 animate-pulse rounded bg-surface-muted" />
        <div className="h-64 w-full animate-pulse rounded bg-surface-muted" />
      </div>
    );
  }

  if (modoEmision) {
    return (
      <div className="space-y-5">
        <div className="flex flex-wrap items-center gap-3">
          <PageHeader
            mono
            titulo={<>{p.numeroPedido ?? 'Pedido manual'}</>}
            descripcion={<>Consulta el pedido y sus partidas por facturar.</>}
            estado={<ChipEstadoPedido estado={p.estado} />}
          />
          <ClasePedidoBadge clasificacion={p} />
          <span className="text-sm text-ink-muted">
            Emitiendo factura · {p.clienteNombre}
            {salidaEnEmision && ` · parcialidad ${salidaEnEmision.numeroParcialidad}`}
          </span>
        </div>
        <EmitirFacturaForm
          prefill={{
            pedidoFacturableId: p.id,
            salidaFacturableId: salidaEnEmision?.id ?? null,
            numeroParcialidad: salidaEnEmision?.numeroParcialidad ?? null,
            embarqueCe: embarqueCeEmision,
            // F6.1: clase del pedido manual (serie de Venta directa, activo).
            origen: p.origen,
            claseManual: p.claseManual ?? null,
            subtipoVentaDirecta: p.subtipoVentaDirecta ?? null,
            sucursalId: p.sucursalId,
            clienteId: p.clienteId,
            receptorNombre: p.clienteNombre,
            // Datos fiscales vivos del master de clientes (FAC-UX-PR3).
            receptorRfc: p.clienteFiscal?.rfc,
            receptorRegimenFiscal: p.clienteFiscal?.regimenFiscal,
            receptorCodigoPostal: p.clienteFiscal?.codigoPostalFiscal,
            receptorUsoCfdi: p.clienteFiscal?.usoCfdiDefault,
            metodoPago: p.clienteFiscal?.metodoPagoDefault,
            formaPago: p.clienteFiscal?.formaPagoDefault,
            // Receptor extranjero (CCE) del catálogo del cliente, también en la salida.
            cceReceptorNumRegIdTrib: p.clienteFiscal?.numRegIdTrib,
            cceReceptorPaisResidencia: p.clienteFiscal?.paisResidencia,
            cceReceptorDomicilioCalle: p.clienteFiscal?.domicilioExtranjeroCalle,
            cceReceptorDomicilioEstado: p.clienteFiscal?.domicilioExtranjeroEstado,
            cceReceptorDomicilioCodigoPostal: p.clienteFiscal?.domicilioExtranjeroCodigoPostal,
            datosFiscalesIncompletos:
              p.clienteFiscal == null ||
              p.clienteFiscal.rfc == null ||
              p.clienteFiscal.regimenFiscal == null ||
              p.clienteFiscal.codigoPostalFiscal == null,
            // FAC-ING-PR3: id + nombre del catálogo de canales de venta.
            canalVenta: p.canalVentaId,
            canalVentaNombre: p.canalVenta,
            comportamientoFiscal:
              ComportamientoFiscal[p.comportamientoFiscal as keyof typeof ComportamientoFiscal],
            moneda: p.moneda,
            obraNombre: p.obraNombre,
            referenciaCliente: p.numeroPedido,
            observaciones: p.comentarios,
            condicionesPago: p.condicionPagoOrigen,
            lineas: lineasEmision.map(({ l, cantidad, descuento }) => ({
              productoId: l.productoId,
              claveProdServSat: l.claveProdServSat ?? '',
              descripcion: l.productoDescripcion,
              claveUnidadSat: l.claveUnidadSat ?? '',
              cantidad,
              valorUnitario: l.precio,
              descuento,
              requierePedimento: l.requierePedimento,
              objetoImp: l.objetoImp,
              tasaIvaTraslado: l.tasaIvaTraslado,
              tasaRetencionIva: l.tasaRetencionIva,
              tasaRetencionIsr: l.tasaRetencionIsr,
            })),
          }}
          onCancel={cerrarEmision}
          onEditarPedido={
            puedeEditar && editable
              ? () => {
                  cerrarEmision();
                  nuevoPedido.abrir(p);
                }
              : undefined
          }
          onSuccess={(res) => {
            if (res.uuid) setEmitida({ id: res.id, folio: res.folio });
            void queryClient.invalidateQueries({
              queryKey: facturacionKeys.pedidos(),
            });
            cerrarEmision();
          }}
        />
      </div>
    );
  }

  return (
    <div className="space-y-5">
      {emitida && <AvisoCobrarEnCaja comprobanteId={emitida.id} folio={emitida.folio} />}
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="space-y-1">
          <div className="flex flex-wrap items-center gap-3">
            <PageHeader
              mono
              titulo={<>{p.numeroPedido ?? 'Pedido manual'}</>}
              descripcion={
                <>
                  <span>{p.clienteNombre}</span> ·{' '}
                  <span className="font-mono">
                    {p.clienteFiscal?.rfc ?? <span className="text-ink-muted">Sin RFC</span>}
                  </span>
                </>
              }
              estado={<ChipEstadoPedido estado={p.estado} />}
            />
            <Badge variant="neutral">{ETIQUETA_ORIGEN_PEDIDO[p.origen] ?? p.origen}</Badge>
            <ClasePedidoBadge clasificacion={p} />
            <BadgeCredito esCredito={p.esCredito} />
          </div>
        </div>
        <div className="flex items-center gap-2" data-print="hidden">
          {puedeEditar && editable && (
            <Button variant="outline" onClick={() => nuevoPedido.abrir(p)}>
              <Pencil className="mr-2 h-4 w-4" />
              Editar
            </Button>
          )}
          {puedeLeerFacturas && ultimaProforma.data != null && (
            <Button
              variant="outline"
              onClick={() => setProformaEnVisor(ultimaProforma.data ?? null)}
            >
              <Eye className="mr-2 h-4 w-4" />
              Ver proforma
            </Button>
          )}
          {puedeEmitir && facturable && (
            <Button
              variant="outline"
              onClick={onGenerarProforma}
              disabled={generarProforma.isPending}
            >
              <FileText className="mr-2 h-4 w-4" />
              {generarProforma.isPending ? 'Generando…' : 'Generar proforma'}
            </Button>
          )}
          {puedeEmitir && facturable && (
            <Button
              onClick={() => setModoEmision(true)}
              disabled={manualSinClase || haySalidas}
              title={
                haySalidas
                  ? 'El pedido se factura por salidas parciales: factura cada parcialidad en «Salidas parciales».'
                  : bloqueoEmision
              }
            >
              <Receipt className="mr-2 h-4 w-4" />
              Facturar
            </Button>
          )}
        </div>
      </div>

      {manualSinClase && facturable && (
        <AvisoEnLinea tono="advertencia" titulo="Pedido manual sin clase">
          Indica si es venta directa o emergencia para poder facturarlo.
          {puedeEditar && editable
            ? ' Usa «Editar» para clasificarlo.'
            : ' Pide a quien captura pedidos que lo clasifique.'}
        </AvisoEnLinea>
      )}

      {esEmergencia && (
        <section
          aria-label="Datos de la emergencia"
          className="space-y-2 rounded-lg bg-surface-card px-4 py-3 shadow-card-flat"
        >
          <h2 className="text-sm font-semibold text-ink">Captura de emergencia</h2>
          <dl className="grid grid-cols-1 gap-3 sm:grid-cols-3">
            <div className="space-y-0.5 sm:col-span-2">
              <dt className="text-xs text-ink-muted">Motivo</dt>
              <dd className="text-sm text-ink">{p.motivoEmergencia || <span className="text-ink-muted">—</span>}</dd>
            </div>
            <div className="space-y-0.5">
              <dt className="text-xs text-ink-muted">Pedido A+W para conciliar</dt>
              <dd className="font-mono text-sm text-ink">
                {p.referenciaPedidoAw ?? 'Sin referencia'}
              </dd>
            </div>
          </dl>
        </section>
      )}

      <dl className="grid grid-cols-1 gap-4 rounded-lg bg-surface-card p-4 shadow-card-flat sm:grid-cols-4">
        {/* FAC-ING-PR3: el backend ya devuelve el nombre del catálogo. */}
        <Dato label="Canal de venta">{p.canalVenta}</Dato>
        <Dato label="Comportamiento fiscal">
          {ETIQUETA_COMPORTAMIENTO_FISCAL[p.comportamientoFiscal] ?? p.comportamientoFiscal}
        </Dato>
        <Dato label="Moneda">{p.moneda}</Dato>
        {p.esCredito && (
          <Dato label="Condición de pago">{p.condicionPagoOrigen ?? 'Crédito'}</Dato>
        )}
        {p.obraNombre && <Dato label="Obra">{p.obraNombre}</Dato>}
        <Dato label="RFC del cliente">
          {p.clienteFiscal?.rfc ?? (
            <span className="text-warning-fg">
              Sin RFC — completar en Datos Maestros → Clientes
            </span>
          )}
        </Dato>
        <Dato label="Versión">{p.version}</Dato>
      </dl>

      {puedeLeerFacturas && (ultimaProforma.data != null || facturable) && (
        <ProformaPedidoCard
          proforma={ultimaProforma.data}
          cargando={ultimaProforma.isLoading}
          descargando={descargandoProforma}
          onVer={setProformaEnVisor}
          onDescargar={(pf) => void descargarProforma(pf)}
        />
      )}
      <VisorProformaDialog
        proforma={proformaEnVisor}
        descargando={descargandoProforma}
        onDescargar={(pf) => void descargarProforma(pf)}
        onCerrar={() => setProformaEnVisor(null)}
      />

      {p.comentarios && (
        <section className="rounded-md bg-surface-muted/40 px-3 py-2 text-sm">
          <span className="text-xs font-medium text-ink-muted">Comentarios del origen</span>
          <p className="mt-0.5">{p.comentarios}</p>
        </section>
      )}

      <section className="space-y-2">
        <h2 className="text-sm font-medium">Partidas ({p.lineas.length})</h2>
        <div className="overflow-x-auto rounded-md border border-line">
          <Table className="w-full text-sm">
            <TableHeader className="bg-surface-muted/50">
              <TableRow>
                <TableHead className="px-3 py-2 text-left">#</TableHead>
                <TableHead className="px-3 py-2 text-left">Producto / servicio</TableHead>
                {mostrarSat && <TableHead className="px-3 py-2 text-left">Clave SAT</TableHead>}
                <TableHead className="px-3 py-2 text-right tabular-nums">Cant.</TableHead>
                <TableHead className="px-3 py-2 text-right tabular-nums">Precio</TableHead>
                <TableHead className="px-3 py-2 text-right tabular-nums">Descuento</TableHead>
                {mostrarPedimento && (
                  <TableHead className="px-3 py-2 text-center">Pedimento</TableHead>
                )}
              </TableRow>
            </TableHeader>
            <TableBody>
              {p.lineas.map((l) => (
                <TableRow key={l.posicion} className="border-t border-line">
                  <TableCell className="px-3 py-2 text-right text-ink-muted tabular-nums">{l.posicion}</TableCell>
                  <TableCell className="px-3 py-2">{l.productoDescripcion}</TableCell>
                  {mostrarSat && (
                    <TableCell className="px-3 py-2 font-mono text-xs">
                      {l.claveProdServSat || <span className="text-ink-muted">—</span>} · {l.claveUnidadSat || <span className="text-ink-muted">—</span>}
                    </TableCell>
                  )}
                  <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                    {formatCantidad(l.cantidad)}
                  </TableCell>
                  <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                    {formatMoney({ amount: l.precio, currency: p.moneda })}
                  </TableCell>
                  <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                    {formatMoney({ amount: l.descuento, currency: p.moneda })}
                  </TableCell>
                  {mostrarPedimento && (
                    <TableCell className="px-3 py-2 text-center">
                      {l.requierePedimento ? 'Sí' : <span className="text-ink-muted">—</span>}
                    </TableCell>
                  )}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
        <p className="text-xs text-ink-muted">Subtotal sin impuestos</p>
        <ResumenImportes moneda={p.moneda} total={p.total} />
        {p.ranura != null && p.ranura > 0 && (
          <p className="text-right text-sm text-warning-fg tabular-nums">
            Ranura (descuento del pedido — la factura va por el total; se documenta con nota de
            crédito y la caja cobra el neto): −{formatMoney({ amount: p.ranura, currency: p.moneda })}
          </p>
        )}
        <p className="text-xs text-ink-muted">
          Los impuestos y el total fiscal se calculan al preparar la factura.
        </p>
      </section>

      {puedeLeerFacturas && (
        <SalidasPedido
          pedidoId={p.id}
          moneda={p.moneda}
          facturable={facturable}
          puedeCapturar={puedeEditar}
          puedeEmitir={puedeEmitir}
          bloqueoEmision={bloqueoEmision}
          salidas={salidas}
          onFacturarSalida={(s) => {
            setSalidaEnEmision(s);
            setModoEmision(true);
          }}
        />
      )}

      {(p.anticiposLigados?.length ?? 0) > 0 && (
        <section className="space-y-2">
          <h2 className="text-sm font-medium">Anticipos del pedido ({p.anticiposLigados!.length})</h2>
          <p className="text-xs text-ink-muted">
            Cobrados con este número de pedido de A+W antes de que llegara al ERP; se aplican al facturar.
          </p>
          <div className="overflow-x-auto rounded-md border border-line">
            <Table className="w-full text-sm">
              <TableHeader className="bg-surface-muted/50">
                <TableRow>
                  <TableHead className="px-3 py-2 text-left">Folio</TableHead>
                  <TableHead className="px-3 py-2 text-right tabular-nums">Cobrado</TableHead>
                  <TableHead className="px-3 py-2 text-right tabular-nums">Saldo</TableHead>
                  <TableHead className="px-3 py-2 text-left">Estado</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {p.anticiposLigados!.map((a) => (
                  <TableRow key={a.anticipoId} className="border-t border-line">
                    <TableCell className="px-3 py-2 font-mono text-xs">{a.folio}</TableCell>
                    <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                      {formatMoney({ amount: a.montoCobrado, currency: p.moneda })}
                    </TableCell>
                    <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                      {formatMoney({ amount: a.saldo, currency: p.moneda })}
                    </TableCell>
                    <TableCell className="px-3 py-2"><EstadoFacturacionBadge entidad="anticipo" estado={a.estado} /></TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        </section>
      )}

      <section className="space-y-2">
        <h2 className="text-sm font-medium">Historial de comprobantes ({comprobantes.length})</h2>
        {comprobantesLoading ? (
          <div className="h-16 w-full animate-pulse rounded bg-surface-muted" />
        ) : comprobantes.length === 0 ? (
          <p className="rounded-md border border-dashed px-3 py-4 text-center text-xs text-ink-muted border-line">
            El pedido aún no tiene comprobantes emitidos.
          </p>
        ) : (
          <div className="overflow-x-auto rounded-md border border-line">
            <Table className="w-full text-sm">
              <TableHeader className="bg-surface-muted/50">
                <TableRow>
                  <TableHead className="px-3 py-2 text-left">Folio</TableHead>
                  <TableHead className="px-3 py-2 text-left">UUID</TableHead>
                  <TableHead className="px-3 py-2 text-right tabular-nums">Total</TableHead>
                  <TableHead className="px-3 py-2 text-left">Estado</TableHead>
                  <TableHead className="px-3 py-2 text-center">Vigente</TableHead>
                  <TableHead className="px-3 py-2 text-right tabular-nums">Acción</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {comprobantes.map((c) => (
                  <TableRow key={c.id} className="border-t hover:bg-surface-muted/30 border-line">
                    <TableCell className="px-3 py-2 font-mono text-xs">{c.folio}</TableCell>
                    <TableCell className="px-3 py-2 font-mono text-xs">
                      {c.uuid ? `${c.uuid.slice(0, 8)}…${c.uuid.slice(-4)}` : <span className="text-ink-muted">—</span>}
                    </TableCell>
                    <TableCell className="px-3 py-2 text-right font-mono tabular-nums">
                      {formatMoney({ amount: c.total, currency: c.moneda })}
                    </TableCell>
                    <TableCell className="px-3 py-2">
                      <ChipTimbrado estado={c.estado} />
                    </TableCell>
                    <TableCell className="px-3 py-2 text-center">
                      {c.vigente ? <Badge variant="success">Vigente</Badge> : <span className="text-ink-muted">—</span>}
                    </TableCell>
                    <TableCell className="px-3 py-2 text-right tabular-nums">
                      <Link
                        to="/facturacion/facturas/$id"
                        params={{ id: c.id }}
                        className="text-sm font-medium text-brand hover:underline"
                      >
                        Ver
                      </Link>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </section>

      {/* ── Trazabilidad documento-céntrica (ANT-PR3, doc 13) ────── */}
      <TrazabilidadFacturacion raiz="pedido" id={p.id} />
    </div>
  );
}

export function ChipEstadoPedido({ estado }: { estado: string }) {
  return <EstadoFacturacionBadge entidad="pedido" estado={estado} />;
}

function redondear2(v: number): number {
  return Math.round(v * 100) / 100;
}

function Dato({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="space-y-0.5">
      <dt className="text-xs text-ink-muted">{label}</dt>
      <dd className="text-sm">{children}</dd>
    </div>
  );
}
