import { useEffect } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { DepartamentoSelector } from '@/components/erp';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import {
  ActualizarUsuarioSchema,
  type ActualizarUsuarioValues,
} from '@/modules/identidad/schemas/usuario';
import { useActualizarUsuario } from '@/modules/identidad/api';
import type { UsuarioResponse } from '@/modules/identidad/api/types';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';

/**
 * Form de datos generales del usuario (tab "Datos" del detalle).
 * PATCH parcial: email, nombre y departamentoId son editables; el
 * <c>entraOid</c> queda fijado al crear y se muestra read-only para
 * que el admin pueda distinguir usuarios reales (oid GUID) de
 * placeholders dev (<c>dev-*</c>).
 *
 * <para>Sin <c>identidad.usuarios.editar</c> el form se renderiza
 * read-only sin botón Guardar.</para>
 */
export interface UsuarioDatosFormProps {
  usuario: UsuarioResponse;
}

export function UsuarioDatosForm({ usuario }: UsuarioDatosFormProps) {
  const canEditar = useHasPermission(PermisosCanonicos.IdentidadUsuariosEditar);
  const idempotencyKey = useFormIdempotencyKey();
  const actualizar = useActualizarUsuario();

  const form = useForm<ActualizarUsuarioValues>({
    resolver: zodResolver(ActualizarUsuarioSchema),
    defaultValues: buildDefaults(usuario),
  });

  // Si cambia el usuario cargado (otra row del master), resincronizar.
  useEffect(() => {
    form.reset(buildDefaults(usuario));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [usuario.id, usuario.version]);

  function onSubmit(values: ActualizarUsuarioValues) {
    // Si el usuario borra el departamentoId, marcamos
    // <c>limpiarDepartamento: true</c> (PATCH backend distingue
    // "no se mandó" de "explícitamente null").
    const departamentoVacio = values.departamentoId == null;

    actualizar.mutate(
      {
        id: usuario.id,
        payload: {
          email: values.email,
          nombreCompleto: values.nombreCompleto,
          departamentoId: departamentoVacio ? null : values.departamentoId,
          limpiarDepartamento: departamentoVacio,
        },
        idempotencyKey,
      },
      {
        onSuccess: () => {
          toast.success('Usuario actualizado');
          form.reset(values);
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (error.code === 'USUARIO_EMAIL_DUPLICADO') {
              form.setError('email', {
                type: error.code,
                message: 'Ya existe otro usuario con ese email.',
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
          toast.error('Error inesperado al actualizar el usuario.');
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
      <FormRow
        label="Entra ID Object ID"
        hint="No editable. Se fija al crear el usuario; placeholders dev-* hasta que el resolver real esté disponible."
      >
        <Input
          value={usuario.entraOid}
          readOnly
          aria-readonly
          className="font-mono text-xs"
        />
      </FormRow>

      <FormRow
        label="Email"
        required
        error={form.formState.errors.email?.message}
      >
        <Input
          type="email"
          maxLength={254}
          disabled={!canEditar}
          {...form.register('email')}
        />
      </FormRow>

      <div className="md:col-span-2">
        <FormRow
          label="Nombre completo"
          required
          error={form.formState.errors.nombreCompleto?.message}
        >
          <Input
            maxLength={254}
            disabled={!canEditar}
            {...form.register('nombreCompleto')}
          />
        </FormRow>
      </div>

      <div className="md:col-span-2">
        <FormRow
          label="Departamento"
          hint="Opcional. Vacío = sin departamento asignado."
          error={form.formState.errors.departamentoId?.message}
        >
          <Controller
            name="departamentoId"
            control={form.control}
            render={({ field }) => (
              <DepartamentoSelector
                value={field.value}
                onChange={(v) => field.onChange(v)}
                disabled={!canEditar}
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
            onClick={() => form.reset(buildDefaults(usuario))}
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

function buildDefaults(usuario: UsuarioResponse): ActualizarUsuarioValues {
  return {
    email: usuario.email,
    nombreCompleto: usuario.nombre,
    departamentoId: usuario.departamentoId ?? null,
  };
}
