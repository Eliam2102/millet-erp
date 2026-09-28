import { useEffect } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Check, Plus, X } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { DepartamentoSelector } from '@/components/erp';
import {
  applyServerErrors,
  esApiError,
  useBodyScopedIdempotencyKey,
} from '@/lib/api';
import {
  PuestoSchema,
  type PuestoValues,
} from '@/modules/administracion/schemas/puesto';
import {
  useActualizarPuesto,
  useCrearPuesto,
} from '@/modules/administracion/api';
import { useRoles } from '@/modules/identidad/api/roles';
import type { PuestoListItem } from '@/features/catalogos/api';
import { cn } from '@/lib/utils';

/**
 * Form inline (sin modal) para AGREGAR o EDITAR un puesto (ADM-FE-PR1, F1-ADM-01.4).
 * Mismo patrón que <c>CanalVentaInlineForm</c>: border dashed primary
 * (agregar) vs solid amber (editar). La clave es business key
 * inmutable — en modo editar el input va disabled y el PATCH solo
 * manda nombre, departamentoId y rolSugeridoId opcionales.
 */
export interface PuestoInlineFormProps {
  puesto?: PuestoListItem | null;
  onCancel: () => void;
  onSaved?: () => void;
}

const VALORES_INICIALES: PuestoValues = {
  clave: '',
  nombre: '',
  rolSugeridoId: '',
  departamentoId: '',
};

export function PuestoInlineForm({
  puesto,
  onCancel,
  onSaved,
}: PuestoInlineFormProps) {
  const esEditar = puesto != null;
  const keyFor = useBodyScopedIdempotencyKey();
  const crear = useCrearPuesto();
  const actualizar = useActualizarPuesto();
  const rolesQuery = useRoles({ soloActivos: true });

  const form = useForm<PuestoValues>({
    resolver: zodResolver(PuestoSchema),
    defaultValues: esEditar
      ? {
          clave: puesto.clave,
          nombre: puesto.nombre,
          rolSugeridoId: puesto.rolSugeridoId ?? '',
          departamentoId: puesto.departamentoId ?? '',
        }
      : VALORES_INICIALES,
  });

  useEffect(() => {
    form.setFocus(esEditar ? 'nombre' : 'clave');
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const isPending = crear.isPending || actualizar.isPending;

  function onError(error: Error) {
    if (esApiError(error)) {
      if (error.code === 'PUESTO_CLAVE_DUPLICADA') {
        form.setError('clave', {
          type: error.code,
          message: 'Ya existe un puesto con esa clave.',
        });
        return;
      }
      if (error.code === 'ROL_SUGERIDO_INVALIDO') {
        form.setError('rolSugeridoId', {
          type: error.code,
          message: 'El rol seleccionado no es válido o está inactivo.',
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
        description: error.traceId ? `Código: ${error.traceId}` : undefined,
      });
      return;
    }
    toast.error('Error inesperado al guardar el puesto.');
  }

  function onSubmit(values: PuestoValues) {
    if (esEditar && puesto != null) {
      const limpiarRol = !values.rolSugeridoId && !!puesto.rolSugeridoId;
      const limpiarDepto = !values.departamentoId && !!puesto.departamentoId;
      const payload = {
        nombre: values.nombre,
        rolSugeridoId: values.rolSugeridoId || null,
        limpiarRolSugerido: limpiarRol,
        departamentoId: values.departamentoId || null,
        limpiarDepartamento: limpiarDepto,
      };
      actualizar.mutate(
        {
          id: puesto.id,
          payload,
          idempotencyKey: keyFor({ id: puesto.id, ...values, limpiarRol, limpiarDepto }),
        },
        {
          onSuccess: () => {
            toast.success('Puesto actualizado');
            onSaved?.();
          },
          onError,
        },
      );
      return;
    }
    const command = {
      id: '00000000-0000-0000-0000-000000000000',
      clave: values.clave,
      nombre: values.nombre,
      rolSugeridoId: values.rolSugeridoId || null,
      departamentoId: values.departamentoId || null,
    };
    crear.mutate(
      {
        command,
        idempotencyKey: keyFor(values),
      },
      {
        onSuccess: (resp) => {
          toast.success(`Puesto "${resp.nombre}" agregado`);
          form.reset(VALORES_INICIALES);
          form.setFocus('clave');
          onSaved?.();
        },
        onError,
      },
    );
  }

  return (
    <form
      onSubmit={form.handleSubmit(onSubmit)}
      noValidate
      onKeyDown={(e) => {
        if (e.key === 'Escape' && !isPending) {
          e.preventDefault();
          onCancel();
        }
      }}
      className={cn(
        'space-y-3 rounded-md border p-3',
        esEditar
          ? 'border-amber-400 bg-amber-50/40'
          : 'border-dashed border-primary/40 bg-primary/5',
      )}
      aria-label={esEditar ? `Editar puesto ${puesto?.clave}` : 'Agregar puesto'}
    >
      <div className="grid grid-cols-1 gap-2 md:grid-cols-12">
        <Field
          label="Clave"
          required
          error={form.formState.errors.clave?.message}
          className="md:col-span-3"
        >
          <Input
            maxLength={20}
            placeholder="GER"
            className="font-mono uppercase"
            disabled={esEditar}
            title={
              esEditar
                ? 'La clave es inmutable (business key).'
                : 'Clave corta del puesto (p.ej. GER, EJEC, OPER).'
            }
            {...form.register('clave')}
          />
        </Field>

        <Field
          label="Nombre"
          required
          error={form.formState.errors.nombre?.message}
          className="md:col-span-3"
        >
          <Input
            maxLength={254}
            placeholder="Gerente"
            {...form.register('nombre')}
          />
        </Field>

        <Field
          label="Departamento de referencia"
          error={form.formState.errors.departamentoId?.message}
          className="md:col-span-3"
        >
          <Controller
            control={form.control}
            name="departamentoId"
            render={({ field }) => (
              <DepartamentoSelector
                value={field.value || null}
                onChange={(id) => field.onChange(id ?? '')}
                className="w-full"
                placeholder="(Sin departamento)"
              />
            )}
          />
        </Field>

        <Field
          label="Rol sugerido en el ERP"
          error={form.formState.errors.rolSugeridoId?.message}
          className="md:col-span-3"
        >
          <select
            className="flex h-9 w-full rounded-md border border-input bg-background px-3 py-1 text-sm shadow-sm transition-colors focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50"
            aria-label="Rol sugerido"
            {...form.register('rolSugeridoId')}
          >
            <option value="">(Sin rol sugerido)</option>
            {rolesQuery.data?.items.map((r) => (
              <option key={r.id} value={r.id}>
                {r.nombre} ({r.codigo})
              </option>
            ))}
          </select>
        </Field>
      </div>

      <div className="flex items-center justify-end gap-2">
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={onCancel}
          disabled={isPending}
        >
          <X className="mr-1 h-4 w-4" />
          Cancelar
        </Button>
        <Button type="submit" size="sm" disabled={isPending}>
          {esEditar ? (
            <>
              <Check className="mr-1 h-4 w-4" />
              {isPending ? 'Guardando…' : 'Guardar cambios'}
            </>
          ) : (
            <>
              <Plus className="mr-1 h-4 w-4" />
              {isPending ? 'Agregando…' : 'Agregar puesto'}
            </>
          )}
        </Button>
      </div>
    </form>
  );
}

interface FieldProps {
  label: string;
  required?: boolean;
  error?: string;
  className?: string;
  children: React.ReactNode;
}

function Field({ label, required, error, className, children }: FieldProps) {
  return (
    <div className={cn('space-y-1', className)}>
      <label className="flex items-center gap-1 text-xs font-medium text-muted-foreground">
        {label}
        {required && (
          <span aria-hidden="true" className="text-rose-600">
            *
          </span>
        )}
      </label>
      {children}
      {error != null && (
        <p role="alert" className="text-xs text-rose-600">
          {error}
        </p>
      )}
    </div>
  );
}
