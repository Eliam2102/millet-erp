import { PageHeader } from '@/components/erp/PageHeader';
import { formatCcMaquinaLabel } from '@/features/centros-costo/lib/cc-maquina-label';
import { TrazabilidadComprasDialog } from '@/components/erp/trazabilidad/TrazabilidadComprasDialog';
import { TipoDocumentoTrazabilidad } from '@/components/erp/trazabilidad/types';
import { Link, useParams } from '@tanstack/react-router';
import { ArrowLeft } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { ErrorState, TableSkeleton } from '@/components/erp';
import {
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { useRecepcion } from '@/features/almacen/api/useRecepciones';
import {
} from '@/features/almacen/api/types';
import { esApiError } from '@/lib/api';
import { EstadoAlmacenBadge } from '@/features/almacen/components/EstadoAlmacenBadge';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { formatCantidad } from '@/lib/format-cantidad';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { useDecimalesUnidad } from '@/components/erp/forms/useDecimalesUnidad';

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
  const decimalesUnidad = useDecimalesUnidad();

  return (
    <div className="space-y-4">
      {query.data && <TrazabilidadComprasDialog tipo={TipoDocumentoTrazabilidad.Recepcion} id={id} />}
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
          <PageHeader
            titulo={query.data.folio}
            estado={
              <>
                <EstadoAlmacenBadge tipo="movimiento" estado={query.data.estado} />
              </>
            }
            descripcion={
              <>
                Fecha de movimiento: {query.data.fechaMovimiento}
                {query.data.registradoAt && (
                  <>
                    {' '}
                    · Registrado:{' '}
                    <DateTimeDisplay value={query.data.registradoAt} variant="datetime" />
                  </>
                )}
              </>
            }
            mono
          />

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
            {query.data.ordenCompraId ? (
              <div>
                <div className="text-xs uppercase tracking-wide text-muted-foreground">
                  Orden de compra
                </div>
                <Link
                  to="/compras/ordenes/$id"
                  params={{ id: query.data.ordenCompraId }}
                  className="font-mono text-sm text-brand hover:underline"
                >
                  {query.data.ordenCompraFolio ?? query.data.ordenCompraId}
                </Link>
              </div>
            ) : (
              <Campo label="Orden de compra" valor="—" />
            )}
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
            <TableContainer className="rounded-md border">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>#</TableHead>
                    <TableHead>Artículo</TableHead>
                    <TableHead className="text-right">Cantidad</TableHead>
                    <TableHead>UM</TableHead>
                    <TableHead>Ubicación (rack)</TableHead>
                    <TableHead>Centro de costo</TableHead>
                    <TableHead className="text-right">Costo unitario</TableHead>
                    <TableHead className="text-right">Monto</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {query.data.lineas.map((l) => (
                    <TableRow key={l.id}>
                      <TableCell>{l.posicion}</TableCell>
                      {l.articuloDescripcion ? (
                        <TableCell>
                          {l.articuloClave && (
                            <span className="font-mono text-xs text-ink-muted">
                              {l.articuloClave}
                            </span>
                          )}
                          <div>{l.articuloDescripcion}</div>
                        </TableCell>
                      ) : (
                        <TableCell className="font-mono text-xs">
                          {l.articuloId}
                        </TableCell>
                      )}
                      <TableCell className="text-right tabular-nums">
                        {formatCantidad(l.cantidad, decimalesUnidad.porCodigo(l.unidadMedida))}
                      </TableCell>
                      <TableCell>
                        {l.unidadMedida}
                        {l.unidadCapturada && (
                          <span className="block text-xs text-ink-muted">
                            Captura: {l.cantidadCapturada} {l.unidadCapturada}
                          </span>
                        )}
                      </TableCell>
                      <TableCell className="font-mono text-xs">
                        {l.ubicacionClave ?? '—'}
                      </TableCell>
                      <TableCell className="text-sm text-ink">
                        {l.centroCostoId
                          ? formatCcMaquinaLabel({ clave: l.centroCostoClave, nombre: l.centroCostoNombre })
                          : '—'}
                      </TableCell>
                      <TableCell className="text-right tabular-nums">
                        <MoneyDisplay amount={l.costoUnitarioMxn} currency="MXN" strong />
                      </TableCell>
                      <TableCell className="text-right tabular-nums">
                        <MoneyDisplay amount={l.montoTotalMxn} currency="MXN" strong />
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
                <tfoot>
                  <TableRow className="border-t border-line-divider bg-surface-subtle">
                    <TableCell colSpan={7} className="text-right font-semibold">
                      Total
                    </TableCell>
                    <TableCell className="text-right tabular-nums">
                      <MoneyDisplay
                        amount={query.data.lineas.reduce((t, l) => t + l.montoTotalMxn, 0)}
                        currency="MXN"
                        strong
                      />
                    </TableCell>
                  </TableRow>
                </tfoot>
              </Table>
            </TableContainer>
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


