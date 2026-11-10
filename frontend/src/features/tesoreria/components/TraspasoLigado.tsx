import { useState } from 'react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { esApiError, useBodyScopedIdempotencyKey } from '@/lib/api';
import { useRevertirTraspaso } from '../api/useTesoreria';
import type { MovimientoBancarioResponse } from '../api/types';
import { MotivoDialog } from './MotivoDialog';

/** M-203: el movimiento es un lado de un traspaso; revertirlo revierte los dos lados. */
export function TraspasoLigado({
  movimiento,
  revertido,
}: {
  movimiento: MovimientoBancarioResponse;
  revertido: boolean;
}) {
  const [abierto, setAbierto] = useState(false);
  const mutation = useRevertirTraspaso();
  const keyFor = useBodyScopedIdempotencyKey();
  return (
    <section className="space-y-2 rounded-lg bg-surface-card p-4 shadow-card-flat">
      <h2 className="text-md font-semibold">Traspaso entre cuentas propias</h2>
      <p className="text-sm text-ink-secondary">
        Este movimiento es un lado de un traspaso ligado
        {movimiento.traspasoMotivo ? `: ${movimiento.traspasoMotivo}` : ''}. No cuenta en el flujo de
        efectivo y se revierte completo (los dos lados).
      </p>
      {!revertido && (
        <Button variant="outline" onClick={() => setAbierto(true)}>
          Revertir traspaso
        </Button>
      )}
      <MotivoDialog
        open={abierto}
        onOpenChange={setAbierto}
        titulo="Revertir traspaso"
        descripcion="Se crea un contramovimiento en cada cuenta y los saldos regresan al anterior. Indica el motivo."
        confirmLabel="Revertir"
        pending={mutation.isPending}
        onConfirm={(motivo) => {
          const command = { movimientoId: movimiento.id, motivo: motivo.trim() };
          mutation.mutate(
            { ...command, idempotencyKey: keyFor(command) },
            {
              onSuccess: () => {
                toast.success('Traspaso revertido');
                setAbierto(false);
              },
              onError: (e) =>
                toast.error(
                  esApiError(e) ? (e.problem.detail ?? e.problem.title) : 'No se pudo revertir el traspaso',
                ),
            },
          );
        }}
      />
    </section>
  );
}
