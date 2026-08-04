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
  ActualizarEmpresaSchema,
  type ActualizarEmpresaValues,
} from '@/modules/administracion/schemas/empresa';
import { useActualizarEmpresa } from '@/modules/administracion/api';
import type { EmpresaResponse } from '@/modules/administracion/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Form de datos generales de la empresa (tab "Datos" del detalle).
 * PATCH parcial: el RFC es read-only — el backend no permite cambiarlo
 * una vez creada la empresa (es el natural-key del dominio fiscal).
 *
 * <para>Sin permiso <c>admin.empresas.editar</c> el form se renderiza
 * read-only con los inputs deshabilitados; sin botón Guardar.</para>
 */
export interface EmpresaDatosFormProps {
  empresa: EmpresaResponse;
}

export function EmpresaDatosForm({ empresa }: EmpresaDatosFormProps) {
  const canEditar = useHasPermission(PermisosCanonicos.AdminEmpresasEditar);
  const idempotencyKey = useFormIdempotencyKey();
  const actualizar = useActualizarEmpresa();

  const form = useForm<ActualizarEmpresaValues>({
    resolver: zodResolver(ActualizarEmpresaSchema),
    defaultValues: buildDefaults(empresa),
  });

  // Si cambia la empresa cargada (otra row del master), resincronizar.
  useEffect(() => {
    form.reset(buildDefaults(empresa));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [empresa.id, empresa.version]);

  function onSubmit(values: ActualizarEmpresaValues) {
    // Si el usuario borra el nombre comercial, lo enviamos como
    // <c>limpiarNombreComercial: true</c> (el PATCH backend distingue
    // "no se mandó" de "explícitamente vacío").
    const nombreComercialVacio =
      values.nombreComercial == null || values.nombreComercial.length === 0;
    const tasaIvaVacia = values.tasaIvaDefault == null;

    actualizar.mutate(
      {
        id: empresa.id,
        payload: {
          razonSocial: values.razonSocial,
          regimenFiscal: values.regimenFiscal,
          nombreComercial: nombreComercialVacio ? null : values.nombreComercial,
          limpiarNombreComercial: nombreComercialVacio,
          tasaIvaDefault: tasaIvaVacia ? null : values.tasaIvaDefault,
          limpiarTasaIvaDefault: tasaIvaVacia,
          // PATCH: null = no tocar (el CP no tiene semántica de "limpiar" —
          // una vez capturado, corregirlo requiere otro valor válido).
          codigoPostal: values.codigoPostal,
        },
        idempotencyKey,
      },
      {
        onSuccess: () => {
          toast.success('Empresa actualizada');
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
          toast.error('Error inesperado al actualizar la empresa.');
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
      <FormRow label="RFC" hint="No editable después de creada la empresa.">
        <Input
          value={empresa.rfc}
          readOnly
          aria-readonly
          className="font-mono"
        />
      </FormRow>

      <FormRow
        label="Régimen fiscal"
        required
        error={form.formState.errors.regimenFiscal?.message}
      >
        <Input
          maxLength={10}
          disabled={!canEditar}
          {...form.register('regimenFiscal')}
        />
      </FormRow>

      <div className="md:col-span-2">
        <FormRow
          label="Razón social"
          required
          error={form.formState.errors.razonSocial?.message}
        >
          <Input
            maxLength={254}
            disabled={!canEditar}
            {...form.register('razonSocial')}
          />
        </FormRow>
      </div>

      <FormRow
        label="Tasa IVA default"
        hint="Fracción 0–1 (ej. 0.16). Fallback de IVA en captura manual de Facturación; el IVA del artículo tiene prioridad. Vacío = sin default."
        error={form.formState.errors.tasaIvaDefault?.message}
      >
        <Controller
          name="tasaIvaDefault"
          control={form.control}
          render={({ field }) => (
            <Input
              type="number"
              step="0.01"
              min={0}
              max={1}
              placeholder="0.16"
              disabled={!canEditar}
              value={field.value ?? ''}
              onChange={(e) =>
                field.onChange(
                  e.target.value === '' ? null : Number(e.target.value),
                )
              }
            />
          )}
        />
      </FormRow>

      <FormRow
        label="Código postal fiscal"
        hint="CP del domicilio fiscal SAT — es el LugarExpedicion del CFDI 4.0. Sin él, la emisión de facturas falla."
        error={form.formState.errors.codigoPostal?.message}
      >
        <Controller
          name="codigoPostal"
          control={form.control}
          render={({ field }) => (
            <Input
              maxLength={5}
              inputMode="numeric"
              placeholder="76120"
              className="font-mono"
              disabled={!canEditar}
              value={field.value ?? ''}
              onChange={(e) =>
                field.onChange(e.target.value.length > 0 ? e.target.value : null)
              }
            />
          )}
        />
      </FormRow>

      <div className="md:col-span-2">
        <FormRow
          label="Nombre comercial"
          hint="Opcional. Vacío = no aplica."
          error={form.formState.errors.nombreComercial?.message}
        >
          <Controller
            name="nombreComercial"
            control={form.control}
            render={({ field }) => (
              <Input
                maxLength={254}
                disabled={!canEditar}
                value={field.value ?? ''}
                onChange={(e) =>
                  field.onChange(
                    e.target.value.length > 0 ? e.target.value : null,
                  )
                }
              />
            )}
          />
        </FormRow>
      </div>

      {canEditar && (
        <div className="md:col-span-2 flex flex-wrap items-center justify-end gap-2 border-t pt-3">
          <Button
            type="button"
            variant="ghost"
            onClick={() => form.reset(buildDefaults(empresa))}
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

function buildDefaults(empresa: EmpresaResponse): ActualizarEmpresaValues {
  return {
    razonSocial: empresa.razonSocial,
    regimenFiscal: empresa.regimenFiscal,
    nombreComercial: empresa.nombreComercial ?? null,
    tasaIvaDefault: empresa.tasaIvaDefault ?? null,
    codigoPostal: empresa.codigoPostal ?? null,
  };
}
