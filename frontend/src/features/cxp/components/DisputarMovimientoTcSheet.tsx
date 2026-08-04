import { hoyLocalISO } from '@/lib/datetime';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import { AlertTriangle } from 'lucide-react';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Textarea } from '@/components/ui/textarea';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import {
  DisputarMovimientoTcSchema,
  type DisputarMovimientoTcValues,
} from '@/features/cxp/schemas/estados-cuenta-tc';
import { useDisputarMovimientoTc } from '@/features/cxp/api/useEstadosCuentaTc';
import type { MovimientoTc } from '@/features/cxp/api/types';

/**
 * <c>&lt;DisputarMovimientoTcSheet/&gt;</c> — marca un movimiento TC
 * como en disputa (cargo desconocido o fraudulento). El movimiento
 * pasa a estado <c>EnDisputa</c> y queda excluido del cierre del
 * estado de cuenta hasta resolver (§8.5 del anexo TC).
 *
 * <para>La resolución (FueLegitimo true/false) usa otro endpoint;
 * típicamente la hace el titular o supervisor TC.</para>
 */
export interface DisputarMovimientoTcSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  movimiento: MovimientoTc | null;
}

export function DisputarMovimientoTcSheet({
  open,
  onOpenChange,
  movimiento,
}: DisputarMovimientoTcSheetProps) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="sm:max-w-md">
        <SheetHeader>
          <SheetTitle>Disputar movimiento TC</SheetTitle>
          <SheetDescription>
            {movimiento
              ? `Disputa de ${movimiento.merchantNormalizado} (${movimiento.montoMxn.toFixed(2)} MXN del ${movimiento.fechaMovimiento}).`
              : 'Selecciona un movimiento.'}
          </SheetDescription>
        </SheetHeader>
        {open && movimiento && (
          <Form
            key={movimiento.id}
            movimiento={movimiento}
            onClose={() => onOpenChange(false)}
          />
        )}
      </SheetContent>
    </Sheet>
  );
}

interface FormProps {
  movimiento: MovimientoTc;
  onClose: () => void;
}

function Form({ movimiento, onClose }: FormProps) {
  const idempotencyKey = useFormIdempotencyKey();
  const disputar = useDisputarMovimientoTc();
  const hoy = hoyLocalISO();

  const form = useForm<DisputarMovimientoTcValues>({
    resolver: zodResolver(DisputarMovimientoTcSchema),
    defaultValues: {
      motivo: '',
      fechaInicio: hoy,
    },
  });

  function onSubmit(values: DisputarMovimientoTcValues) {
    disputar.mutate(
      {
        id: movimiento.id,
        versionEsperada: movimiento.version,
        command: values,
        idempotencyKey,
      },
      {
        onSuccess: () => {
          toast.success('Movimiento marcado en disputa');
          onClose();
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (
              applyServerErrors(
                form as unknown as Parameters<typeof applyServerErrors>[0],
                error,
              )
            )
              return;
            toast.error(error.problem.title);
            return;
          }
          toast.error('Error al disputar.');
        },
      },
    );
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4 px-4">
      <div className="rounded-md border border-amber-300 bg-amber-50 px-3 py-2 text-xs text-amber-900">
        <AlertTriangle className="mr-1 inline h-3 w-3" />
        El movimiento quedará <strong>excluido del cierre</strong> del
        estado de cuenta hasta resolver la disputa. Si se confirma como
        legítimo después, vuelve al flujo normal.
      </div>

      <div className="space-y-1">
        <Label htmlFor="motivo" className="text-xs">
          Motivo de la disputa <span className="text-destructive">*</span>
        </Label>
        <Textarea
          id="motivo"
          rows={4}
          placeholder="Ej.: Cargo desconocido. No reconozco el merchant. Posible fraude."
          {...form.register('motivo')}
        />
        {form.formState.errors.motivo && (
          <p className="text-xs text-destructive">
            {form.formState.errors.motivo.message}
          </p>
        )}
      </div>

      <div className="space-y-1">
        <Label htmlFor="fechaInicio" className="text-xs">
          Fecha inicio de la disputa{' '}
          <span className="text-destructive">*</span>
        </Label>
        <Input
          id="fechaInicio"
          type="date"
          {...form.register('fechaInicio')}
        />
      </div>

      <SheetFooter className="px-0">
        <Button
          type="button"
          variant="ghost"
          onClick={onClose}
          disabled={disputar.isPending}
        >
          Cancelar
        </Button>
        <Button
          type="submit"
          variant="destructive"
          disabled={disputar.isPending}
        >
          {disputar.isPending ? 'Disputando…' : 'Marcar en disputa'}
        </Button>
      </SheetFooter>
    </form>
  );
}
