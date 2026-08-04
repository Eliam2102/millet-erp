import { useEffect } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { Check, Plus, X } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import {
  DepartamentoSchema,
  type DepartamentoValues,
} from '@/modules/administracion/schemas/departamento';
import {
  useActualizarDepartamento,
  useCrearDepartamento,
} from '@/modules/administracion/api';
import type { DepartamentoResponse } from '@/modules/administracion/api/types';
import { cn } from '@/lib/utils';

/**
 * Form inline para AGREGAR o EDITAR un departamento. Espejo de
 * <c>SucursalInlineForm</c>: misma UX, misma decisión inline-no-modal
 * (memoria <c>feedback_inline_no_modal_para_items</c>).
 *
 * <para>Modo agregar: border dashed primary. Modo editar: border solid
 * amber, clave read-only (PATCH backend solo acepta nombre).</para>
 */
export interface DepartamentoInlineFormProps {
  empresaId: string;
  departamento?: DepartamentoResponse | null;
  onCancel: () => void;
  onSaved?: () => void;
}

const VALORES_INICIALES: DepartamentoValues = {
  clave: '',
  nombre: '',
};

export function DepartamentoInlineForm({
  empresaId,
  departamento,
  onCancel,
  onSaved,
}: DepartamentoInlineFormProps) {
  const esEditar = departamento != null;
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useCrearDepartamento();
  const actualizar = useActualizarDepartamento();

  const form = useForm<DepartamentoValues>({
    resolver: zodResolver(DepartamentoSchema),
    defaultValues: esEditar
      ? { clave: departamento.clave, nombre: departamento.nombre }
      : VALORES_INICIALES,
  });

  useEffect(() => {
    form.setFocus(esEditar ? 'nombre' : 'clave');
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const isPending = crear.isPending || actualizar.isPending;

  function onError(error: Error) {
    if (esApiError(error)) {
      if (error.code === 'DEPARTAMENTO_CLAVE_DUPLICADA') {
        form.setError('clave', {
          type: error.code,
          message: 'Ya existe un departamento con esa clave.',
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
    toast.error('Error inesperado al guardar el departamento.');
  }

  function onSubmit(values: DepartamentoValues) {
    if (esEditar && departamento != null) {
      actualizar.mutate(
        {
          empresaId,
          id: departamento.id,
          payload: { nombre: values.nombre },
          idempotencyKey,
        },
        {
          onSuccess: () => {
            toast.success('Departamento actualizado');
            onSaved?.();
          },
          onError,
        },
      );
      return;
    }
    crear.mutate(
      { empresaId, command: values, idempotencyKey },
      {
        onSuccess: (resp) => {
          toast.success(`Departamento ${resp.clave} agregado`);
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
      aria-label={
        esEditar
          ? `Editar departamento ${departamento?.clave}`
          : 'Agregar departamento'
      }
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
            placeholder="COMP"
            readOnly={esEditar}
            aria-readonly={esEditar}
            {...form.register('clave')}
          />
        </Field>

        <Field
          label="Nombre"
          required
          error={form.formState.errors.nombre?.message}
          className="md:col-span-9"
        >
          <Input
            maxLength={254}
            placeholder="Compras"
            {...form.register('nombre')}
          />
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
              {isPending ? 'Agregando…' : 'Agregar departamento'}
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
