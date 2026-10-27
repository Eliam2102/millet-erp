import { FormGrid, FormCampo } from '@/components/erp';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import {
  Dialog,
  DialogContent,
  DialogBody,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { useRechazarLiberacionPendiente } from '@/features/cxc/api/useLiberaciones';
import type { DecisionLiberacionResponse } from '@/features/cxc/api/types';
import {
  RechazarPendienteSchema,
  type RechazarPendienteValues,
} from '@/features/cxc/schemas/liberacion';
import { applyServerErrors, esApiError, useFormIdempotencyKey } from '@/lib/api';
import { mensajeErrorAutorizar } from '@/features/cxc/lib/autorizar-pendiente';
import { ResumenPendiente } from '@/features/cxc/components/AutorizarPendienteDialog';

/**
 * <c>&lt;RechazarPendienteDialog/&gt;</c> — C7.3: el gerente de CxC
 * rechaza con motivo obligatorio una pendiente de autorización. El pedido
 * queda retenido (nueva decisión ligada a la pendiente).
 */
export function RechazarPendienteDialog({
  decision,
  onOpenChange,
}: {
  decision: DecisionLiberacionResponse | null;
  onOpenChange: (open: boolean) => void;
}) {
  const open = decision != null;
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent size="md">
        <DialogHeader>
          <DialogTitle>Rechazar liberación</DialogTitle>
          <DialogDescription>
            El pedido quedará retenido y la pendiente ya no podrá autorizarse. Tu motivo queda en la
            auditoría.
          </DialogDescription>
        </DialogHeader>
        {decision && <FormRechazar decision={decision} onClose={() => onOpenChange(false)} />}
      </DialogContent>
    </Dialog>
  );
}

function FormRechazar({
  decision,
  onClose,
}: {
  decision: DecisionLiberacionResponse;
  onClose: () => void;
}) {
  const idempotencyKey = useFormIdempotencyKey();
  const rechazar = useRechazarLiberacionPendiente();
  const form = useForm<RechazarPendienteValues>({
    resolver: zodResolver(RechazarPendienteSchema),
    defaultValues: { motivo: '' },
  });

  function onSubmit(values: RechazarPendienteValues) {
    rechazar.mutate(
      { decisionId: decision.id, body: { motivo: values.motivo }, idempotencyKey },
      {
        onSuccess: (res) => {
          toast.warning(`Pedido ${res.pedidoRef}: retenido por rechazo de CxC.`);
          onClose();
        },
        onError: (error) => {
          if (
            esApiError(error) &&
            applyServerErrors(form as unknown as Parameters<typeof applyServerErrors>[0], error)
          ) {
            return;
          }
          const { titulo, detalle } = mensajeErrorAutorizar(error, 'rechazar');
          toast.error(titulo, detalle ? { description: detalle } : undefined);
        },
      },
    );
  }

  return (
    <form
      onSubmit={form.handleSubmit(onSubmit)}
      noValidate
      className="flex min-h-0 flex-1 flex-col"
    >
      <DialogBody>
        <ResumenPendiente decision={decision} />
        <FormGrid>
          <FormCampo span={12} className="space-y-1">
            <Label htmlFor="motivoRechazar" className="text-xs">
              Motivo <span className="text-danger-fg">*</span>
            </Label>
            <Input
              id="motivoRechazar"
              placeholder="p. ej. Cliente con pagos atrasados"
              maxLength={254}
              {...form.register('motivo')}
            />
            {form.formState.errors.motivo && (
              <p className="text-xs text-danger-fg">{form.formState.errors.motivo.message}</p>
            )}
          </FormCampo>
        </FormGrid>
      </DialogBody>
      <DialogFooter>
        <Button type="button" variant="ghost" onClick={onClose} disabled={rechazar.isPending}>
          Cancelar
        </Button>
        <Button type="submit" variant="destructive" disabled={rechazar.isPending}>
          {rechazar.isPending ? 'Rechazando…' : 'Rechazar y retener'}
        </Button>
      </DialogFooter>
    </form>
  );
}
