import { Link, useParams } from '@tanstack/react-router';
import { ArrowLeft } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { ErrorState, TableSkeleton } from '@/components/erp';
import { useRecepcion } from '@/features/almacen/api/useRecepciones';
import {
  EstadoMovimiento,
  EstadoMovimientoLabels,
} from '@/features/almacen/api/types';
import { esApiError } from '@/lib/api';
import { cn } from '@/lib/utils';

/**
 * <c>P2 — Detalle de recepción</c> (doc 07 §FE-F2-PR1). Vista de solo
 * lectura del movimiento (folio, fecha, sub-almacén, OC, CFDI, factura,
 * observaciones) + líneas con artículo, cantidad, UM, costo y monto.
 *
 * <para>Esta primera iteración muestra los datos crudos; cuando entren
 * los componentes <c>&lt;ArticuloAutocomplete/&gt;</c> y
 * <c>&lt;SaldoChip/&gt;</c>, se enriquece para mostrar nombres y
 * existencias. UI de cancelar (si es Borrador) llega cuando el sheet
 * de captura esté completo y el backend exponga el endpoint de
 * cancelación de borradores (PLATFORM-TODO en F2-PR2).</para>
 */
const FROM = '/_app/almacen/recepciones/$id' as const;

export function RecepcionDetallePage() {
  const { id } = useParams({ from: FROM });
  const query = useRecepcion(id);

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-2">
        <Button asChild variant="ghost" size="sm">
          <Link to="/almacen/recepciones" search={{}}>
            <ArrowLeft className="mr-2 h-4 w-4" />
            Recepciones
          </Link>
        </Button>
      </div>

      {query.isError ? (
        <ErrorState
          title="No se pudo cargar la recepción"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <TableSkeleton
          rows={4}
          columns={[
            { width: 'w-40' },
            { width: 'w-48' },
            { width: 'w-32' },
            { width: 'w-32' },
            { width: 'w-32' },
          ]}
        />
      ) : query.data == null ? null : (
        <div className="space-y-6">
          <header className="space-y-1">
            <div className="flex items-center gap-3">
              <h1 className="text-2xl font-semibold tracking-tight font-mono">
                {query.data.folio}
              </h1>
              <EstadoMovimientoBadge estado={query.data.estado} />
            </div>
            <p className="text-sm text-muted-foreground">
              Fecha de movimiento: {query.data.fechaMovimiento}
              {query.data.registradoAt && (
                <>
                  {' '}
                  · Registrado:{' '}
                  {new Date(query.data.registradoAt).toLocaleString('es-MX')}
                </>
              )}
            </p>
          </header>

          <section className="grid grid-cols-1 gap-x-6 gap-y-2 rounded-md border p-4 md:grid-cols-2">
            <Campo
              label="Sub-almacén"
              valor={
                query.data.subAlmacenNombre
                  ? query.data.subAlmacenClave
                    ? `${query.data.subAlmacenClave} · ${query.data.subAlmacenNombre}`
                    : query.data.subAlmacenNombre
                  : query.data.subAlmacenId
              }
              mono={!query.data.subAlmacenNombre}
            />
            <Campo
              label="Orden de compra"
              valor={
                query.data.ordenCompraFolio ?? query.data.ordenCompraId ?? '—'
              }
              mono={
                query.data.ordenCompraFolio == null &&
                query.data.ordenCompraId != null
              }
            />
            {/* Referencias CxP resueltas en backend (ADR-0042): UUID fiscal
                del SAT / folio del proveedor; si el puerto no resolvió, cae
                al id truncado (nunca el GUID interno completo). */}
            <Campo
              label="CFDI recibido (folio fiscal)"
              valor={
                query.data.cfdiUuidFiscal ??
                (query.data.cfdiRecibidoId
                  ? `${query.data.cfdiRecibidoId.slice(0, 8)}…`
                  : '—')
              }
              mono
            />
            <Campo
              label="Factura proveedor"
              valor={
                query.data.facturaFolio ??
                (query.data.facturaId
                  ? `${query.data.facturaId.slice(0, 8)}…`
                  : '—')
              }
              mono={!query.data.facturaFolio}
            />
            {query.data.observaciones && (
              <div className="md:col-span-2">
                <Campo label="Observaciones" valor={query.data.observaciones} />
              </div>
            )}
          </section>

          <section className="space-y-2">
            <h2 className="text-sm font-semibold uppercase tracking-wide text-muted-foreground">
              Líneas ({query.data.lineas.length})
            </h2>
            <div className="overflow-x-auto rounded-md border">
              <table className="w-full text-sm">
                <thead className="bg-muted/50">
                  <tr>
                    <th className="px-3 py-2 text-left">#</th>
                    <th className="px-3 py-2 text-left">Artículo</th>
                    <th className="px-3 py-2 text-right">Cantidad</th>
                    <th className="px-3 py-2 text-left">UM</th>
                    <th className="px-3 py-2 text-right">Costo unitario</th>
                    <th className="px-3 py-2 text-right">Monto</th>
                  </tr>
                </thead>
                <tbody>
                  {query.data.lineas.map((l) => (
                    <tr key={l.id} className="border-t">
                      <td className="px-3 py-2">{l.posicion}</td>
                      {l.articuloDescripcion ? (
                        <td className="px-3 py-2">
                          {l.articuloClave && (
                            <span className="font-mono text-xs text-muted-foreground">
                              {l.articuloClave}
                            </span>
                          )}
                          <div>{l.articuloDescripcion}</div>
                        </td>
                      ) : (
                        <td className="px-3 py-2 font-mono text-xs">
                          {l.articuloId}
                        </td>
                      )}
                      <td className="px-3 py-2 text-right font-mono">
                        {l.cantidad.toLocaleString('es-MX', {
                          minimumFractionDigits: 2,
                          maximumFractionDigits: 4,
                        })}
                      </td>
                      <td className="px-3 py-2">{l.unidadMedida}</td>
                      <td className="px-3 py-2 text-right font-mono">
                        {formatearMonto(l.costoUnitarioMxn)}
                      </td>
                      <td className="px-3 py-2 text-right font-mono">
                        {formatearMonto(l.montoTotalMxn)}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>
        </div>
      )}
    </div>
  );
}

function Campo({
  label,
  valor,
  mono,
}: {
  label: string;
  valor: string;
  mono?: boolean;
}) {
  return (
    <div>
      <div className="text-xs uppercase tracking-wide text-muted-foreground">
        {label}
      </div>
      <div className={mono ? 'font-mono text-xs break-all' : 'text-sm'}>
        {valor}
      </div>
    </div>
  );
}

function EstadoMovimientoBadge({ estado }: { estado: EstadoMovimiento }) {
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium',
        estado === EstadoMovimiento.Borrador && 'bg-slate-200 text-slate-700',
        estado === EstadoMovimiento.Validado && 'bg-blue-100 text-blue-800',
        estado === EstadoMovimiento.Registrado &&
          'bg-emerald-100 text-emerald-800',
        estado === EstadoMovimiento.Cancelado && 'bg-rose-100 text-rose-800',
      )}
    >
      {EstadoMovimientoLabels[estado]}
    </span>
  );
}

function formatearMonto(v: number): string {
  return new Intl.NumberFormat('es-MX', {
    style: 'currency',
    currency: 'MXN',
    minimumFractionDigits: 2,
  }).format(v);
}
