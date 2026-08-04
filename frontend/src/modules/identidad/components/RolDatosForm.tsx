import { useEffect } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import {
  ActualizarRolSchema,
  type ActualizarRolValues,
} from '@/modules/identidad/schemas/rol';
import { useActualizarRol } from '@/modules/identidad/api';
import type { RolResponse } from '@/modules/identidad/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Form de datos generales del rol (tab "Datos" del detalle). PATCH
 * parcial: el <c>codigo</c> es read-only — el backend no permite
 * cambiarlo una vez creado el rol (es el natural-key del dominio de
 * identidad).
 *
 * <para>Sin permiso <c>identidad.roles.editar</c> el form se renderiza
 * read-only sin botón Guardar. Si <c>esDelSistema=true</c> los inputs
 * quedan disabled (los roles MVP seedeados no son editables).</para>
 */
export interface RolDatosFormProps {
  rol: RolResponse;
}

export function RolDatosForm({ rol }: RolDatosFormProps) {
  const canEditar = useHasPermission(PermisosCanonicos.IdentidadRolesEditar);
  const editable = canEditar && !rol.esDelSistema;
  const idempotencyKey = useFormIdempotencyKey();
  const actualizar = useActualizarRol();

  const form = useForm<ActualizarRolValues>({
    resolver: zodResolver(ActualizarRolSchema),
    defaultValues: buildDefaults(rol),
  });

  useEffect(() => {
    form.reset(buildDefaults(rol));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [rol.id, rol.version]);

  function onSubmit(values: ActualizarRolValues) {
    const descripcionVacia =
      values.descripcion == null || values.descripcion.length === 0;

    actualizar.mutate(
      {
        id: rol.id,
        payload: {
          nombre: values.nombre,
          descripcion: descripcionVacia ? null : values.descripcion,
          limpiarDescripcion: descripcionVacia,
        },
        idempotencyKey,
      },
      {
        onSuccess: () => {
          toast.success('Rol actualizado');
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
          toast.error('Error inesperado al actualizar el rol.');
        },
      },
    );
  }

  const dirty = form.formState.isDirty;

  return (
    <form
      onSubmit={form.handleSubmit(onSubmit)}
      noValidate
      className="grid grid-cols-1 gap-4 rounded-md border bg-card p-4 max-w-3xl"
    >
      {rol.esDelSistema && (
        <div
          role="status"
          className="rounded-md border border-amber-300 bg-amber-50/50 px-3 py-2 text-xs text-amber-900"
        >
          Este rol es del sistema (seed inicial). No se puede editar ni
          eliminar.
        </div>
      )}

      <FormRow label="Código" hint="No editable después de creado el rol.">
        <Input
          value={rol.codigo}
          readOnly
          aria-readonly
          className="font-mono"
        />
      </FormRow>

      <FormRow
        label="Nombre"
        required
        error={form.formState.errors.nombre?.message}
      >
        <Input
          maxLength={100}
          disabled={!editable}
          {...form.register('nombre')}
        />
      </FormRow>

      <FormRow
        label="Descripción"
        hint="Opcional. Máximo 500 caracteres."
        error={form.formState.errors.descripcion?.message}
      >
        <Controller
          name="descripcion"
          control={form.control}
          render={({ field }) => (
            <Textarea
              maxLength={500}
              rows={3}
              disabled={!editable}
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

      {editable && (
        <div className="flex flex-wrap items-center justify-end gap-2 border-t pt-3">
          <Button
            type="button"
            variant="ghost"
            onClick={() => form.reset(buildDefaults(rol))}
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

function buildDefaults(rol: RolResponse): ActualizarRolValues {
  return {
    nombre: rol.nombre,
    descripcion: rol.descripcion ?? null,
  };
}
