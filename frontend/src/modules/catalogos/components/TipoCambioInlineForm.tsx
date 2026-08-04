import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { DatePickerField } from '@/components/erp';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import {
  RegistrarTipoCambioSchema,
  type RegistrarTipoCambioValues,
} from '@/modules/catalogos/schemas/tipo-cambio';
import { useRegistrarTipoCambio } from '@/modules/catalogos/api';
import { OrigenTipoCambio } from '@/modules/catalogos/api/types';

/**
 * <c>&lt;TipoCambioInlineForm/&gt;</c> — inline form (border-dashed
 * primary) que aparece debajo del botón "Registrar tipo de cambio".
 * Mismo patrón visual que <see cref="LineaInlineForm"/> en modo
 * AGREGAR (border-dashed primary/40, bg-primary/5).
 *
 * <para>Default: hoy + Manual + valor 0. POST con Idempotency-Key;
 * onSuccess llama <c>onSaved</c> para que el parent cierre el form.</para>
 */
export interface TipoCambioInlineFormProps {
  monedaId: string;
  onCancel: () => void;
  onSaved?: () => void;
}

function fechaHoyISO(): string {
  const now = new Date();
  const y = now.getFullYear();
  const m = String(now.getMonth() + 1).padStart(2, '0');
  const d = String(now.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

export function TipoCambioInlineForm({
  monedaId,
  onCancel,
  onSaved,
}: TipoCambioInlineFormProps) {
  const idempotencyKey = useFormIdempotencyKey();
  const registrar = useRegistrarTipoCambio();

  const form = useForm<RegistrarTipoCambioValues>({
    resolver: zodResolver(RegistrarTipoCambioSchema),
    defaultValues: {
      fecha: fechaHoyISO(),
      valorEnMxn: 0,
      origen: OrigenTipoCambio.Manual,
    },
  });

  function onSubmit(values: RegistrarTipoCambioValues) {
    registrar.mutate(
      {
        monedaId,
        payload: {
          fecha: values.fecha,
          valorEnMxn: values.valorEnMxn,
          origen: values.origen,
        },
        idempotencyKey,
      },
      {
        onSuccess: () => {
          toast.success('Tipo de cambio registrado');
          form.reset({
            fecha: fechaHoyISO(),
            valorEnMxn: 0,
            origen: OrigenTipoCambio.Manual,
          });
          onSaved?.();
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (error.code === 'TIPO_CAMBIO_DUPLICADO') {
              form.setError('fecha', {
                type: error.code,
                message: 'Ya existe un valor para esta moneda en esa fecha.',
              });
              return;
            }
            if (
              applyServerErrors(
                form as unknown as Parameters<typeof applyServerErrors>[0],
                error,
              )
            ) {
              return;
            }
            toast.error(error.problem.title, {
              description: error.traceId
                ? `Código: ${error.traceId}`
                : undefined,
            });
            return;
          }
          toast.error('Error al registrar el tipo de cambio.');
        },
      },
    );
  }

  return (
    <form
      onSubmit={form.handleSubmit(onSubmit)}
      noValidate
      className="space-y-3 rounded-md border border-dashed border-primary/40 bg-primary/5 p-3"
      aria-label="Registrar tipo de cambio"
    >
      <div className="grid grid-cols-1 gap-2 md:grid-cols-12">
        <FieldInline
          label="Fecha"
          required
          error={form.formState.errors.fecha?.message}
          className="md:col-span-3"
        >
          <Controller
            name="fecha"
            control={form.control}
            render={({ field }) => (
              <DatePickerField
                value={field.value}
                onChange={(v) => field.onChange(v ?? '')}
              />
            )}
          />
        </FieldInline>

        <FieldInline
          label="Valor en MXN"
          required
          error={form.formState.errors.valorEnMxn?.message}
          className="md:col-span-4"
        >
          <Input
            type="number"
            step="0.0001"
            min="0"
            {...form.register('valorEnMxn', { valueAsNumber: true })}
          />
        </FieldInline>

        <FieldInline
          label="Origen"
          required
          error={form.formState.errors.origen?.message}
          className="md:col-span-3"
        >
          <Controller
            name="origen"
            control={form.control}
            render={({ field }) => (
              <Select
                value={String(field.value)}
                onValueChange={(v) => field.onChange(Number(v))}
              >
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={String(OrigenTipoCambio.Manual)}>
                    Manual
                  </SelectItem>
                  <SelectItem value={String(OrigenTipoCambio.DOF)}>
                    DOF
                  </SelectItem>
                  <SelectItem value={String(OrigenTipoCambio.Banxico)}>
                    Banxico
                  </SelectItem>
                </SelectContent>
              </Select>
            )}
          />
        </FieldInline>
      </div>

      <div className="flex flex-wrap items-center justify-end gap-2 border-t pt-2">
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={onCancel}
          disabled={registrar.isPending}
        >
          Cancelar
        </Button>
        <Button type="submit" size="sm" disabled={registrar.isPending}>
          {registrar.isPending ? 'Registrando…' : 'Registrar'}
        </Button>
      </div>
    </form>
  );
}

interface FieldInlineProps {
  label: string;
  required?: boolean;
  error?: string;
  className?: string;
  children: React.ReactNode;
}

function FieldInline({
  label,
  required,
  error,
  className,
  children,
}: FieldInlineProps) {
  return (
    <div className={className}>
      <label className="mb-1 flex items-center gap-1 text-xs font-medium">
        {label}
        {required && (
          <span aria-hidden="true" className="text-rose-600">
            *
          </span>
        )}
      </label>
      {children}
      {error != null && (
        <p role="alert" className="mt-1 text-xs text-rose-600">
          {error}
        </p>
      )}
    </div>
  );
}
