import { hoyLocalISO } from '@/lib/datetime';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
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
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import {
  RegistrarRefundTcSchema,
  type RegistrarRefundTcValues,
} from '@/features/cxp/schemas/estados-cuenta-tc';
import { useRegistrarRefundTc } from '@/features/cxp/api/useEstadosCuentaTc';
import type { MovimientoTc } from '@/features/cxp/api/types';

/**
 * <c>&lt;RegistrarRefundTcSheet/&gt;</c> — registra un refund de un
 * movimiento original (proveedor canceló compra y abonó a la TC).
 * Pre-rellena tarjeta + usuario + UUID del movimiento original desde
 * el contexto.
 *
 * <para>Flujo §5.5 del anexo TC. El backend valida que el movimiento
 * original esté en estado compatible.</para>
 */
export interface RegistrarRefundTcSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  movimientoOriginal: MovimientoTc | null;
}

export function RegistrarRefundTcSheet({
  open,
  onOpenChange,
  movimientoOriginal,
}: RegistrarRefundTcSheetProps) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="sm:max-w-md">
        <SheetHeader>
          <SheetTitle>Registrar refund</SheetTitle>
          <SheetDescription>
            {movimientoOriginal
              ? `Refund del movimiento ${movimientoOriginal.merchantNormalizado} (${movimientoOriginal.montoMxn.toFixed(2)} MXN).`
              : 'Selecciona el movimiento original desde la bandeja.'}
          </SheetDescription>
        </SheetHeader>
        {open && movimientoOriginal && (
          <Form
            key={movimientoOriginal.id}
            original={movimientoOriginal}
            onClose={() => onOpenChange(false)}
          />
        )}
      </SheetContent>
    </Sheet>
  );
}

interface FormProps {
  original: MovimientoTc;
  onClose: () => void;
}

function Form({ original, onClose }: FormProps) {
  const idempotencyKey = useFormIdempotencyKey();
  const registrar = useRegistrarRefundTc();
  const hoy = hoyLocalISO();

  const form = useForm<RegistrarRefundTcValues>({
    resolver: zodResolver(RegistrarRefundTcSchema),
    defaultValues: {
      tarjetaId: original.tarjetaId,
      usuarioQueUsoId: original.usuarioQueUsoId,
      fechaMovimiento: hoy,
      montoOriginal: original.montoOriginal,
      monedaOriginal: original.monedaOriginal,
      tipoCambioCaptura: original.tipoCambioCaptura,
      merchantRaw: `REFUND ${original.merchantNormalizado}`,
      conceptoContable: original.conceptoContable,
      movimientoOriginalId: original.id,
    },
  });

  function onSubmit(values: RegistrarRefundTcValues) {
    registrar.mutate(
      { command: values, idempotencyKey },
      {
        onSuccess: (res) => {
          toast.success(
            `Refund registrado (${res.montoMxn.toFixed(2)} MXN)`,
          );
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
          toast.error('Error al registrar refund.');
        },
      },
    );
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4 px-4">
      <div className="rounded-md border bg-muted/30 px-3 py-2 text-xs">
        <p>
          <strong>Movimiento original:</strong>{' '}
          <span className="font-mono">{original.id.slice(0, 8)}…</span>
        </p>
        <p className="text-muted-foreground">
          Pre-rellenamos los campos desde el movimiento original. Ajusta el
          monto si el refund fue parcial.
        </p>
      </div>

      <div className="grid grid-cols-2 gap-3">
        <Field label="Fecha refund" required>
          <Input type="date" {...form.register('fechaMovimiento')} />
        </Field>
        <Field label="Moneda" required>
          <Input
            maxLength={3}
            className="uppercase"
            {...form.register('monedaOriginal')}
          />
        </Field>
        <Field
          label="Monto refund"
          required
          error={form.formState.errors.montoOriginal?.message}
        >
          <Input
            type="number"
            step="0.01"
            min="0"
            {...form.register('montoOriginal', { valueAsNumber: true })}
          />
        </Field>
        <Field label="Tipo cambio">
          <Input
            type="number"
            step="0.0001"
            min="0"
            {...form.register('tipoCambioCaptura', { valueAsNumber: true })}
          />
        </Field>
      </div>

      <Field
        label="Merchant"
        required
        error={form.formState.errors.merchantRaw?.message}
      >
        <Input {...form.register('merchantRaw')} />
      </Field>

      <Field
        label="Concepto contable"
        required
        error={form.formState.errors.conceptoContable?.message}
      >
        <Input {...form.register('conceptoContable')} />
      </Field>

      <SheetFooter className="px-0">
        <Button
          type="button"
          variant="ghost"
          onClick={onClose}
          disabled={registrar.isPending}
        >
          Cancelar
        </Button>
        <Button type="submit" disabled={registrar.isPending}>
          {registrar.isPending ? 'Registrando…' : 'Registrar refund'}
        </Button>
      </SheetFooter>
    </form>
  );
}

function Field({
  label,
  required,
  error,
  children,
}: {
  label: string;
  required?: boolean;
  error?: string;
  children: React.ReactNode;
}) {
  return (
    <div className="space-y-1">
      <Label className="text-xs">
        {label}
        {required && <span className="ml-1 text-destructive">*</span>}
      </Label>
      {children}
      {error && <p className="text-xs text-destructive">{error}</p>}
    </div>
  );
}
