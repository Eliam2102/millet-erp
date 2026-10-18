import { PageHeader } from '@/components/erp/PageHeader';
import { useState } from 'react';
import { Link, useParams } from '@tanstack/react-router';
import { ArrowLeft, Check, Send, Truck, X } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { ErrorState, DetalleSkeleton } from '@/components/erp';
import { useDevolucionProveedor } from '@/features/almacen/api/useDevolucionesProveedor';
import {
  EstadoDevolucionProveedor,
} from '@/features/almacen/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';
import { EvidenciasManager } from '@/features/almacen/components/EvidenciasManager';
import {
  AutorizarDevolucionConfirm,
  RechazarDevolucionDialog,
  RegistrarSalidaDevolucionSheet,
  SolicitarAutorizacionConfirm,
} from '@/features/almacen/components/DevolucionAccionesDialogs';
import { EstadoAlmacenBadge } from '@/features/almacen/components/EstadoAlmacenBadge';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { formatCantidad } from '@/lib/format-cantidad';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { useDecimalesUnidad } from '@/components/erp/forms/useDecimalesUnidad';

const FROM = '/_app/almacen/devoluciones/proveedor/$id' as const;

/**
 * <c>P6 — Detalle de devolución a proveedor</c> (doc 07 §FE-F4-PR1).
 * Muestra cabecera + líneas + evidencias + acciones según estado:
 *
 * <list type="bullet">
 *   <item><b>Borrador</b>: agregar evidencias (uno o más) + solicitar
 *     autorización (gateada por <c>devoluciones-proveedor.iniciar</c>).</item>
 *   <item><b>EnAutorizacion</b>: autorizar/rechazar
 *     (<c>devoluciones-proveedor.autorizar</c>, Dirección).</item>
 *   <item><b>Autorizada</b>: registrar salida (<c>devoluciones-proveedor.registrar</c>,
 *     Almacenista).</item>
 *   <item><b>Registrada</b>: esperar NC fiscal del proveedor (info).</item>
 *   <item><b>ConciliadaConNcFiscal</b> / <b>Rechazada</b>: terminal.</item>
 * </list>
 */
export function DevolucionProveedorDetallePage() {
  const { id } = useParams({ from: FROM });
  const query = useDevolucionProveedor(id);
  const decimalesUnidad = useDecimalesUnidad();

  const puedeIniciar = useHasPermission(
    PermisosCanonicos.AlmacenDevolucionesProveedorIniciar,
  );
  const puedeAutorizar = useHasPermission(
    PermisosCanonicos.AlmacenDevolucionesProveedorAutorizar,
  );
  const puedeRegistrar = useHasPermission(
    PermisosCanonicos.AlmacenDevolucionesProveedorRegistrar,
  );

  const [solicitarOpen, setSolicitarOpen] = useState(false);
  const [autorizarOpen, setAutorizarOpen] = useState(false);
  const [rechazarOpen, setRechazarOpen] = useState(false);
  const [registrarSalidaOpen, setRegistrarSalidaOpen] = useState(false);

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-2">
        <Button asChild variant="ghost" size="sm">
          <Link to="/almacen/devoluciones" search={{}}>
            <ArrowLeft className="mr-2 size-3.5" />
            Devoluciones
          </Link>
        </Button>
      </div>

      {query.isError ? (
        <ErrorState
          title="No se pudo cargar la devolución"
          problem={esApiError(query.error) ? query.error.problem : undefined}
          onRetry={() => query.refetch()}
        />
      ) : query.isLoading ? (
        <DetalleSkeleton />
      ) : query.data == null ? null : (
        <div className="space-y-6">
          <PageHeader
            titulo="Devolución a proveedor"
            estado={
              <>
                <EstadoAlmacenBadge tipo="devolucion-proveedor" estado={query.data.estado} />
              </>
            }
            descripcion={
              <>
                Solicitada el{' '}
                <DateTimeDisplay value={query.data.solicitadaAt} variant="datetime" />
                {query.data.autorizadaAt && (
                  <>
                    {' '}
                    · Autorizada el{' '}
                    <DateTimeDisplay value={query.data.autorizadaAt} variant="datetime" />
                  </>
                )}
                {query.data.registradaAt && (
                  <>
                    {' '}
                    · Registrada el{' '}
                    <DateTimeDisplay value={query.data.registradaAt} variant="datetime" />
                  </>
                )}
              </>
            }
            acciones={
              <>
                <Acciones
                  estado={query.data.estado}
                  tieneEvidencias={query.data.evidencias.length > 0}
                  puedeIniciar={puedeIniciar}
                  puedeAutorizar={puedeAutorizar}
                  puedeRegistrar={puedeRegistrar}
                  onSolicitar={() => setSolicitarOpen(true)}
                  onAutorizar={() => setAutorizarOpen(true)}
                  onRechazar={() => setRechazarOpen(true)}
                  onRegistrarSalida={() => setRegistrarSalidaOpen(true)}
                />
              </>
            }
          />

          <section className="grid grid-cols-1 gap-x-6 gap-y-2 rounded-md border p-4 md:grid-cols-2">
            {/* Razón social resuelta en backend (ADR-0042); si el puerto no
                resolvió, cae al id truncado (nunca el GUID completo). */}
            <Campo
              label="Proveedor"
              valor={
                query.data.proveedorNombre ??
                `${query.data.proveedorId.slice(0, 8)}…`
              }
              mono={!query.data.proveedorNombre}
            />
            <Campo
              label="Recepción origen"
              valor={query.data.recepcionOrigenId ?? '—'}
              mono
            />
            <Campo
              label="OC origen"
              valor={query.data.ordenCompraOrigenId ?? '—'}
              mono
            />
            <Campo
              label="Factura proveedor origen"
              valor={query.data.facturaProveedorOrigenId ?? '—'}
              mono
            />
            <Campo
              label="Sub-almacén origen"
              valor={query.data.subAlmacenOrigenId ?? '—'}
              mono
            />
            <Campo
              label="Folio movimiento salida"
              valor={query.data.folioMovimientoSalida ?? '—'}
              mono
            />
            <div className="md:col-span-2">
              <Campo label="Motivo" valor={query.data.motivo} />
            </div>
            {query.data.motivoRechazo && (
              <div className="md:col-span-2">
                <Campo
                  label="Motivo de rechazo"
                  valor={query.data.motivoRechazo}
                />
              </div>
            )}
            {query.data.notaCreditoFiscalId && (
              <Campo
                label="NC fiscal vinculada"
                valor={
                  query.data.notaCreditoFolio ??
                  `${query.data.notaCreditoFiscalId.slice(0, 8)}…`
                }
                mono={!query.data.notaCreditoFolio}
              />
            )}
          </section>

          <EvidenciasManager
            devolucionId={query.data.id}
            evidencias={query.data.evidencias}
            lineaRevisionId={query.data.lineas.find((l) => l.lineaRecepcionOrigenId)?.lineaRecepcionOrigenId}
            canAdd={
              puedeIniciar &&
              (query.data.estado === EstadoDevolucionProveedor.Borrador ||
                query.data.estado === EstadoDevolucionProveedor.EnAutorizacion)
            }
          />

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
                      <td className="px-3 py-2">
                        {l.articuloClave ? (
                          <>
                            <code className="font-mono text-xs">
                              {l.articuloClave}
                            </code>
                            {l.articuloDescripcion && (
                              <> · {l.articuloDescripcion}</>
                            )}
                          </>
                        ) : (
                          <code
                            className="font-mono text-xs"
                            title={l.articuloId}
                          >
                            {l.articuloId.slice(0, 8)}…
                          </code>
                        )}
                      </td>
                      <td className="py-2 pl-3 pr-4 text-right tabular-nums">
                        {formatCantidad(l.cantidad, decimalesUnidad.porCodigo(l.unidadMedida))}
                      </td>
                      <td className="px-3 py-2">{l.unidadMedida}</td>
                      <td className="py-2 pl-3 pr-4 text-right tabular-nums">
                        <MoneyDisplay amount={l.costoUnitarioMxn} currency="MXN" strong />
                      </td>
                      <td className="py-2 pl-3 pr-4 text-right tabular-nums">
                        <MoneyDisplay amount={l.montoTotalMxn} currency="MXN" strong />
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>
        </div>
      )}

      {/* Dialogs / sheets de acciones */}
      <SolicitarAutorizacionConfirm
        devolucionId={query.data?.id ?? null}
        open={solicitarOpen}
        onOpenChange={setSolicitarOpen}
      />
      <AutorizarDevolucionConfirm
        devolucionId={query.data?.id ?? null}
        open={autorizarOpen}
        onOpenChange={setAutorizarOpen}
      />
      <RechazarDevolucionDialog
        devolucionId={query.data?.id ?? null}
        open={rechazarOpen}
        onOpenChange={setRechazarOpen}
      />
      <RegistrarSalidaDevolucionSheet
        devolucionId={query.data?.id ?? null}
        open={registrarSalidaOpen}
        onOpenChange={setRegistrarSalidaOpen}
      />
    </div>
  );
}

function Acciones({
  estado,
  tieneEvidencias,
  puedeIniciar,
  puedeAutorizar,
  puedeRegistrar,
  onSolicitar,
  onAutorizar,
  onRechazar,
  onRegistrarSalida,
}: {
  estado: EstadoDevolucionProveedor;
  tieneEvidencias: boolean;
  puedeIniciar: boolean;
  puedeAutorizar: boolean;
  puedeRegistrar: boolean;
  onSolicitar: () => void;
  onAutorizar: () => void;
  onRechazar: () => void;
  onRegistrarSalida: () => void;
}) {
  // Acciones según estado actual:
  if (estado === EstadoDevolucionProveedor.Borrador && puedeIniciar) {
    return (
      <div className="flex gap-2">
        <Button
          onClick={onSolicitar}
          disabled={!tieneEvidencias}
          title={
            tieneEvidencias
              ? undefined
              : 'Agrega al menos una evidencia antes de solicitar'
          }
        >
          <Send className="mr-2 size-3.5" />
          Solicitar autorización
        </Button>
      </div>
    );
  }
  if (estado === EstadoDevolucionProveedor.EnAutorizacion && puedeAutorizar) {
    return (
      <div className="flex gap-2">
        <Button onClick={onAutorizar}>
          <Check className="mr-2 size-3.5" />
          Autorizar
        </Button>
        <Button variant="destructive" onClick={onRechazar}>
          <X className="mr-2 size-3.5" />
          Rechazar
        </Button>
      </div>
    );
  }
  if (estado === EstadoDevolucionProveedor.Autorizada && puedeRegistrar) {
    return (
      <div className="flex gap-2">
        <Button onClick={onRegistrarSalida}>
          <Truck className="mr-2 size-3.5" />
          Registrar salida física
        </Button>
      </div>
    );
  }
  if (estado === EstadoDevolucionProveedor.Registrada) {
    return (
      <p className="text-sm text-muted-foreground">
        Esperando NC fiscal del proveedor (CxP la captura cuando llegue).
      </p>
    );
  }
  return null;
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


