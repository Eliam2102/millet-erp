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
  OperadorSchema,
  type OperadorValues,
} from '@/features/facturacion/schemas/carta-porte-catalogos';
import {
  useActualizarOperador,
  useCrearOperador,
} from '@/features/facturacion/api/cartaPorteCatalogos';
import type { OperadorListItem } from '@/features/facturacion/api/types';
import { cn } from '@/lib/utils';

/**
 * Form inline (sin modal) para AGREGAR o EDITAR un operador (chofer) del
 * catálogo de Carta Porte. Mismo patrón que <c>CanalVentaInlineForm</c>
 * (memoria <c>feedback_inline_no_modal_para_items</c>).
 *
 * <para><b>Modo agregar</b> (sin <c>operador</c>): border dashed primary.
 * <b>Modo editar</b> (con <c>operador</c>): border solid amber; el RFC
 * es inmutable (identidad fiscal del chofer — el input queda
 * deshabilitado).</para>
 */
export interface OperadorInlineFormProps {
  operador?: OperadorListItem | null;
  onCancel: () => void;
  onSaved?: () => void;
}

const VALORES_INICIALES: OperadorValues = {
  rfc: '',
  nombre: '',
  numLicencia: '',
};

export function OperadorInlineForm({
  operador,
  onCancel,
  onSaved,
}: OperadorInlineFormProps) {
  const esEditar = operador != null;
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useCrearOperador();
  const actualizar = useActualizarOperador();

  const form = useForm<OperadorValues>({
    resolver: zodResolver(OperadorSchema),
    defaultValues: esEditar
      ? {
          rfc: operador.rfc,
          nombre: operador.nombre,
          numLicencia: operador.numLicencia,
        }
      : VALORES_INICIALES,
  });

  useEffect(() => {
    form.setFocus(esEditar ? 'nombre' : 'rfc');
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const isPending = crear.isPending || actualizar.isPending;

  function onError(error: Error) {
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
        description: error.traceId ? `Código: ${error.traceId}` : undefined,
      });
      return;
    }
    toast.error('Error inesperado al guardar el operador.');
  }

  function onSubmit(values: OperadorValues) {
    if (esEditar && operador != null) {
      actualizar.mutate(
        {
          id: operador.id,
          payload: { nombre: values.nombre, numLicencia: values.numLicencia },
          idempotencyKey,
        },
        {
          onSuccess: () => {
            toast.success(`Operador ${values.nombre} actualizado`);
            onSaved?.();
          },
          onError,
        },
      );
      return;
    }
    crear.mutate(
      {
        command: {
          rfc: values.rfc.toUpperCase(),
          nombre: values.nombre,
          numLicencia: values.numLicencia,
        },
        idempotencyKey,
      },
      {
        onSuccess: () => {
          toast.success(`Operador ${values.nombre} agregado`);
          form.reset(VALORES_INICIALES);
          form.setFocus('rfc');
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
        esEditar ? `Editar operador ${operador?.nombre}` : 'Agregar operador'
      }
    >
      <div className="grid grid-cols-1 gap-2 md:grid-cols-12">
        <Field
          label="RFC"
          required
          error={form.formState.errors.rfc?.message}
          className="md:col-span-3"
        >
          <Input
            className="font-mono uppercase"
            maxLength={13}
            placeholder="XAXX010101000"
            disabled={esEditar}
            title={esEditar ? 'El RFC es inmutable (identidad fiscal del chofer).' : undefined}
            {...form.register('rfc')}
          />
        </Field>
        <Field
          label="Nombre"
          required
          error={form.formState.errors.nombre?.message}
          className="md:col-span-6"
        >
          <Input
            maxLength={254}
            placeholder="Juan Pérez"
            {...form.register('nombre')}
          />
        </Field>
        <Field
          label="Núm. licencia"
          required
          error={form.formState.errors.numLicencia?.message}
          className="md:col-span-3"
        >
          <Input maxLength={50} {...form.register('numLicencia')} />
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
              {isPending ? 'Agregando…' : 'Agregar operador'}
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
