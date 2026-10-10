import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle, DialogFooter } from '@/components/ui/dialog';
import { ResolverCancelacionOcSchema, type ResolverCancelacionOcValues } from '../schemas/cancelar-doble-firma';

export function ResolverCancelacionDialog({ confirmar, onClose, onSubmit, isPending }: {
  confirmar: boolean; onClose: () => void; onSubmit: (values: ResolverCancelacionOcValues) => Promise<void>; isPending: boolean;
}) {
  const [motivo, setMotivo] = useState('');
  const [error, setError] = useState<string>();
  const accion = confirmar ? 'Confirmar cancelación' : 'Rechazar cancelación';
  return <Dialog open onOpenChange={(open) => { if (!open && !isPending) onClose(); }}>
    <DialogContent>
      <DialogHeader>
        <DialogTitle>{accion}</DialogTitle>
        <DialogDescription>{confirmar
          ? 'Registrarás la segunda firma. La OC quedará cancelada; solo lo no recibido regresará a la requisición.'
          : 'La OC volverá a su estado anterior y podrá recibirse y facturarse de nuevo.'}</DialogDescription>
      </DialogHeader>
      <form className="space-y-4" onSubmit={async (e) => {
        e.preventDefault();
        const result = ResolverCancelacionOcSchema.safeParse({ confirmar, motivo });
        if (!result.success) { setError(result.error.issues[0]?.message); return; }
        setError(undefined);
        await onSubmit(result.data);
      }}>
        <Label htmlFor="motivo-resolucion-cancelacion">Motivo de la decisión</Label>
        <Textarea id="motivo-resolucion-cancelacion" value={motivo} onChange={(e) => setMotivo(e.target.value)} maxLength={500} disabled={isPending} required />
        {error && <p role="alert" className="text-danger-fg">{error}</p>}
        <DialogFooter>
          <Button type="button" variant="secondary" disabled={isPending} onClick={onClose}>Volver</Button>
          <Button type="submit" disabled={isPending} title={isPending ? 'Guardando la decisión' : undefined}>{accion}</Button>
        </DialogFooter>
      </form>
    </DialogContent>
  </Dialog>;
}
