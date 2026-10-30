import { useState } from 'react';
import { Ban, LockOpen, TriangleAlert } from 'lucide-react';
import { toast } from 'sonner';
import { Alert, AlertDescription } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Textarea } from '@/components/ui/textarea';
import {
  Dialog,
  DialogBody,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { useBloqueoProveedor } from '@/modules/datos-maestros/api';
import type { ProveedorDetalle } from '@/modules/datos-maestros/api/types';
import { codigoSoporte, esApiError } from '@/lib/api';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Bloqueo total del proveedor (86e3tactf): no se paga ninguna factura pendiente ni se levantan
 * OC nuevas. No cambia el estatus de alta. Muestra el aviso con el motivo y, con el permiso
 * `datos_maestros.proveedores.bloquear`, la acción de bloquear o desbloquear (motivo de 10 a 500
 * caracteres). Desbloquea quien lo pidió o quien tenga el permiso de reemplazo; el backend decide.
 */
export function ProveedorBloqueo({ proveedor }: { proveedor: ProveedorDetalle }) {
  const puedeBloquear = useHasPermission(PermisosCanonicos.DatosMaestrosProveedoresBloquear);
  const bloqueado = proveedor.bloqueado === true;
  const [abierto, setAbierto] = useState(false);
  const [motivo, setMotivo] = useState('');
  const mutacion = useBloqueoProveedor(bloqueado ? 'desbloquear' : 'bloquear');
  const motivoValido = motivo.trim().length >= 10 && motivo.trim().length <= 500;
  const verbo = bloqueado ? 'Desbloquear' : 'Bloquear';

  function confirmar() {
    mutacion.mutate(
      { id: proveedor.id, motivo: motivo.trim() },
      {
        onSuccess: () => {
          toast.success(bloqueado ? 'Proveedor desbloqueado.' : 'Proveedor bloqueado.');
          setAbierto(false);
          setMotivo('');
        },
        onError: (error) =>
          toast.error(
            esApiError(error) ? error.problem.title : `No se pudo ${verbo.toLowerCase()} al proveedor.`,
            esApiError(error) ? { description: error.problem.detail ?? codigoSoporte(error) } : undefined,
          ),
      },
    );
  }

  return (
    <>
      {bloqueado && (
        <Alert variant="warning">
          <TriangleAlert className="size-4" aria-hidden="true" />
          <AlertDescription>
            <span className="font-semibold">Proveedor bloqueado:</span> no se paga ninguna factura ni se
            levantan órdenes de compra nuevas. Motivo: {proveedor.motivoBloqueo ?? '—'}
          </AlertDescription>
        </Alert>
      )}
      {puedeBloquear && (
        <div>
          <Button variant="outline" size="sm" onClick={() => setAbierto(true)}>
            {bloqueado ? (
              <LockOpen className="mr-1.5 h-4 w-4" aria-hidden="true" />
            ) : (
              <Ban className="mr-1.5 h-4 w-4" aria-hidden="true" />
            )}
            {verbo} proveedor
          </Button>
        </div>
      )}
      <Dialog open={abierto} onOpenChange={setAbierto}>
        <DialogContent size="sm">
          <DialogHeader>
            <DialogTitle>{verbo} proveedor</DialogTitle>
            <DialogDescription>
              {bloqueado
                ? 'Se podrá pagar al proveedor y levantarle órdenes de compra. Solo desbloquea quien pidió el bloqueo, o quien tenga permiso para hacerlo en su lugar. El motivo queda en la bitácora.'
                : 'No se pagará ninguna factura pendiente ni se levantarán órdenes de compra nuevas con este proveedor. Se siguen capturando facturas y recibiendo las órdenes ya autorizadas. El motivo queda en la bitácora.'}
            </DialogDescription>
          </DialogHeader>
          <DialogBody>
            <label htmlFor="motivo-bloqueo-proveedor" className="text-sm text-ink">
              Motivo (obligatorio, 10 a 500 caracteres)
            </label>
            <Textarea
              id="motivo-bloqueo-proveedor"
              value={motivo}
              maxLength={500}
              onChange={(e) => setMotivo(e.target.value)}
            />
          </DialogBody>
          <DialogFooter>
            <Button variant="ghost" onClick={() => setAbierto(false)}>
              Cancelar
            </Button>
            <Button disabled={!motivoValido || mutacion.isPending} onClick={confirmar}>
              {mutacion.isPending ? 'Guardando…' : verbo}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}
