import { Link, useParams, useSearch } from '@tanstack/react-router';
import { X } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { ErrorState } from '@/components/erp';
import { useRepp } from '@/features/facturacion/api/useRepp';
import { esApiError } from '@/lib/api';
import { ChipTimbrado } from '@/features/facturacion/pages/BandejaFacturas';
import { TimbradoFallidoBanner } from '@/features/facturacion/components/TimbradoFallidoBanner';
import { IntentosTimbradoPanel } from '@/features/facturacion/components/IntentosTimbradoPanel';
import { TrazabilidadFacturacion } from '@/features/facturacion/components/TrazabilidadFacturacion';
import type { ReppSearch } from '@/features/facturacion/lib/repp-search-schema';

/**
 * <c>Detalle de un REPP</c> (FE-F6). Cabecera del pago + bandeja de
 * facturas cubiertas (parcialidad, importe, saldo insoluto, ganancia/
 * pérdida cambiaria), con enlace a cada factura.
 */
export function DetalleRepp() {
  const { id } = useParams({ from: '/_app/facturacion/repp/$id' });
  const search = useSearch({ strict: false }) as ReppSearch;
  const query = useRepp(id);

  return (
    <div className="space-y-4">
      {/* Sub-topbar del detalle (§6.5): cerrar vuelve a la bandeja
          preservando los filtros activos. */}
      <div
        className="sticky top-0 z-10 flex items-center justify-end gap-2 border-b bg-background/95 pb-2 backdrop-blur"
        data-print="hidden"
      >
        <Button variant="ghost" size="sm" asChild aria-label="Cerrar detalle">
          <Link to="/facturacion/repp" search={search}>
            <X className="h-4 w-4" />
          </Link>
        </Button>
      </div>

      {query.isError ? (
        <ErrorState
          title="No se pudo cargar el complemento de pago"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading || query.data == null ? (
        <div className="space-y-3">
          <div className="h-8 w-64 animate-pulse rounded bg-muted" />
          <div className="h-40 w-full animate-pulse rounded bg-muted" />
        </div>
      ) : (
        <Contenido r={query.data} />
      )}
    </div>
  );
}

function Contenido({
  r,
}: {
  r: NonNullable<ReturnType<typeof useRepp>['data']>;
}) {
  return (
    <div className="space-y-6">
      <TimbradoFallidoBanner
        comprobanteId={r.id}
        estado={r.estado}
        errorCodigo={r.timbradoErrorCodigo}
        errorMensaje={r.timbradoErrorMensaje}
      />

      <IntentosTimbradoPanel comprobanteId={r.id} />

      <header className="space-y-1">
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="font-mono text-2xl font-semibold">{r.folio}</h1>
          <ChipTimbrado estado={r.estado} />
        </div>
        {r.uuid && (
          <p className="font-mono text-xs text-muted-foreground">UUID {r.uuid}</p>
        )}
      </header>

      <section className="grid grid-cols-1 gap-4 sm:grid-cols-3">
        <Dato label="Receptor">{r.receptorNombre}</Dato>
        <Dato label="Moneda del pago">{r.monedaPago}</Dato>
        <Dato label="Fecha de pago">
          {new Date(r.fechaPago).toLocaleString('es-MX')}
        </Dato>
      </section>

      <section className="space-y-2">
        <h2 className="text-sm font-medium">
          Facturas cubiertas ({r.facturasCubiertas.length})
        </h2>
        <div className="overflow-x-auto rounded-md border">
          <table className="w-full text-sm">
            <thead className="bg-muted/50">
              <tr>
                <th className="px-3 py-2 text-left">Factura</th>
                <th className="px-3 py-2 text-right">Parcialidad</th>
                <th className="px-3 py-2 text-right">Importe pagado</th>
                <th className="px-3 py-2 text-right">Saldo insoluto</th>
                <th className="px-3 py-2 text-right">Gan./pérd. cambiaria</th>
              </tr>
            </thead>
            <tbody>
              {r.facturasCubiertas.map((f) => (
                <tr key={f.facturaVentaId} className="border-t">
                  <td className="px-3 py-2">
                    <Link
                      to="/facturacion/facturas/$id"
                      params={{ id: f.facturaVentaId }}
                      className="font-mono text-xs text-primary hover:underline"
                    >
                      {f.folio ?? f.facturaUuid.slice(0, 8)}
                    </Link>
                  </td>
                  <td className="px-3 py-2 text-right font-mono">
                    {f.numParcialidad}
                  </td>
                  <td className="px-3 py-2 text-right font-mono">
                    {f.importePagado.toFixed(2)}
                  </td>
                  <td className="px-3 py-2 text-right font-mono">
                    {f.saldoInsoluto.toFixed(2)}
                  </td>
                  <td className="px-3 py-2 text-right font-mono">
                    {f.gananciaPerdidaCambiaria.toFixed(2)}
                  </td>
                </tr>
              ))}
            </tbody>
            <tfoot>
              <tr className="border-t bg-muted/30 font-semibold">
                <td className="px-3 py-2" colSpan={2}>
                  Total del pago
                </td>
                <td className="px-3 py-2 text-right font-mono">
                  {r.importeTotalPago.toFixed(2)} {r.monedaPago}
                </td>
                <td colSpan={2} />
              </tr>
            </tfoot>
          </table>
        </div>
      </section>

      {/* ── Trazabilidad documento-céntrica (ANT-PR3, doc 13) ────── */}
      <TrazabilidadFacturacion raiz="comprobante" id={r.id} />
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
