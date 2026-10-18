import { useState } from 'react';
import { useAuthStore } from '@/lib/auth/auth-store';
import { Link, useNavigate } from '@tanstack/react-router';
import { Pencil, X } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { AvisoEnLinea, ConfirmarAccionDialog, ErrorState, TableSkeleton } from '@/components/erp';
import { PageHeader } from '@/components/erp/PageHeader';
import { DateTimeDisplay } from '@/components/erp/display/DateTimeDisplay';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import {
  useAutorizarDevolucion,
  useCancelarDevolucion,
  useDevolucion,
  useRechazarDevolucion,
} from '@/features/tesoreria/api/useTesoreria';
import {
  EstadoDevolucionCliente,
  ORIGEN_DEVOLUCION_LABELS,
  OrigenDevolucionCliente,
  type DevolucionClienteResponse,
} from '@/features/tesoreria/api/types';
import { EstadoDevolucionBadge } from '@/features/tesoreria/components/Badges';
import { EditarDevolucionForm } from '@/features/tesoreria/components/EditarDevolucionForm';
import { MotivoDialog } from '@/features/tesoreria/components/MotivoDialog';
import { PagarDevolucionDialog } from '@/features/tesoreria/components/PagarDevolucionDialog';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError, useBodyScopedIdempotencyKey } from '@/lib/api';

/**
 * <c>Detalle de devolución a cliente</c> (T9.6, P3 página completa): origen,
 * importe, cliente, cuenta destino y seguimiento. Editar y cancelar los hace
 * quien solicitó; autorizar y rechazar, otra persona con permiso de autorizar.
 */
export function DetalleDevolucion({ id }: { id: string }) {
  const navigate = useNavigate();
  const query = useDevolucion(id);
  const keyFor = useBodyScopedIdempotencyKey();
  const puedeSolicitar = useHasPermission(PermisosCanonicos.TesoreriaDevolucionesSolicitar);
  const puedeAutorizar = useHasPermission(PermisosCanonicos.TesoreriaDevolucionesAutorizar);
  const puedePagar = useHasPermission(PermisosCanonicos.TesoreriaDevolucionesPagar);
  const autorizar = useAutorizarDevolucion();
  const rechazar = useRechazarDevolucion();
  const cancelar = useCancelarDevolucion();

  const usuarioId = useAuthStore((s) => s.user?.id);
  const [editando, setEditando] = useState(false);
  const [confirmandoAutorizar, setConfirmandoAutorizar] = useState(false);
  const [rechazando, setRechazando] = useState(false);
  const [cancelando, setCancelando] = useState(false);
  const [pagando, setPagando] = useState(false);
  const [aviso, setAviso] = useState<string | null>(null);

  if (query.isError) {
    return (
      <ErrorState
        title="No se pudo cargar la devolución"
        problem={esApiError(query.error) ? query.error.problem : undefined}
        onRetry={() => query.refetch()}
      />
    );
  }
  if (query.isLoading || query.data == null) {
    return <TableSkeleton rows={6} columns={[{ width: 'w-full' }]} />;
  }
  const d = query.data;

  function alError(error: unknown, fallback: string) {
    if (esApiError(error)) {
      setAviso(error.problem.detail ?? error.problem.title);
    } else toast.error(fallback);
  }

  function ejecutarAutorizar(dev: DevolucionClienteResponse) {
    setAviso(null);
    const body = { id: dev.id, versionEsperada: dev.version };
    autorizar.mutate(
      { ...body, idempotencyKey: keyFor(body) },
      {
        onSuccess: () => {
          toast.success('Devolución autorizada');
          setConfirmandoAutorizar(false);
        },
        onError: (e) => {
          setConfirmandoAutorizar(false);
          alError(e, 'No se pudo autorizar la devolución.');
        },
      },
    );
  }

  function ejecutarConMotivo(tipo: 'rechazar' | 'cancelar', dev: DevolucionClienteResponse, motivo: string) {
    setAviso(null);
    const body = { id: dev.id, motivo, versionEsperada: dev.version };
    const mutacion = tipo === 'rechazar' ? rechazar : cancelar;
    mutacion.mutate(
      { ...body, idempotencyKey: keyFor({ tipo, ...body }) },
      {
        onSuccess: () => {
          toast.success(tipo === 'rechazar' ? 'Devolución rechazada' : 'Solicitud cancelada');
          setRechazando(false);
          setCancelando(false);
        },
        onError: (e) => alError(e, 'No se pudo completar la acción.'),
      },
    );
  }

  const solicitada = d.estado === EstadoDevolucionCliente.Solicitada;
  const destinoCompleto = d.destinoBanco != null && d.destinoClabe != null && d.destinoTitular != null;
  const cliente = d.clienteRazonSocial ?? d.clienteClave ?? 'Cliente sin nombre';
  const soyYo = usuarioId != null && d.solicitadaPor === usuarioId;

  return (
    <div className="space-y-4">
      <PageHeader
        titulo="Devolución a cliente"
        estado={<EstadoDevolucionBadge estado={d.estado} />}
        descripcion={
          <>
            {ORIGEN_DEVOLUCION_LABELS[d.origen]} · {cliente} · <MoneyDisplay amount={d.importe} currency={d.moneda} />
          </>
        }
        acciones={
          <Button
            variant="ghost"
            size="icon"
            aria-label="Cerrar"
            onClick={() => navigate({ to: '/tesoreria/devoluciones' })}
            data-print="hidden"
          >
            <X className="h-4 w-4" />
          </Button>
        }
      />

      {aviso && (
        <AvisoEnLinea tono="error" titulo="No se pudo completar la acción">
          {aviso}
        </AvisoEnLinea>
      )}

      <div data-print="hidden" className="flex flex-wrap items-center gap-2">
        {solicitada && puedeSolicitar && !editando && (soyYo || puedeAutorizar || d.solicitadaPor == null) && (
          <>
            <Button variant="outline" size="sm" onClick={() => setEditando(true)}>
              <Pencil className="mr-1 h-3 w-3" aria-hidden="true" />
              Editar
            </Button>
            <Button variant="outline" size="sm" onClick={() => setCancelando(true)}>
              Cancelar solicitud
            </Button>
          </>
        )}
        {solicitada && puedeAutorizar && !editando && (
          <>
            <Button
              variant="outline"
              size="sm"
              disabled={soyYo}
              aria-describedby={soyYo ? 'autorizar-bloqueo' : undefined}
              onClick={() => setRechazando(true)}
            >
              Rechazar
            </Button>
            <Button
              size="sm"
              disabled={soyYo || !destinoCompleto}
              aria-describedby={soyYo || !destinoCompleto ? 'autorizar-bloqueo' : undefined}
              onClick={() => setConfirmandoAutorizar(true)}
            >
              Autorizar
            </Button>
            {(soyYo || !destinoCompleto) && (
              <span id="autorizar-bloqueo" className="text-xs text-ink-muted">
                {soyYo
                  ? 'Quien solicita no puede autorizar; pide la autorización al Gerente o al Jefe de Tesorería'
                  : 'Captura banco, CLABE y titular antes de autorizar'}
              </span>
            )}
          </>
        )}
        {d.estado === EstadoDevolucionCliente.Autorizada && puedePagar && (
          <Button size="sm" onClick={() => setPagando(true)}>
            Registrar pago
          </Button>
        )}
      </div>

      {editando && <EditarDevolucionForm devolucion={d} onCerrar={() => setEditando(false)} />}

      <Seccion titulo="Origen">
        {d.origen === OrigenDevolucionCliente.DepositoErroneo && d.movimientoOrigenId != null ? (
          <Dato etiqueta="Depósito">
            <Link
              to="/tesoreria/movimientos/$id"
              params={{ id: d.movimientoOrigenId }}
              className="text-brand hover:underline"
            >
              Ver depósito
            </Link>
          </Dato>
        ) : (
          <>
            <Dato etiqueta="Folio de la nota de crédito">
              <span className="font-mono">{d.notaCreditoFolio ?? '—'}</span>
            </Dato>
            <Dato etiqueta="UUID">
              <span className="font-mono text-xs">{d.notaCreditoUuid ?? '—'}</span>
            </Dato>
          </>
        )}
        <Dato etiqueta="Motivo">{d.motivo}</Dato>
      </Seccion>

      <Seccion titulo="Importe">
        <Dato etiqueta="A devolver">
          <MoneyDisplay amount={d.importe} currency={d.moneda} strong />
        </Dato>
        <Dato etiqueta="Máximo del origen">
          <MoneyDisplay amount={d.importeMaximoOrigen} currency={d.moneda} />
        </Dato>
        {solicitada && (
          <Dato etiqueta="Disponible (sin contar esta)">
            <MoneyDisplay amount={d.disponibleOrigen} currency={d.moneda} />
          </Dato>
        )}
      </Seccion>

      <Seccion titulo="Cliente">
        <Dato etiqueta="Razón social">{d.clienteRazonSocial ?? '—'}</Dato>
        <Dato etiqueta="Clave">
          <span className="font-mono">{d.clienteClave ?? '—'}</span>
        </Dato>
      </Seccion>

      <Seccion titulo="Cuenta destino">
        <Dato etiqueta="Banco">{d.destinoBanco ?? '—'}</Dato>
        <Dato etiqueta="CLABE">
          <span className="font-mono">{d.destinoClabe ?? '—'}</span>
        </Dato>
        <Dato etiqueta="Titular">{d.destinoTitular ?? '—'}</Dato>
      </Seccion>

      <Seccion titulo="Seguimiento">
        <Dato etiqueta="Solicitada">
          <DateTimeDisplay value={d.solicitadaEn} variant="datetime" />
        </Dato>
        <Dato etiqueta="Autorizada">
          <DateTimeDisplay value={d.autorizadaEn} variant="datetime" />
        </Dato>
        <Dato etiqueta="Pagada">
          <DateTimeDisplay value={d.pagadaEn} variant="datetime" />
        </Dato>
        {d.motivoRechazo != null && <Dato etiqueta="Motivo del rechazo">{d.motivoRechazo}</Dato>}
        {d.motivoCancelacion != null && <Dato etiqueta="Motivo de la cancelación">{d.motivoCancelacion}</Dato>}
        {d.movimientoPagoId != null && (
          <Dato etiqueta="Pago">
            <Link
              to="/tesoreria/movimientos/$id"
              params={{ id: d.movimientoPagoId }}
              className="text-brand hover:underline"
            >
              Ver pago
            </Link>
          </Dato>
        )}
      </Seccion>

      <PagarDevolucionDialog
        devolucion={d}
        abierto={pagando}
        onCerrar={() => setPagando(false)}
      />
      <ConfirmarAccionDialog
        open={confirmandoAutorizar}
        onOpenChange={setConfirmandoAutorizar}
        titulo="Autorizar devolución"
        resumen={
          <>
            Autorizar la devolución de <MoneyDisplay amount={d.importe} currency={d.moneda} strong /> {d.moneda} a{' '}
            {cliente}.
          </>
        }
        verbo="Autorizar devolución"
        tono="primario"
        onConfirmar={() => ejecutarAutorizar(d)}
        pendiente={autorizar.isPending}
      />
      <MotivoDialog
        open={rechazando}
        onOpenChange={setRechazando}
        titulo="Rechazar devolución"
        descripcion="La solicitud queda rechazada y no se puede reabrir. Indica el motivo."
        confirmLabel="Rechazar"
        onConfirm={(motivo) => ejecutarConMotivo('rechazar', d, motivo)}
        pending={rechazar.isPending}
      />
      <MotivoDialog
        open={cancelando}
        onOpenChange={setCancelando}
        titulo="Cancelar solicitud"
        descripcion="La solicitud se cancela y libera el importe comprometido. Indica el motivo."
        confirmLabel="Cancelar solicitud"
        onConfirm={(motivo) => ejecutarConMotivo('cancelar', d, motivo)}
        pending={cancelar.isPending}
      />
    </div>
  );
}

function Seccion({ titulo, children }: { titulo: string; children: React.ReactNode }) {
  return (
    <section className="space-y-2 rounded-lg border border-line-row bg-surface-card p-4">
      <h2 className="text-sm font-semibold text-ink">{titulo}</h2>
      <dl className="grid grid-cols-1 gap-x-6 gap-y-3 text-sm md:grid-cols-3">{children}</dl>
    </section>
  );
}

function Dato({ etiqueta, children }: { etiqueta: string; children: React.ReactNode }) {
  return (
    <div>
      <dt className="text-xs text-ink-muted">{etiqueta}</dt>
      <dd>{children}</dd>
    </div>
  );
}
