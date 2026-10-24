import { IndicadorAlmacenBadge } from '../components/IndicadorAlmacenBadge';
import { ResumenImportes } from '@/components/erp/display/ResumenImportes';
import { PageHeader } from '@/components/erp/PageHeader';
import { formatCcMaquinaLabel } from '@/features/centros-costo/lib/cc-maquina-label';
import { TrazabilidadComprasDialog } from '@/components/erp/trazabilidad/TrazabilidadComprasDialog';
import { TipoDocumentoTrazabilidad } from '@/components/erp/trazabilidad/types';
import { useState } from 'react';
import { toast } from 'sonner';
import { Link, useParams } from '@tanstack/react-router';
import { ArrowLeft } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { AvisoEnLinea } from '@/components/erp/feedback/AvisoEnLinea';
import { ErrorState, DetalleSkeleton } from '@/components/erp';
import {
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { ActivosRecibidosOc } from '@/features/almacen/components/ActivosRecibidosOc';
import { useRecepcion } from '@/features/almacen/api/useRecepciones';
import { EstadoMovimiento } from '@/features/almacen/api/types';
import { esApiError } from '@/lib/api';
import { EstadoAlmacenBadge } from '@/features/almacen/components/EstadoAlmacenBadge';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { formatCantidad } from '@/lib/format-cantidad';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { useDecimalesUnidad } from '@/components/erp/forms/useDecimalesUnidad';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { CancelarRecepcionDialog } from '@/features/almacen/components/CancelarRecepcionDialog';
import { NuevaRecepcionSheet } from '@/features/almacen/components/NuevaRecepcionSheet';

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
  const puedeCancelarPermiso = useHasPermission(PermisosCanonicos.AlmacenRecepcionesCancelar);
  const puedeRegistrar = useHasPermission(PermisosCanonicos.AlmacenEntradasRegistrar);
  const [cancelarAbierto, setCancelarAbierto] = useState(false);
  const [capturaAbierta, setCapturaAbierta] = useState(false);
  const lineasEnRevision = query.data?.lineas.filter((l) => l.enRevision).length ?? 0;

  return (
    <div className="space-y-5">
      <div className="flex items-center gap-2 md:hidden">
        <Button asChild variant="ghost" size="sm">
          <Link to="/almacen/recepciones" search={{}}>
            <ArrowLeft className="mr-2 size-3.5" />
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
        <DetalleSkeleton />
      ) : query.data == null ? null : (
        <div className="space-y-5">
          <PageHeader
            titulo={query.data.folio}
            estado={
              <>
                <EstadoAlmacenBadge tipo="movimiento" estado={query.data.estado} />
              </>
            }
            descripcion={
              <>
                Fecha de movimiento:{' '}
                <DateTimeDisplay value={query.data.fechaMovimiento} variant="media" />
                {query.data.registradoAt && (
                  <>
                    {' '}
                    · Registrado:{' '}
                    <DateTimeDisplay value={query.data.registradoAt} variant="media" />
                  </>
                )}
              </>
            }
            acciones={
              <>
                {puedeCancelarPermiso &&
                  query.data.estado === EstadoMovimiento.Registrado &&
                  query.data.registradaPorOtroUsuario === true && (
                    <Button variant="secondary-danger" onClick={() => setCancelarAbierto(true)}>
                      Cancelar recepción
                    </Button>
                  )}
                {puedeRegistrar &&
                  query.data.estado === EstadoMovimiento.Cancelado &&
                  query.data.ordenCompraId && (
                    <Button onClick={() => setCapturaAbierta(true)}>
                      Capturar la recepción correcta
                    </Button>
                  )}
                <TrazabilidadComprasDialog tipo={TipoDocumentoTrazabilidad.Recepcion} id={id} />
              </>
            }
            mono
          />

          <dl className="grid grid-cols-1 gap-4 rounded-lg bg-surface-card p-4 shadow-card-flat sm:grid-cols-4">
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
                <dt className="text-xs text-ink-muted">Orden de compra</dt>
                <dd>
                  <Link
                    to="/compras/ordenes/$id"
                    params={{ id: query.data.ordenCompraId }}
                    className="font-mono text-sm text-brand hover:underline"
                  >
                    {query.data.ordenCompraFolio ?? query.data.ordenCompraId}
                  </Link>
                </dd>
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
                (query.data.cfdiRecibidoId ? `${query.data.cfdiRecibidoId.slice(0, 8)}…` : '—')
              }
              mono
            />
            <Campo
              label="Factura proveedor"
              valor={
                query.data.facturaFolio ??
                (query.data.facturaId ? `${query.data.facturaId.slice(0, 8)}…` : '—')
              }
              mono={!query.data.facturaFolio}
            />
            {query.data.importacion && (
              <>
                <Campo label="Pedimento" valor={query.data.importacion.numeroPedimento} mono />
                <Campo label="BL / guía aérea" valor={query.data.importacion.numeroBl} mono />
                <Campo label="Contenedor" valor={query.data.importacion.numeroContenedor} mono />
                <Campo
                  label="Aduana"
                  valor={[query.data.importacion.aduana, query.data.importacion.aduanaNombre]
                    .filter(Boolean)
                    .join(' · ')}
                />
                <Campo
                  label="Clave de pedimento"
                  valor={[
                    query.data.importacion.clavePedimento,
                    query.data.importacion.clavePedimentoNombre,
                  ]
                    .filter(Boolean)
                    .join(' · ')}
                />
              </>
            )}
            {query.data.observaciones && (
              <div className="md:col-span-2">
                <Campo label="Observaciones" valor={query.data.observaciones} />
              </div>
            )}
          </dl>

          {(query.data.canceladaAt || query.data.registradoPorNombre) && (
            <section
              aria-labelledby="bitacora-recepcion"
              className="space-y-3 rounded-lg bg-surface-card p-4 shadow-card-flat"
            >
              <h2
                id="bitacora-recepcion"
                className="text-sm font-semibold uppercase tracking-wide text-ink-muted"
              >
                Bitácora
              </h2>
              <dl className="grid grid-cols-1 gap-4 sm:grid-cols-4">
                {query.data.registradoPorNombre && (
                  <Campo label="Registró" valor={query.data.registradoPorNombre} />
                )}
                {query.data.canceladaAt && (
                  <>
                    <Campo label="Canceló" valor={query.data.canceladaPorNombre ?? '—'} />
                    <div>
                      <dt className="text-xs text-ink-muted">Fecha de cancelación</dt>
                      <dd className="text-sm">
                        <DateTimeDisplay value={query.data.canceladaAt} variant="media" />
                      </dd>
                    </div>
                    <Campo
                      label="Movimientos de cancelación"
                      valor={(query.data.foliosCancelacion ?? []).join(', ') || '—'}
                      mono
                    />
                    <div className="sm:col-span-4">
                      <dt className="text-xs text-ink-muted">Motivo</dt>
                      <dd className="whitespace-pre-wrap break-words text-sm">
                        {query.data.motivoCancelacion ?? '—'}
                      </dd>
                    </div>
                  </>
                )}
              </dl>
            </section>
          )}

          <section className="space-y-2">
            <h2 className="text-sm font-semibold uppercase tracking-wide text-ink-muted">
              Partidas ({query.data.lineas.length})
            </h2>
            {lineasEnRevision > 0 && (
              <AvisoEnLinea tono="advertencia">
                {lineasEnRevision} {lineasEnRevision === 1 ? 'partida' : 'partidas'} en material en
                revisión: no están disponibles hasta el dictamen del Gerente de almacén/calidad.
              </AvisoEnLinea>
            )}
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
                    {query.data.importacion && <TableHead>Fracción arancelaria</TableHead>}
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
                          {l.enRevision && (
                            <div className="mt-1 space-y-0.5">
                              <IndicadorAlmacenBadge estado="revision" />
                              {l.motivoDano && (
                                <p className="text-xs text-ink-muted">{l.motivoDano}</p>
                              )}
                            </div>
                          )}
                        </TableCell>
                      ) : (
                        <TableCell className="font-mono text-xs">{l.articuloId}</TableCell>
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
                      <TableCell className="font-mono text-xs">{l.ubicacionClave ?? '—'}</TableCell>
                      <TableCell className="text-sm text-ink">
                        {l.centroCostoId
                          ? formatCcMaquinaLabel({
                              clave: l.centroCostoClave,
                              nombre: l.centroCostoNombre,
                            })
                          : '—'}
                      </TableCell>
                      {query.data.importacion && (
                        <TableCell className="font-mono text-xs">
                          {l.fraccionArancelaria ?? '—'}
                        </TableCell>
                      )}
                      <TableCell className="text-right tabular-nums">
                        <MoneyDisplay amount={l.costoUnitarioMxn} currency="MXN" strong />
                      </TableCell>
                      <TableCell className="text-right tabular-nums">
                        <MoneyDisplay amount={l.montoTotalMxn} currency="MXN" strong />
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </TableContainer>
            <ResumenImportes
              moneda="MXN"
              total={query.data.lineas.reduce((t, l) => t + l.montoTotalMxn, 0)}
            />
          </section>
          {query.data.ordenCompraId && (
            <ActivosRecibidosOc ordenCompraId={query.data.ordenCompraId} />
          )}

          <CancelarRecepcionDialog
            recepcion={query.data}
            open={cancelarAbierto}
            onOpenChange={setCancelarAbierto}
            onCancelada={() => {
              toast.success('Recepción cancelada. Ya puedes capturar la correcta.');
            }}
          />
          {capturaAbierta && (
            <NuevaRecepcionSheet
              open
              onOpenChange={setCapturaAbierta}
              ordenCompraIdInicial={query.data.ordenCompraId ?? undefined}
            />
          )}
        </div>
      )}
    </div>
  );
}

function Campo({ label, valor, mono }: { label: string; valor: string; mono?: boolean }) {
  return (
    <div>
      <dt className="text-xs text-ink-muted">{label}</dt>
      <dd className={mono ? 'font-mono text-xs break-all' : 'text-sm'}>{valor}</dd>
    </div>
  );
}
