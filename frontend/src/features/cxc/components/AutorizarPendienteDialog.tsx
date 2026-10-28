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
import { useAutorizarLiberacionPendiente } from '@/features/cxc/api/useLiberaciones';
import type { DecisionLiberacionResponse } from '@/features/cxc/api/types';
import { formatoCobertura, formatoMonto } from '@/features/cxc/lib/glosario';
import {
  AutorizarPendienteSchema,
  type AutorizarPendienteValues,
} from '@/features/cxc/schemas/liberacion';
import { applyServerErrors, esApiError, useFormIdempotencyKey } from '@/lib/api';
import {
  avisoResultadoAutorizar,
  mensajeErrorAutorizar,
} from '@/features/cxc/lib/autorizar-pendiente';

/**
 * <c>&lt;AutorizarPendienteDialog/&gt;</c> — C7.3 (cuestionario 04 R3):
 * el gerente de CxC autoriza con motivo obligatorio un pedido cuya línea
 * cubre entre 50 % y 99 %. El backend recalcula el crédito: si ya alcanza
 * libera sin autorización, si bajó de 50 % o no hay línea retiene; si sigue
 * parcial, la autorización es de un solo uso y se consume en el acto
 * (Matriz I7).
 */
export function AutorizarPendienteDialog({
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
          <DialogTitle>Autorizar liberación</DialogTitle>
          <DialogDescription>
            La línea de crédito cubre solo parte del pedido. Al autorizar se vuelve a calcular el
            crédito del cliente; si sigue cubriendo al menos el 50 %, el pedido queda liberado con
            autorización. Tu motivo queda en la auditoría.
          </DialogDescription>
        </DialogHeader>
        {decision && <FormAutorizar decision={decision} onClose={() => onOpenChange(false)} />}
      </DialogContent>
    </Dialog>
  );
}

function FormAutorizar({
  decision,
  onClose,
}: {
  decision: DecisionLiberacionResponse;
  onClose: () => void;
}) {
  const idempotencyKey = useFormIdempotencyKey();
  const autorizar = useAutorizarLiberacionPendiente();
  const form = useForm<AutorizarPendienteValues>({
    resolver: zodResolver(AutorizarPendienteSchema),
    defaultValues: { motivo: '' },
  });

  function onSubmit(values: AutorizarPendienteValues) {
    autorizar.mutate(
      { decisionId: decision.id, body: { motivo: values.motivo }, idempotencyKey },
      {
        onSuccess: (res) => {
          const aviso = avisoResultadoAutorizar(res);
          const opciones = aviso.detalle ? { description: aviso.detalle } : undefined;
          if (aviso.tipo === 'warning') toast.warning(aviso.titulo, opciones);
          else toast.success(aviso.titulo, opciones);
          onClose();
        },
        onError: (error) => {
          if (
            esApiError(error) &&
            applyServerErrors(form as unknown as Parameters<typeof applyServerErrors>[0], error)
          ) {
            return;
          }
          const { titulo, detalle } = mensajeErrorAutorizar(error);
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
            <Label htmlFor="motivoAutorizar" className="text-xs">
              Motivo <span className="text-danger-fg">*</span>
            </Label>
            <Input
              id="motivoAutorizar"
              placeholder="p. ej. Pago en tránsito confirmado por tesorería"
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
        <Button type="button" variant="ghost" onClick={onClose} disabled={autorizar.isPending}>
          Cancelar
        </Button>
        <Button type="submit" disabled={autorizar.isPending}>
          {autorizar.isPending ? 'Autorizando…' : 'Autorizar y liberar'}
        </Button>
      </DialogFooter>
    </form>
  );
}

/** Datos de la pendiente que se va a resolver (autorizar o rechazar). */
export function ResumenPendiente({ decision }: { decision: DecisionLiberacionResponse }) {
  return (
    <dl className="mb-4 grid grid-cols-2 gap-x-4 gap-y-1 text-sm">
      <dt className="text-ink-muted">Pedido</dt>
      <dd className="font-mono">{decision.pedidoRef}</dd>
      <dt className="text-ink-muted">Monto</dt>
      <dd className="font-mono tabular-nums">
        {formatoMonto(decision.montoPedido, decision.moneda)}
      </dd>
      <dt className="text-ink-muted">Crédito al decidir</dt>
      <dd className="font-mono tabular-nums">
        {formatoMonto(decision.creditoDisponibleSnapshot, decision.moneda)}
      </dd>
      <dt className="text-ink-muted">Cobertura</dt>
      <dd className="font-medium text-warning-fg">
        {formatoCobertura(decision.porcentajeCobertura) ?? '—'}
      </dd>
    </dl>
  );
}
