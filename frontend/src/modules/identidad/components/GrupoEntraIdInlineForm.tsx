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
  AsociarGrupoEntraIdSchema,
  type AsociarGrupoEntraIdValues,
} from '@/modules/identidad/schemas/grupo-entra-id';
import { useAsociarGrupoEntraId } from '@/modules/identidad/api';
import { cn } from '@/lib/utils';

/**
 * Form inline para asociar un grupo de Microsoft Entra ID a un rol.
 * Espejo de <c>SucursalInlineForm</c> / <c>DepartamentoInlineForm</c>
 * (decisión inline-no-modal del feedback
 * <c>inline_no_modal_para_items</c>).
 *
 * <para>Solo modo agregar: border dashed primary. La edición de un
 * grupo asociado no existe en el MVP — para cambiar nombre/objectId se
 * desasocia y se vuelve a asociar.</para>
 */
export interface GrupoEntraIdInlineFormProps {
  rolId: string;
  onCancel: () => void;
  onSaved?: () => void;
}

const VALORES_INICIALES: AsociarGrupoEntraIdValues = {
  objectId: '',
  nombre: '',
};

export function GrupoEntraIdInlineForm({
  rolId,
  onCancel,
  onSaved,
}: GrupoEntraIdInlineFormProps) {
  const idempotencyKey = useFormIdempotencyKey();
  const asociar = useAsociarGrupoEntraId();

  const form = useForm<AsociarGrupoEntraIdValues>({
    resolver: zodResolver(AsociarGrupoEntraIdSchema),
    defaultValues: VALORES_INICIALES,
  });

  useEffect(() => {
    form.setFocus('objectId');
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function onSubmit(values: AsociarGrupoEntraIdValues) {
    asociar.mutate(
      { rolId, command: values, idempotencyKey },
      {
        onSuccess: (resp) => {
          toast.success(`Grupo "${resp.nombre}" asociado`);
          form.reset(VALORES_INICIALES);
          form.setFocus('objectId');
          onSaved?.();
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (error.code === 'GRUPO_ENTRA_ID_DUPLICADO') {
              form.setError('objectId', {
                type: error.code,
                message:
                  'Ese grupo de Entra ID ya está asociado a este rol.',
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
          toast.error('Error inesperado al asociar el grupo.');
        },
      },
    );
  }

  return (
    <form
      onSubmit={form.handleSubmit(onSubmit)}
      noValidate
      onKeyDown={(e) => {
        if (e.key === 'Escape' && !asociar.isPending) {
          e.preventDefault();
          onCancel();
        }
      }}
      className={cn(
        'space-y-3 rounded-md border p-3',
        'border-dashed border-primary/40 bg-primary/5',
      )}
      aria-label="Asociar grupo Entra ID"
    >
      <div className="grid grid-cols-1 gap-2 md:grid-cols-12">
        <Field
          label="Object ID"
          required
          error={form.formState.errors.objectId?.message}
          className="md:col-span-5"
        >
          <Input
            maxLength={100}
            placeholder="00000000-0000-0000-0000-000000000000"
            className="font-mono text-xs"
            {...form.register('objectId')}
          />
        </Field>

        <Field
          label="Nombre del grupo"
          required
          error={form.formState.errors.nombre?.message}
          className="md:col-span-7"
        >
          <Input
            maxLength={254}
            placeholder="Compras - Aprobadores"
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
          disabled={asociar.isPending}
        >
          <X className="mr-1 h-4 w-4" />
          Cancelar
        </Button>
        <Button type="submit" size="sm" disabled={asociar.isPending}>
          {asociar.isPending ? (
            <>
              <Check className="mr-1 h-4 w-4" />
              Asociando…
            </>
          ) : (
            <>
              <Plus className="mr-1 h-4 w-4" />
              Asociar grupo
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
