import { hoyLocalISO } from '@/lib/datetime';
import { Controller, useForm } from 'react-hook-form';
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
  CrearEstadoCuentaTcSchema,
  type CrearEstadoCuentaTcValues,
} from '@/features/cxp/schemas/estados-cuenta-tc';
import { useCrearEstadoCuentaTc } from '@/features/cxp/api/useEstadosCuentaTc';
import { TarjetaSelector } from '@/features/cxp/components/TarjetaSelector';

export interface NuevoEstadoCuentaTcSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function NuevoEstadoCuentaTcSheet({
  open,
  onOpenChange,
}: NuevoEstadoCuentaTcSheetProps) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="sm:max-w-md">
        <SheetHeader>
          <SheetTitle>Nuevo estado de cuenta TC</SheetTitle>
          <SheetDescription>
            Define el periodo y fechas. El archivo del banco se sube en un
            paso posterior desde la bandeja.
          </SheetDescription>
        </SheetHeader>
        {open && <Form onClose={() => onOpenChange(false)} />}
      </SheetContent>
    </Sheet>
  );
}

function Form({ onClose }: { onClose: () => void }) {
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useCrearEstadoCuentaTc();
  const hoy = hoyLocalISO();

  const form = useForm<CrearEstadoCuentaTcValues>({
    resolver: zodResolver(CrearEstadoCuentaTcSchema),
    defaultValues: {
      tarjetaId: '',
      periodoDesde: hoy,
      periodoHasta: hoy,
      fechaCorte: hoy,
      fechaLimitePago: hoy,
    },
  });

  function onSubmit(values: CrearEstadoCuentaTcValues) {
    crear.mutate(
      { command: values, idempotencyKey },
      {
        onSuccess: () => {
          toast.success('Estado de cuenta creado');
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
          toast.error('Error al crear estado de cuenta.');
        },
      },
    );
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} className="space-y-4 px-4">
      <div className="space-y-1">
        <Label className="text-xs">
          Tarjeta <span className="text-destructive">*</span>
        </Label>
        <Controller
          control={form.control}
          name="tarjetaId"
          render={({ field }) => (
            <TarjetaSelector
              value={field.value || null}
              onChange={(id) => field.onChange(id ?? '')}
            />
          )}
        />
        {form.formState.errors.tarjetaId && (
          <p className="text-xs text-destructive">
            {form.formState.errors.tarjetaId.message}
          </p>
        )}
      </div>

      <div className="grid grid-cols-2 gap-3">
        <Field label="Periodo desde">
          <Input type="date" {...form.register('periodoDesde')} />
        </Field>
        <Field label="Periodo hasta" error={form.formState.errors.periodoHasta?.message}>
          <Input type="date" {...form.register('periodoHasta')} />
        </Field>
        <Field label="Fecha corte">
          <Input type="date" {...form.register('fechaCorte')} />
        </Field>
        <Field label="Fecha límite pago">
          <Input type="date" {...form.register('fechaLimitePago')} />
        </Field>
      </div>

      <SheetFooter className="px-0">
        <Button type="button" variant="ghost" onClick={onClose} disabled={crear.isPending}>
          Cancelar
        </Button>
        <Button type="submit" disabled={crear.isPending}>
          {crear.isPending ? 'Creando…' : 'Crear estado de cuenta'}
        </Button>
      </SheetFooter>
    </form>
  );
}

function Field({
  label,
  error,
  children,
}: {
  label: string;
  error?: string;
  children: React.ReactNode;
}) {
  return (
    <div className="space-y-1">
      <Label className="text-xs">{label}</Label>
      {children}
      {error && <p className="text-xs text-destructive">{error}</p>}
    </div>
  );
}
