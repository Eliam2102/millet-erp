import { useState } from 'react';
import { toast } from 'sonner';
import { useQueryClient } from '@tanstack/react-query';
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent,
  AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import { esConflictoConcurrencia, esPrecondicionRequerida } from '@/lib/api';
import { useConflictDialog } from '@/components/erp/collaboration/conflict-dialog-context';
import { useCambiarEstatusCuenta } from '../api/hooks';
import type { Cuenta } from '../api/types';
import { manejarErrorCuenta } from '../lib/errores';

interface Props {
  cuenta: Cuenta;
  accion: 'desactivar' | 'reactivar';
  onClose: () => void;
}

/** Confirmación de baja lógica / reactivación con el mensaje de efectos. Toast solo tras 2xx. */
export function ConfirmarEstatusCuenta({ cuenta, accion, onClose }: Props) {
  const conflictDialog = useConflictDialog();
  const queryClient = useQueryClient();
  const cambiar = useCambiarEstatusCuenta();
  const [mensaje, setMensaje] = useState<string | null>(null);
  const baja = accion === 'desactivar';

  function confirmar() {
    setMensaje(null);
    cambiar.mutate({ id: cuenta.id, accion }, {
      onSuccess: () => {
        toast.success(baja ? 'Cuenta desactivada' : 'Cuenta reactivada');
        onClose();
      },
      onError: (error) => {
        // Conflicto de versión: se cierra esta confirmación y se abre el diálogo de recarga.
        if (esConflictoConcurrencia(error) || esPrecondicionRequerida(error)) onClose();
        manejarErrorCuenta(error, { conflictDialog, queryClient, setMensaje });
      },
    });
  }

  return (
    <AlertDialog open onOpenChange={(o) => !o && onClose()}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{baja ? 'Desactivar' : 'Reactivar'} cuenta</AlertDialogTitle>
          <AlertDialogDescription asChild>
            <div className="space-y-2">
              <p className="font-medium">{cuenta.codigo} — {cuenta.nombre}</p>
              {baja ? (
                <ul className="list-disc space-y-1 pl-5">
                  <li>La cuenta dejará de aceptar movimientos nuevos.</li>
                  <li>Nada se borra: histórico, orígenes y uso se conservan, y el código no se reutiliza.</li>
                  <li>No desactiva a sus hijas: si tiene hijas activas, el sistema rechazará la baja.</li>
                </ul>
              ) : (
                <ul className="list-disc space-y-1 pl-5">
                  <li>La cuenta volverá a poder recibir movimientos (si es afectable y está validada).</li>
                  <li>Requiere que su cuenta padre esté activa; no reactiva a sus hijas.</li>
                </ul>
              )}
              {mensaje && <p role="alert" className="text-danger-fg">{mensaje}</p>}
            </div>
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={cambiar.isPending}>Cancelar</AlertDialogCancel>
          <AlertDialogAction onClick={(e) => { e.preventDefault(); confirmar(); }} disabled={cambiar.isPending}>
            {cambiar.isPending ? 'Aplicando…' : baja ? 'Desactivar' : 'Reactivar'}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
