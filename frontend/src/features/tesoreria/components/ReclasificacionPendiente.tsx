import { useState } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { esApiError, useBodyScopedIdempotencyKey } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useResolverReclasificacion } from '../api/useTesoreria';
import type { ReclasificacionPendienteDto } from '../api/types';
import { MotivoDialog } from './MotivoDialog';

/** M-204 · solicitud de reclasificación a la espera de la segunda firma (autorizar o rechazar con motivo). */
export function ReclasificacionPendiente({
  movimientoId,
  pendiente,
}: {
  movimientoId: string;
  pendiente: ReclasificacionPendienteDto;
}) {
  const puedeAutorizar = useHasPermission(PermisosCanonicos.TesoreriaMovimientosAutorizarReclasificacion);
  const [rechazando, setRechazando] = useState(false);
  const resolver = useResolverReclasificacion();
  const keyFor = useBodyScopedIdempotencyKey();

  const enviar = (rechazo?: { motivo: string }) => {
    const command = { movimientoId, rechazo };
    resolver.mutate(
      { ...command, idempotencyKey: keyFor(command) },
      {
        onSuccess: () => {
          toast.success(rechazo ? 'Reclasificación rechazada' : 'Reclasificación autorizada');
          setRechazando(false);
        },
        onError: (e) =>
          toast.error(esApiError(e) ? (e.problem.detail ?? e.problem.title) : 'No se pudo resolver la reclasificación'),
      },
    );
  };

  return (
    <section className="space-y-2 rounded-lg bg-warning-bg p-4 text-warning-fg shadow-card-flat">
      <h2 className="text-md font-semibold">Reclasificación pendiente de autorizar</h2>
      <p className="text-sm text-ink-secondary">
        Propuesto: <strong>{pendiente.conceptoNuevoNombre ?? 'otro concepto'}</strong>. Motivo: {pendiente.motivo}
      </p>
      {puedeAutorizar ? (
        <div className="flex gap-2">
          <Button disabled={resolver.isPending} onClick={() => enviar()}>
            Autorizar
          </Button>
          <Button variant="outline" disabled={resolver.isPending} onClick={() => setRechazando(true)}>
            Rechazar
          </Button>
        </div>
      ) : (
        <p className="text-xs text-ink-muted">La autoriza otra persona con la facultad de autorizar reclasificaciones.</p>
      )}
      <MotivoDialog
        open={rechazando}
        onOpenChange={setRechazando}
        titulo="Rechazar reclasificación"
        descripcion="El movimiento conserva su concepto actual. Indica el motivo del rechazo."
        confirmLabel="Rechazar"
        onConfirm={(motivo) => enviar({ motivo })}
        pending={resolver.isPending}
      />
    </section>
  );
}
