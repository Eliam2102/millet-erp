import { FormGrid, FormCampo } from '@/components/erp';
import { Controller, useForm, useWatch } from 'react-hook-form';
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
import { useCorregirReevaluarLiberacion } from '@/features/cxc/api/useLiberaciones';
import { TipoCorreccionLiberacion, type DecisionLiberacionResponse } from '@/features/cxc/api/types';
import { ClienteSelectorCxc } from '@/features/cxc/components/ClienteSelectorCxc';
import { avisoReevaluacion } from '@/features/cxc/lib/reevaluacion';
import { ETIQUETA_TIPO_CORRECCION, formatoMonto } from '@/features/cxc/lib/glosario';
import {
  CorregirReevaluarSchema,
  type CorregirReevaluarValues,
} from '@/features/cxc/schemas/liberacion';
import { applyServerErrors, esApiError, useFormIdempotencyKey } from '@/lib/api';

/**
 * <c>&lt;CorregirReevaluarDialog/&gt;</c> — M-238: sobre un pedido retenido, corrige el dato que lo retuvo y vuelve
 * a evaluar la liberación. El cliente se corrige aquí; la condición del cliente y el saldo o cobro mal aplicado se
 * corrigen en cartera (estado de cuenta) y aquí se registra qué se corrigió y se reevalúa. Queda el rastro de las
 * dos evaluaciones.
 */
export function CorregirReevaluarDialog({
  decision,
  onOpenChange,
}: {
  decision: DecisionLiberacionResponse | null;
  onOpenChange: (open: boolean) => void;
}) {
  return (
    <Dialog open={decision != null} onOpenChange={onOpenChange}>
      <DialogContent size="md">
        <DialogHeader>
          <DialogTitle>Corregir y reevaluar</DialogTitle>
          <DialogDescription>
            Indica qué dato corregiste del pedido retenido. Se vuelve a evaluar la liberación con el
            crédito actual del cliente y quedan registradas las dos evaluaciones, la de antes y la de
            después.
          </DialogDescription>
        </DialogHeader>
        {decision && <FormCorregir decision={decision} onClose={() => onOpenChange(false)} />}
      </DialogContent>
    </Dialog>
  );
}

function FormCorregir({ decision, onClose }: { decision: DecisionLiberacionResponse; onClose: () => void }) {
  const idempotencyKey = useFormIdempotencyKey();
  const corregir = useCorregirReevaluarLiberacion();
  const form = useForm<CorregirReevaluarValues>({
    resolver: zodResolver(CorregirReevaluarSchema),
    defaultValues: { tipo: TipoCorreccionLiberacion.SaldoAplicado, detalle: '', clienteIdCorregido: undefined },
  });
  const tipo = useWatch({ control: form.control, name: 'tipo' });

  function onSubmit(values: CorregirReevaluarValues) {
    corregir.mutate(
      {
        decisionId: decision.id,
        body: {
          tipo: values.tipo as TipoCorreccionLiberacion,
          detalle: values.detalle,
          clienteIdCorregido: values.tipo === TipoCorreccionLiberacion.Cliente ? values.clienteIdCorregido : null,
        },
        idempotencyKey,
      },
      {
        onSuccess: (res) => {
          const aviso = avisoReevaluacion(res);
          if (aviso.tipo === 'warning') toast.warning(aviso.titulo);
          else toast.success(aviso.titulo);
          onClose();
        },
        onError: (error) => {
          if (esApiError(error) && applyServerErrors(form as unknown as Parameters<typeof applyServerErrors>[0], error))
            return;
          toast.error(esApiError(error) ? error.problem.title : 'No se pudo reevaluar el pedido.', {
            description: esApiError(error) ? error.problem.detail : undefined,
          });
        },
      },
    );
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} noValidate className="flex min-h-0 flex-1 flex-col">
      <DialogBody>
        <dl className="mb-4 grid grid-cols-2 gap-x-4 gap-y-1 text-sm">
          <dt className="text-ink-muted">Pedido</dt>
          <dd className="font-mono">{decision.pedidoRef}</dd>
          <dt className="text-ink-muted">Monto</dt>
          <dd className="font-mono tabular-nums">{formatoMonto(decision.montoPedido, decision.moneda)}</dd>
          <dt className="text-ink-muted">Crédito al decidir</dt>
          <dd className="font-mono tabular-nums">
            {formatoMonto(decision.creditoDisponibleSnapshot, decision.moneda)}
          </dd>
        </dl>
        <FormGrid>
          <FormCampo span={12} className="space-y-1">
            <Label htmlFor="tipoCorreccion" className="text-xs">
              Qué se corrigió <span className="text-danger-fg">*</span>
            </Label>
            <select
              id="tipoCorreccion"
              className="h-9 w-full rounded-md border border-line-control bg-transparent px-3 text-sm shadow-card-flat"
              {...form.register('tipo', { valueAsNumber: true })}
            >
              {Object.values(TipoCorreccionLiberacion).map((t) => (
                <option key={t} value={t}>
                  {ETIQUETA_TIPO_CORRECCION[t]}
                </option>
              ))}
            </select>
            {tipo !== TipoCorreccionLiberacion.Cliente && (
              <p className="text-xs text-ink-muted">
                {tipo === TipoCorreccionLiberacion.SaldoAplicado
                  ? 'Corrige el cobro o movimiento en el estado de cuenta del cliente y luego reevalúa aquí.'
                  : 'Corrige la línea, el plazo o el bloqueo del cliente y luego reevalúa aquí.'}
              </p>
            )}
          </FormCampo>

          {tipo === TipoCorreccionLiberacion.Cliente && (
            <FormCampo span={12} className="space-y-1">
              <Label className="text-xs">
                Cliente correcto <span className="text-danger-fg">*</span>
              </Label>
              <Controller
                name="clienteIdCorregido"
                control={form.control}
                render={({ field }) => (
                  <ClienteSelectorCxc value={field.value || null} onChange={(item) => field.onChange(item?.id ?? '')} />
                )}
              />
              {form.formState.errors.clienteIdCorregido && (
                <p className="text-xs text-danger-fg">{form.formState.errors.clienteIdCorregido.message}</p>
              )}
            </FormCampo>
          )}

          <FormCampo span={12} className="space-y-1">
            <Label htmlFor="detalleCorreccion" className="text-xs">
              Qué corregiste y por qué <span className="text-danger-fg">*</span>
            </Label>
            <Input
              id="detalleCorreccion"
              placeholder="p. ej. Cobro aplicado a la factura correcta"
              maxLength={254}
              {...form.register('detalle')}
            />
            {form.formState.errors.detalle && (
              <p className="text-xs text-danger-fg">{form.formState.errors.detalle.message}</p>
            )}
          </FormCampo>
        </FormGrid>
      </DialogBody>
      <DialogFooter>
        <Button type="button" variant="ghost" onClick={onClose} disabled={corregir.isPending}>
          Cancelar
        </Button>
        <Button type="submit" disabled={corregir.isPending}>
          {corregir.isPending ? 'Reevaluando…' : 'Corregir y reevaluar'}
        </Button>
      </DialogFooter>
    </form>
  );
}
