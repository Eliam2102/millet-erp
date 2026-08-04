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
  CanalVentaSchema,
  type CanalVentaValues,
} from '@/modules/administracion/schemas/canal-venta';
import {
  useActualizarCanalVenta,
  useCrearCanalVenta,
} from '@/modules/administracion/api';
import type { CanalVentaResponse } from '@/modules/administracion/api/types';
import { cn } from '@/lib/utils';

/**
 * Form inline (sin modal) para AGREGAR o EDITAR un canal de venta
 * (FAC-ING-PR3). Mismo patrón que <c>SucursalInlineForm</c>
 * (memoria <c>feedback_inline_no_modal_para_items</c>): nada de
 * dialogs para items dentro de un master.
 *
 * <para><b>Modo agregar</b> (sin <c>canal</c>): border dashed primary,
 * label "Agregar canal". El id lo asigna el backend (max + 1).</para>
 *
 * <para><b>Modo editar</b> (con <c>canal</c>): border solid amber,
 * label "Guardar cambios". El id es inmutable (ya persistido en pedidos
 * y facturas); vaciar la clave A+W desasocia el canal de la ingesta
 * (<c>limpiarClaveAw</c>).</para>
 */
export interface CanalVentaInlineFormProps {
  canal?: CanalVentaResponse | null;
  onCancel: () => void;
  onSaved?: () => void;
}

const VALORES_INICIALES: CanalVentaValues = {
  nombre: '',
  claveAw: '',
};

export function CanalVentaInlineForm({
  canal,
  onCancel,
  onSaved,
}: CanalVentaInlineFormProps) {
  const esEditar = canal != null;
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useCrearCanalVenta();
  const actualizar = useActualizarCanalVenta();

  const form = useForm<CanalVentaValues>({
    resolver: zodResolver(CanalVentaSchema),
    defaultValues: esEditar
      ? { nombre: canal.nombre, claveAw: canal.claveAw ?? '' }
      : VALORES_INICIALES,
  });

  useEffect(() => {
    form.setFocus('nombre');
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const isPending = crear.isPending || actualizar.isPending;

  function onError(error: Error) {
    if (esApiError(error)) {
      if (error.code === 'CANAL_VENTA_NOMBRE_DUPLICADO') {
        form.setError('nombre', {
          type: error.code,
          message: 'Ya existe un canal de venta con ese nombre.',
        });
        return;
      }
      if (error.code === 'CANAL_VENTA_CLAVE_AW_DUPLICADA') {
        form.setError('claveAw', {
          type: error.code,
          message: 'Otro canal ya usa esa clave A+W.',
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
    toast.error('Error inesperado al guardar el canal de venta.');
  }

  function onSubmit(values: CanalVentaValues) {
    const claveAw = values.claveAw ?? '';
    if (esEditar && canal != null) {
      // Campo vaciado por el usuario = desasociar de A+W (limpiarClaveAw);
      // con valor = set/replace. El PATCH usa null como "no tocar".
      const limpiarClaveAw = claveAw === '' && canal.claveAw != null;
      actualizar.mutate(
        {
          id: canal.id,
          payload: {
            nombre: values.nombre,
            claveAw: claveAw === '' ? null : claveAw,
            limpiarClaveAw,
          },
          idempotencyKey,
        },
        {
          onSuccess: () => {
            toast.success('Canal de venta actualizado');
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
          nombre: values.nombre,
          claveAw: claveAw === '' ? null : claveAw,
        },
        idempotencyKey,
      },
      {
        onSuccess: (resp) => {
          toast.success(`Canal de venta "${resp.nombre}" agregado`);
          form.reset(VALORES_INICIALES);
          form.setFocus('nombre');
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
        esEditar ? `Editar canal ${canal?.nombre}` : 'Agregar canal'
      }
    >
      <div className="grid grid-cols-1 gap-2 md:grid-cols-12">
        <Field
          label="Nombre"
          required
          error={form.formState.errors.nombre?.message}
          className="md:col-span-8"
        >
          <Input
            maxLength={254}
            placeholder="Tienda Cancún"
            {...form.register('nombre')}
          />
        </Field>

        <Field
          label="Clave A+W"
          error={form.formState.errors.claveAw?.message}
          className="md:col-span-4"
        >
          <Input
            maxLength={40}
            placeholder="CANCUN"
            title="GRUPPE con el que A+W refiere este canal en la ingesta de pedidos. Vacía = no recibe pedidos de A+W."
            {...form.register('claveAw')}
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
              {isPending ? 'Agregando…' : 'Agregar canal'}
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
