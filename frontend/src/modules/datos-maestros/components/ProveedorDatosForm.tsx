import { useEffect, type ReactNode } from 'react';
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
import { applyServerErrors, esApiError } from '@/lib/api';
import {
  ActualizarProveedorSchema,
  type ActualizarProveedorValues,
} from '@/modules/datos-maestros/schemas/proveedor';
import { useActualizarProveedor } from '@/modules/datos-maestros/api';
import {
  TipoPersonaProveedor,
  type ProveedorDetalle,
} from '@/modules/datos-maestros/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Form de datos del proveedor (sección única del detalle, sin tabs).
 * PATCH parcial: la <c>clave</c> es read-only — el backend la marca
 * inmutable (natural-key del catálogo cross-empresa).
 *
 * <para>Sin permiso <c>compartido.catalogos.administrar</c> el form
 * queda read-only sin botón Guardar. El submit usa la convención
 * <c>limpiarX</c> de la API: nullable vacío + flag = setear a null.</para>
 */
export interface ProveedorDatosFormProps {
  proveedor: ProveedorDetalle;
}

export function ProveedorDatosForm({ proveedor }: ProveedorDatosFormProps) {
  const canEditar = useHasPermission(
    PermisosCanonicos.CompartidoCatalogosAdministrar,
  );
  const actualizar = useActualizarProveedor();

  const form = useForm<ActualizarProveedorValues>({
    resolver: zodResolver(ActualizarProveedorSchema),
    defaultValues: buildDefaults(proveedor),
  });

  // Si cambia el proveedor cargado (otra row), resincronizar.
  useEffect(() => {
    form.reset(buildDefaults(proveedor));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [proveedor.id]);

  function onSubmit(values: ActualizarProveedorValues) {
    const nombreComercialVacio =
      values.nombreComercial == null || values.nombreComercial.length === 0;
    const emailVacio = values.email == null || values.email.length === 0;
    const telefonoVacio =
      values.telefono == null || values.telefono.length === 0;
    const condicionesVacio = values.condicionesPagoDias == null;

    actualizar.mutate(
      {
        id: proveedor.id,
        payload: {
          razonSocial: values.razonSocial,
          rfc: values.rfc,
          tipoPersona: values.tipoPersona,
          nombreComercial: nombreComercialVacio ? null : values.nombreComercial,
          email: emailVacio ? null : values.email,
          telefono: telefonoVacio ? null : values.telefono,
          condicionesPagoDias: condicionesVacio
            ? null
            : values.condicionesPagoDias,
          limpiarNombreComercial: nombreComercialVacio,
          limpiarEmail: emailVacio,
          limpiarTelefono: telefonoVacio,
          limpiarCondicionesPago: condicionesVacio,
        },
        // Key fresca por submit: este form es multi-submit (queda montado tras
        // guardar), así que una key estable daría 422 al 2º guardado con un body
        // distinto (ADR-0020; patrón AccionesOC/SheetNuevaOC).
        idempotencyKey: crypto.randomUUID(),
      },
      {
        onSuccess: () => {
          toast.success('Proveedor actualizado');
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
          toast.error('Error inesperado al actualizar el proveedor.');
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
      <FormRow label="Clave" hint="No editable.">
        <Input
          value={proveedor.clave}
          readOnly
          aria-readonly
          className="font-mono"
        />
      </FormRow>

      <FormRow
        label="RFC"
        required
        error={form.formState.errors.rfc?.message}
      >
        <Input
          maxLength={13}
          disabled={!canEditar}
          className="font-mono"
          {...form.register('rfc')}
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
        label="Tipo persona"
        required
        error={form.formState.errors.tipoPersona?.message}
      >
        <Controller
          name="tipoPersona"
          control={form.control}
          render={({ field }) => (
            <Select
              value={String(field.value)}
              onValueChange={(v) => field.onChange(Number(v))}
              disabled={!canEditar}
            >
              <SelectTrigger>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={String(TipoPersonaProveedor.Moral)}>
                  Moral
                </SelectItem>
                <SelectItem value={String(TipoPersonaProveedor.Fisica)}>
                  Física
                </SelectItem>
              </SelectContent>
            </Select>
          )}
        />
      </FormRow>

      <FormRow
        label="Condiciones de pago (días)"
        hint="Vacío = no aplica."
        error={form.formState.errors.condicionesPagoDias?.message}
      >
        <Controller
          name="condicionesPagoDias"
          control={form.control}
          render={({ field }) => (
            <Input
              type="number"
              min={0}
              max={365}
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

      <div className="md:col-span-2">
        <FormRow
          label="Nombre comercial"
          hint="Opcional."
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

      <FormRow
        label="Email"
        hint="Opcional."
        error={form.formState.errors.email?.message}
      >
        <Controller
          name="email"
          control={form.control}
          render={({ field }) => (
            <Input
              type="email"
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

      <FormRow
        label="Teléfono"
        hint="Opcional."
        error={form.formState.errors.telefono?.message}
      >
        <Controller
          name="telefono"
          control={form.control}
          render={({ field }) => (
            <Input
              maxLength={50}
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

      {canEditar && (
        <div className="md:col-span-2 flex flex-wrap items-center justify-end gap-2 border-t pt-3">
          <Button
            type="button"
            variant="ghost"
            onClick={() => form.reset(buildDefaults(proveedor))}
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
  children: ReactNode;
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

function buildDefaults(p: ProveedorDetalle): ActualizarProveedorValues {
  return {
    razonSocial: p.razonSocial,
    rfc: p.rfc,
    tipoPersona: p.tipoPersona,
    nombreComercial: p.nombreComercial ?? null,
    email: p.email ?? null,
    telefono: p.telefono ?? null,
    condicionesPagoDias: p.condicionesPagoDias ?? null,
  };
}
