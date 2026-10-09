import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { MotivoRechazoSelector, MotivoRechazoAplicaA } from '@/features/compras/components/MotivoRechazoSelector';
import { CancelarOcConRecepcionesSchema, type CancelarOcConRecepcionesValues } from '../schemas/cancelar-doble-firma';

export interface DobleFirmaDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  ocFolio: string;
  isPending?: boolean;
  onSubmit: (values: CancelarOcConRecepcionesValues) => Promise<void>;
}

/** Primera firma: cada persona firma con su propia sesión. */
export function DobleFirmaDialog({ open, onOpenChange, ocFolio, isPending, onSubmit }: DobleFirmaDialogProps) {
  const [motivoId, setMotivoId] = useState<string | null>(null);
  const [motivo, setMotivo] = useState('');
  const [error, setError] = useState<string>();
  return (
    <Dialog open={open} onOpenChange={(value) => { if (!isPending) onOpenChange(value); }}>
      <DialogContent data-component="doble-firma-dialog">
        <DialogHeader>
          <DialogTitle>Solicitar cancelación · {ocFolio}</DialogTitle>
          <DialogDescription>
            Registrarás la primera firma. Dirección deberá confirmar con otra persona.
            Mientras la solicitud esté pendiente no se podrá recibir ni facturar contra esta OC.
            Al confirmar, solo lo no recibido volverá al saldo de la requisición.
          </DialogDescription>
        </DialogHeader>
        <form className="space-y-4" onSubmit={async (event) => {
          event.preventDefault();
          const result = CancelarOcConRecepcionesSchema.safeParse({ motivoCancelacionId: motivoId, motivoCancelacionTexto: motivo });
          if (!result.success) { setError(result.error.issues[0]?.message); return; }
          setError(undefined);
          await onSubmit(result.data);
        }}>
          <div className="space-y-1.5">
            <p className="text-xs font-medium text-ink-strong">Motivo de cancelación</p>
            <MotivoRechazoSelector aplicaA={MotivoRechazoAplicaA.Cancelacion} value={motivoId} onChange={setMotivoId} disabled={isPending} />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="motivo-solicitud-cancelacion">Detalle del motivo</Label>
            <Textarea id="motivo-solicitud-cancelacion" value={motivo} onChange={(e) => setMotivo(e.target.value)} maxLength={500} disabled={isPending} required />
          </div>
          {error && <p role="alert" className="text-danger-fg">{error}</p>}
          <DialogFooter>
            <Button type="button" variant="secondary" disabled={isPending} onClick={() => onOpenChange(false)}>Volver</Button>
            <Button type="submit" disabled={isPending} title={isPending ? 'Guardando la solicitud' : undefined}>Solicitar cancelación</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
