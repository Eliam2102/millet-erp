import { useState, type ReactNode } from 'react';
import {
  CheckCircle2,
  ClipboardCheck,
  Send,
  Unlock,
  Wallet,
  XCircle,
} from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import { MoneyDisplay } from '@/components/erp/display/MoneyDisplay';
import { ConfirmarAccionDialog } from '@/components/erp/feedback/ConfirmarAccionDialog';
import { MotivoDialog } from '@/components/erp/feedback/MotivoDialog';
import {
  useAutorizarPorDfViaticos,
  useAutorizarPorJefeViaticos,
  useLiberarComprobacionViaticos,
  useMarcarAnticipoPagadoViaticos,
  useRechazarSolicitudViaticos,
} from '@/features/cxp/api/useViaticos';
import {
  EstadoSolicitudViaticos,
  type SolicitudViaticosListItem,
} from '@/features/cxp/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { useMiEmpleado } from '@/lib/auth/useMiEmpleado';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { esApiError } from '@/lib/api';

/** Campos mínimos de la solicitud: la lista y el detalle los comparten. */
export type SolicitudAccionable = Pick<
  SolicitudViaticosListItem,
  'id' | 'version' | 'estado' | 'destino' | 'montoSolicitado' | 'excedePolitica'
>;

type Dialogo = 'autJefe' | 'autDf' | 'pagado' | 'liberar' | 'rechazar' | null;

export const MOTIVO_SIN_EMPLEADO =
  'Tu usuario no está ligado a un empleado; pide que lo liguen en Administración → Empleados.';

/**
 * Acciones de workflow de una solicitud de viáticos. Misma lógica en la fila de
 * la bandeja y en el sub-topbar del detalle (CXP-29). Las acciones financieras
 * pasan por confirmación con resumen (CXP-14) y el rechazo pide motivo (CXP-13).
 * El estado pendiente es propio de cada instancia: actuar sobre una fila no
 * deshabilita las demás.
 */
export function AccionesViatico({
  s,
  moneda = 'MXN',
  empleadoNombre,
  onComprobar,
  className,
}: {
  s: SolicitudAccionable;
  moneda?: string;
  empleadoNombre?: string | null;
  onComprobar?: () => void;
  className?: string;
}) {
  const autJefe = useAutorizarPorJefeViaticos();
  const autDf = useAutorizarPorDfViaticos();
  const marcarPagado = useMarcarAnticipoPagadoViaticos();
  const liberar = useLiberarComprobacionViaticos();
  const rechazar = useRechazarSolicitudViaticos();
  const puedeAutJefe = useHasPermission(PermisosCanonicos.CuentasPorPagarViaticosAutorizarJefe);
  const puedeAutDf = useHasPermission(PermisosCanonicos.CuentasPorPagarViaticosAutorizarDf);
  const puedeMarcarPagado = useHasPermission(PermisosCanonicos.CuentasPorPagarViaticosMarcarPagado);
  const puedeCapturarComp = useHasPermission(PermisosCanonicos.CuentasPorPagarViaticosCapturarComprobacion);
  const puedeLiberar = useHasPermission(PermisosCanonicos.CuentasPorPagarViaticosLiberar);
  const { tieneEmpleado } = useMiEmpleado();
  const [dialogo, setDialogo] = useState<Dialogo>(null);
  const [errorMotivo, setErrorMotivo] = useState<string | null>(null);

  const pendiente =
    autJefe.isPending || autDf.isPending || marcarPagado.isPending ||
    liberar.isPending || rechazar.isPending;
  const sinEmpleado = tieneEmpleado === false;
  const base = { id: s.id, versionEsperada: s.version };

  function ejecutar(etiqueta: string, p: Promise<unknown>) {
    p.then(
      () => toast.success(etiqueta),
      (error) =>
        toast.error(esApiError(error) ? error.problem.title : 'Error al ejecutar la acción.'),
    ).finally(() => setDialogo(null));
  }

  function confirmar() {
    const idempotencyKey = crypto.randomUUID();
    switch (dialogo) {
      case 'autJefe':
        return ejecutar(
          s.excedePolitica ? 'Autorizada por jefe (escala a DF)' : 'Autorizada por jefe',
          autJefe.mutateAsync({ ...base, idempotencyKey }),
        );
      case 'autDf':
        return ejecutar(
          'Autorizada por Dirección de Finanzas',
          autDf.mutateAsync({ ...base, idempotencyKey }),
        );
      case 'pagado':
        return ejecutar(
          'Anticipo marcado como pagado (Tesorería)',
          marcarPagado.mutateAsync({ ...base, idempotencyKey }),
        );
      case 'liberar':
        return ejecutar(
          'Comprobación liberada (calculó diferencia)',
          liberar.mutateAsync({ ...base, idempotencyKey }),
        );
    }
  }

  const monto = <MoneyDisplay amount={s.montoSolicitado} currency={moneda} strong />;
  const quien = empleadoNombre ? ` de ${empleadoNombre}` : '';
  const resumen: Record<Exclude<Dialogo, 'rechazar' | null>, { titulo: string; verbo: string; texto: ReactNode }> = {
    autJefe: {
      titulo: 'Autorizar como jefe',
      verbo: 'Autorizar como jefe',
      texto: (
        <>
          Autorizas los viáticos{quien} a {s.destino} por {monto}.
          {s.excedePolitica && ' Excede la política: pasará a Dirección de Finanzas.'}
        </>
      ),
    },
    autDf: {
      titulo: 'Autorizar (Dirección de Finanzas)',
      verbo: 'Autorizar (Dir. Finanzas)',
      texto: (
        <>
          Autorizas el exceso de política de los viáticos{quien} a {s.destino} por {monto}.
        </>
      ),
    },
    pagado: {
      titulo: 'Marcar anticipo como pagado',
      verbo: 'Marcar anticipo como pagado',
      texto: (
        <>
          Se marcará como pagado el anticipo{quien} de {monto} (viáticos a {s.destino}). Úsalo
          solo cuando Tesorería ya lo pagó; no se puede deshacer.
        </>
      ),
    },
    liberar: {
      titulo: 'Liberar comprobación',
      verbo: 'Liberar comprobación',
      texto: (
        <>
          Se liberará la comprobación de los viáticos{quien} a {s.destino} y se calculará la
          diferencia contra el anticipo de {monto}.
        </>
      ),
    },
  };
  const abierto = dialogo !== null && dialogo !== 'rechazar' ? resumen[dialogo] : null;
  const ayudaId = `sin-empleado-${s.id}`;

  return (
    <div className={cn('flex flex-wrap items-center justify-end gap-1 empty:hidden', className)}>
      {s.estado === EstadoSolicitudViaticos.Solicitada && puedeAutJefe && (
        <>
          <Button
            variant="outline"
            size="sm"
            onClick={() => setDialogo('autJefe')}
            disabled={pendiente || sinEmpleado}
            title={sinEmpleado ? MOTIVO_SIN_EMPLEADO : undefined}
            aria-describedby={sinEmpleado ? ayudaId : undefined}
          >
            <CheckCircle2 className="mr-1 size-3.5" aria-hidden="true" />
            Autorizar como jefe
          </Button>
          {sinEmpleado && (
            <span id={ayudaId} className="sr-only">
              {MOTIVO_SIN_EMPLEADO}
            </span>
          )}
        </>
      )}
      {s.estado === EstadoSolicitudViaticos.RequiereDireccionFinanzas && puedeAutDf && (
        <Button variant="outline" size="sm" onClick={() => setDialogo('autDf')} disabled={pendiente}>
          <CheckCircle2 className="mr-1 size-3.5" aria-hidden="true" />
          Autorizar (Dir. Finanzas)
        </Button>
      )}
      {(s.estado === EstadoSolicitudViaticos.AutorizadaPorJefe ||
        s.estado === EstadoSolicitudViaticos.AutorizadaCompleta) &&
        puedeMarcarPagado && (
          <Button variant="outline" size="sm" onClick={() => setDialogo('pagado')} disabled={pendiente}>
            <Wallet className="mr-1 size-3.5" aria-hidden="true" />
            Marcar pagado
          </Button>
        )}
      {s.estado === EstadoSolicitudViaticos.Anticipada && puedeCapturarComp && onComprobar && (
        <Button variant="outline" size="sm" onClick={onComprobar}>
          <ClipboardCheck className="mr-1 size-3.5" aria-hidden="true" />
          Comprobar
        </Button>
      )}
      {s.estado === EstadoSolicitudViaticos.ComprobacionCapturada && puedeLiberar && (
        <Button variant="outline" size="sm" onClick={() => setDialogo('liberar')} disabled={pendiente}>
          <Unlock className="mr-1 size-3.5" aria-hidden="true" />
          Liberar
        </Button>
      )}
      {(s.estado === EstadoSolicitudViaticos.Solicitada ||
        s.estado === EstadoSolicitudViaticos.RequiereDireccionFinanzas) &&
        puedeAutJefe && (
          <Button
            variant="ghost"
            size="sm"
            className="text-danger-fg"
            onClick={() => {
              setErrorMotivo(null);
              setDialogo('rechazar');
            }}
            disabled={pendiente}
          >
            <XCircle className="mr-1 size-3.5" aria-hidden="true" />
            Rechazar
          </Button>
        )}
      {s.estado === EstadoSolicitudViaticos.Solicitada && s.excedePolitica && (
        <span className="ml-1 inline-flex items-center text-xs text-warning-note-fg">
          <Send className="mr-1 size-3.5" aria-hidden="true" />
          Pasará a DF
        </span>
      )}

      {abierto && (
        <ConfirmarAccionDialog
          open
          onOpenChange={(o) => !o && setDialogo(null)}
          titulo={abierto.titulo}
          resumen={abierto.texto}
          verbo={abierto.verbo}
          tono="primario"
          pendiente={pendiente}
          onConfirmar={confirmar}
        />
      )}
      {dialogo === 'rechazar' && (
        <MotivoDialog
          titulo="Rechazar solicitud de viáticos"
          descripcion={<>Viáticos{quien} a {s.destino}. El motivo queda registrado y lo ve el solicitante.</>}
          etiqueta="Motivo del rechazo"
          min={5}
          max={500}
          verbo="Rechazar solicitud"
          error={errorMotivo}
          onCerrar={() => setDialogo(null)}
          onConfirmar={async (motivo) => {
            try {
              await rechazar.mutateAsync({
                ...base,
                command: { motivo },
                idempotencyKey: crypto.randomUUID(),
              });
              toast.success('Solicitud rechazada');
              setDialogo(null);
            } catch (e) {
              setErrorMotivo(
                esApiError(e) ? (e.problem.detail ?? e.problem.title) : 'No se pudo rechazar la solicitud.',
              );
            }
          }}
        />
      )}
    </div>
  );
}
