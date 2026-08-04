import { useState } from 'react';
import { Link, useParams, useSearch } from '@tanstack/react-router';
import { useQueryClient } from '@tanstack/react-query';
import { Pencil, Receipt, X } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { ErrorState } from '@/components/erp';
import {
  usePedido,
  usePedidoComprobantes,
} from '@/features/facturacion/api/usePedidos';
import { facturacionKeys } from '@/features/facturacion/api/keys';
import { EmitirFacturaForm } from '@/features/facturacion/components/emitir-factura/EmitirFacturaForm';
import { useNuevoPedido } from '@/features/facturacion/components/nuevo-pedido-context';
import { TrazabilidadFacturacion } from '@/features/facturacion/components/TrazabilidadFacturacion';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { ComportamientoFiscal } from '@/features/facturacion/api/types';
import {
  ETIQUETA_COMPORTAMIENTO_FISCAL,
  ETIQUETA_ESTADO_PEDIDO,
  ETIQUETA_ORIGEN_PEDIDO,
} from '@/features/facturacion/lib/glosario';
import { ChipTimbrado } from '@/features/facturacion/pages/BandejaFacturas';
import type { PedidosSearch } from '@/features/facturacion/lib/pedidos-search-schema';
import { cn } from '@/lib/utils';

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
    <div className="space-y-4">
      {/* Sub-topbar del detalle (§6.5): cerrar vuelve a la bandeja
          preservando los filtros activos. */}
      <div
        className="sticky top-0 z-10 flex items-center justify-end gap-2 border-b bg-background/95 pb-2 backdrop-blur"
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
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading || query.data == null ? (
        <div className="space-y-3">
          <div className="h-8 w-64 animate-pulse rounded bg-muted" />
          <div className="h-40 w-full animate-pulse rounded bg-muted" />
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
  const puedeEmitir = useHasPermission(PermisosCanonicos.FacturacionFacturasEmitir);
  const puedeEditar = useHasPermission(PermisosCanonicos.FacturacionPedidosCapturar);
  const facturable = p.estado === 'Importado';
  // Solo los manuales en Importado son editables (PUT con If-Match).
  const editable = p.estado === 'Importado' && p.origen === 'Manual';

  if (modoEmision) {
    return (
      <div className="space-y-4">
        <header className="flex flex-wrap items-center gap-3">
          <h1 className="font-mono text-2xl font-semibold">
            {p.numeroPedido ?? 'Pedido manual'}
          </h1>
          <ChipEstadoPedido estado={p.estado} />
          <span className="text-sm text-muted-foreground">
            Emitiendo factura · {p.clienteNombre}
          </span>
        </header>
        <EmitirFacturaForm
          prefill={{
            pedidoFacturableId: p.id,
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
            datosFiscalesIncompletos:
              p.clienteFiscal == null ||
              p.clienteFiscal.rfc == null ||
              p.clienteFiscal.regimenFiscal == null ||
              p.clienteFiscal.codigoPostalFiscal == null,
            // FAC-ING-PR3: id + nombre del catálogo de canales de venta.
            canalVenta: p.canalVentaId,
            canalVentaNombre: p.canalVenta,
            comportamientoFiscal:
              ComportamientoFiscal[
                p.comportamientoFiscal as keyof typeof ComportamientoFiscal
              ],
            moneda: p.moneda,
            obraNombre: p.obraNombre,
            lineas: p.lineas.map((l) => ({
              productoId: l.productoId,
              claveProdServSat: l.claveProdServSat ?? '',
              descripcion: l.productoDescripcion,
              claveUnidadSat: l.claveUnidadSat ?? '',
              cantidad: l.cantidad,
              valorUnitario: l.precio,
              descuento: l.descuento,
              requierePedimento: l.requierePedimento,
              objetoImp: l.objetoImp,
              tasaIvaTraslado: l.tasaIvaTraslado,
              tasaRetencionIva: l.tasaRetencionIva,
              tasaRetencionIsr: l.tasaRetencionIsr,
            })),
          }}
          onCancel={() => setModoEmision(false)}
          onSuccess={() => {
            void queryClient.invalidateQueries({
              queryKey: facturacionKeys.pedidos(),
            });
            setModoEmision(false);
          }}
        />
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div className="space-y-1">
          <div className="flex flex-wrap items-center gap-3">
            <h1 className="font-mono text-2xl font-semibold">
              {p.numeroPedido ?? 'Pedido manual'}
            </h1>
            <ChipEstadoPedido estado={p.estado} />
            <span className="rounded-full bg-muted px-2 py-0.5 text-xs">
              {ETIQUETA_ORIGEN_PEDIDO[p.origen] ?? p.origen}
            </span>
          </div>
          <p className="text-sm text-muted-foreground">{p.clienteNombre}</p>
        </div>
        <div className="flex items-center gap-2" data-print="hidden">
          {puedeEditar && editable && (
            <Button variant="outline" onClick={() => nuevoPedido.abrir(p)}>
              <Pencil className="mr-2 h-4 w-4" />
              Editar
            </Button>
          )}
          {puedeEmitir && facturable && (
            <Button onClick={() => setModoEmision(true)}>
              <Receipt className="mr-2 h-4 w-4" />
              Facturar
            </Button>
          )}
        </div>
      </header>

      <section className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {/* FAC-ING-PR3: el backend ya devuelve el nombre del catálogo. */}
        <Dato label="Canal de venta">{p.canalVenta}</Dato>
        <Dato label="Comportamiento fiscal">
          {ETIQUETA_COMPORTAMIENTO_FISCAL[p.comportamientoFiscal] ??
            p.comportamientoFiscal}
        </Dato>
        <Dato label="Moneda">{p.moneda}</Dato>
        {p.obraNombre && <Dato label="Obra">{p.obraNombre}</Dato>}
        <Dato label="RFC del cliente">
          {p.clienteFiscal?.rfc ?? (
            <span className="text-amber-700">
              Sin RFC — completar en Datos Maestros → Clientes
            </span>
          )}
        </Dato>
        <Dato label="Versión">{p.version}</Dato>
      </section>

      {p.comentarios && (
        <section className="rounded-md bg-muted/40 px-3 py-2 text-sm">
          <span className="text-xs font-medium text-muted-foreground">
            Comentarios del origen
          </span>
          <p className="mt-0.5">{p.comentarios}</p>
        </section>
      )}

      <section className="space-y-2">
        <h2 className="text-sm font-medium">Líneas ({p.lineas.length})</h2>
        <div className="overflow-x-auto rounded-md border">
          <table className="w-full text-sm">
            <thead className="bg-muted/50">
              <tr>
                <th className="px-3 py-2 text-left">#</th>
                <th className="px-3 py-2 text-left">Producto / servicio</th>
                <th className="px-3 py-2 text-left">Clave SAT</th>
                <th className="px-3 py-2 text-right">Cant.</th>
                <th className="px-3 py-2 text-right">Precio</th>
                <th className="px-3 py-2 text-right">Descuento</th>
                <th className="px-3 py-2 text-center">Pedimento</th>
              </tr>
            </thead>
            <tbody>
              {p.lineas.map((l) => (
                <tr key={l.posicion} className="border-t">
                  <td className="px-3 py-2 text-muted-foreground">{l.posicion}</td>
                  <td className="px-3 py-2">{l.productoDescripcion}</td>
                  <td className="px-3 py-2 font-mono text-xs">
                    {l.claveProdServSat ?? '—'} · {l.claveUnidadSat ?? '—'}
                  </td>
                  <td className="px-3 py-2 text-right font-mono">
                    {l.cantidad.toFixed(2)}
                  </td>
                  <td className="px-3 py-2 text-right font-mono">
                    {l.precio.toFixed(2)}
                  </td>
                  <td className="px-3 py-2 text-right font-mono">
                    {l.descuento.toFixed(2)}
                  </td>
                  <td className="px-3 py-2 text-center">
                    {l.requierePedimento ? 'Sí' : '—'}
                  </td>
                </tr>
              ))}
            </tbody>
            <tfoot>
              <tr className="border-t bg-muted/30 font-semibold">
                <td className="px-3 py-2" colSpan={6}>
                  Total
                </td>
                <td className="px-3 py-2 text-right font-mono">
                  {p.total.toFixed(2)} {p.moneda}
                </td>
              </tr>
              {p.ranura != null && p.ranura > 0 && (
                <tr className="border-t text-amber-700 dark:text-amber-400">
                  <td className="px-3 py-2 text-xs" colSpan={6}>
                    Ranura (descuento del pedido — la factura va por el total;
                    se documenta con nota de crédito y la caja cobra el neto)
                  </td>
                  <td className="px-3 py-2 text-right font-mono">
                    −{p.ranura.toFixed(2)} {p.moneda}
                  </td>
                </tr>
              )}
            </tfoot>
          </table>
        </div>
      </section>

      <section className="space-y-2">
        <h2 className="text-sm font-medium">
          Historial de comprobantes ({comprobantes.length})
        </h2>
        {comprobantesLoading ? (
          <div className="h-16 w-full animate-pulse rounded bg-muted" />
        ) : comprobantes.length === 0 ? (
          <p className="rounded-md border border-dashed px-3 py-4 text-center text-xs text-muted-foreground">
            El pedido aún no tiene comprobantes emitidos.
          </p>
        ) : (
          <div className="overflow-x-auto rounded-md border">
            <table className="w-full text-sm">
              <thead className="bg-muted/50">
                <tr>
                  <th className="px-3 py-2 text-left">Folio</th>
                  <th className="px-3 py-2 text-left">UUID</th>
                  <th className="px-3 py-2 text-right">Total</th>
                  <th className="px-3 py-2 text-left">Estado</th>
                  <th className="px-3 py-2 text-center">Vigente</th>
                  <th className="px-3 py-2 text-right">Acción</th>
                </tr>
              </thead>
              <tbody>
                {comprobantes.map((c) => (
                  <tr key={c.id} className="border-t hover:bg-muted/30">
                    <td className="px-3 py-2 font-mono text-xs">{c.folio}</td>
                    <td className="px-3 py-2 font-mono text-xs">
                      {c.uuid ? `${c.uuid.slice(0, 8)}…${c.uuid.slice(-4)}` : '—'}
                    </td>
                    <td className="px-3 py-2 text-right font-mono">
                      {c.total.toFixed(2)} {c.moneda}
                    </td>
                    <td className="px-3 py-2">
                      <ChipTimbrado estado={c.estado} />
                    </td>
                    <td className="px-3 py-2 text-center">
                      {c.vigente ? (
                        <span className="rounded-full bg-emerald-100 px-2 py-0.5 text-xs font-medium text-emerald-800">
                          Vigente
                        </span>
                      ) : (
                        '—'
                      )}
                    </td>
                    <td className="px-3 py-2 text-right">
                      <Link
                        to="/facturacion/facturas/$id"
                        params={{ id: c.id }}
                        className="text-sm font-medium text-primary hover:underline"
                      >
                        Ver
                      </Link>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      {/* ── Trazabilidad documento-céntrica (ANT-PR3, doc 13) ────── */}
      <TrazabilidadFacturacion raiz="pedido" id={p.id} />
    </div>
  );
}

export function ChipEstadoPedido({ estado }: { estado: string }) {
  const tono =
    estado === 'Facturado'
      ? 'bg-emerald-100 text-emerald-800'
      : estado === 'Excepcion'
        ? 'bg-rose-100 text-rose-800'
        : estado === 'Cancelado'
          ? 'bg-zinc-200 text-zinc-700'
          : estado === 'Bloqueado'
            ? 'bg-amber-100 text-amber-800'
            : 'bg-sky-100 text-sky-800';
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
        tono,
      )}
    >
      {ETIQUETA_ESTADO_PEDIDO[estado] ?? estado}
    </span>
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
