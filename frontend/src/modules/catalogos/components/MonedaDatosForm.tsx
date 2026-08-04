import { useEffect } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import {
  ActualizarMonedaSchema,
  type ActualizarMonedaValues,
} from '@/modules/catalogos/schemas/moneda';
import { useActualizarMoneda } from '@/modules/catalogos/api';
import type { MonedaResponse } from '@/modules/catalogos/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Form de datos generales de la moneda (tab "Datos" del detalle).
 * Código read-only — el backend no permite cambiarlo (business key).
 */
export interface MonedaDatosFormProps {
  moneda: MonedaResponse;
}

export function MonedaDatosForm({ moneda }: MonedaDatosFormProps) {
  const canEditar = useHasPermission(PermisosCanonicos.CatalogosMonedasGestionar);
  const idempotencyKey = useFormIdempotencyKey();
  const actualizar = useActualizarMoneda();

  const form = useForm<ActualizarMonedaValues>({
    resolver: zodResolver(ActualizarMonedaSchema),
    defaultValues: buildDefaults(moneda),
  });

  useEffect(() => {
    form.reset(buildDefaults(moneda));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [moneda.id, moneda.version]);

  function onSubmit(values: ActualizarMonedaValues) {
    actualizar.mutate(
      {
        id: moneda.id,
        payload: {
          nombre: values.nombre,
          decimales: values.decimales,
          activa: values.activa,
        },
        idempotencyKey,
      },
      {
        onSuccess: () => {
          toast.success('Moneda actualizada');
          form.reset(values);
        },
        onError: (error) => {
          if (esApiError(error)) {
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
          toast.error('Error inesperado al actualizar la moneda.');
        },
      },
    );
  }

  const dirty = form.formState.isDirty;

  return (
    <form
      onSubmit={form.handleSubmit(onSubmit)}
      noValidate
      className="grid grid-cols-1 gap-4 rounded-md border bg-card p-4 md:grid-cols-2 max-w-3xl"
    >
      <FormRow label="Código" hint="No editable después de creada la moneda.">
        <Input
          value={moneda.codigo}
          readOnly
          aria-readonly
          className="font-mono"
        />
      </FormRow>

      <FormRow
        label="Decimales"
        required
        hint="Número de decimales a desplegar (0–6)."
        error={form.formState.errors.decimales?.message}
      >
        <Input
          type="number"
          min={0}
          max={6}
          step={1}
          disabled={!canEditar}
          {...form.register('decimales', { valueAsNumber: true })}
        />
      </FormRow>

      <div className="md:col-span-2">
        <FormRow
          label="Nombre"
          required
          error={form.formState.errors.nombre?.message}
        >
          <Input
            maxLength={100}
            disabled={!canEditar}
            {...form.register('nombre')}
          />
        </FormRow>
      </div>

      <div className="md:col-span-2">
        <FormRow label="Activa" hint="Las monedas inactivas siguen siendo visibles en históricos pero no se ofrecen en selectores.">
          <Controller
            name="activa"
            control={form.control}
            render={({ field }) => (
              <label className="inline-flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  className="h-4 w-4 rounded border-input"
                  checked={field.value}
                  onChange={(e) => field.onChange(e.target.checked)}
                  disabled={!canEditar}
                />
                <span>{field.value ? 'Activa' : 'Inactiva'}</span>
              </label>
            )}
          />
        </FormRow>
      </div>

      {canEditar && (
        <div className="md:col-span-2 flex flex-wrap items-center justify-end gap-2 border-t pt-3">
          <Button
            type="button"
            variant="ghost"
            onClick={() => form.reset(buildDefaults(moneda))}
            disabled={!dirty || actualizar.isPending}
          >
            Descartar cambios
          </Button>
          <Button type="submit" disabled={!dirty || actualizar.isPending}>
            {actualizar.isPending ? 'Guardando…' : 'Guardar cambios'}
          </Button>
        </div>
      )}
    </form>
  );
}

interface FormRowProps {
  label: string;
  required?: boolean;
  hint?: string;
  error?: string;
  children: React.ReactNode;
}

function FormRow({ label, required, hint, error, children }: FormRowProps) {
  return (
    <div className="space-y-1.5">
      <label className="flex items-center gap-1 text-sm font-medium">
        {label}
        {required && (
          <span aria-hidden="true" className="text-rose-600">
            *
          </span>
        )}
      </label>
      {children}
      {hint != null && error == null && (
        <p className="text-xs text-muted-foreground">{hint}</p>
      )}
      {error != null && (
        <p role="alert" className="text-xs text-rose-600">
          {error}
        </p>
      )}
    </div>
  );
}

function buildDefaults(moneda: MonedaResponse): ActualizarMonedaValues {
  return {
    nombre: moneda.nombre,
    decimales: moneda.decimales,
    activa: moneda.activa,
  };
}
