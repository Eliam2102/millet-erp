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
  SucursalSchema,
  type SucursalValues,
} from '@/modules/administracion/schemas/sucursal';
import {
  useActualizarSucursal,
  useCrearSucursal,
} from '@/modules/administracion/api';
import {
  type SucursalResponse,
  TipoSucursal,
} from '@/modules/administracion/api/types';
import { cn } from '@/lib/utils';

/**
 * Form inline (sin modal) para AGREGAR o EDITAR una sucursal. Mismo
 * patrón que <c>LineaInlineForm</c> del módulo Compras
 * (memoria <c>feedback_inline_no_modal_para_items</c>): nada de
 * dialogs para items dentro de un master.
 *
 * <para><b>Modo agregar</b> (sin <c>sucursal</c>): border dashed
 * primary, label "Agregar sucursal". Al guardar resetea y vuelve a
 * foco en clave para encadenar altas.</para>
 *
 * <para><b>Modo editar</b> (con <c>sucursal</c>): border solid amber,
 * label "Guardar cambios". Clave es read-only — el backend no permite
 * cambiar clave una vez creada (la PATCH solo acepta nombre).</para>
 */
export interface SucursalInlineFormProps {
  empresaId: string;
  sucursal?: SucursalResponse | null;
  onCancel: () => void;
  onSaved?: () => void;
}

const VALORES_INICIALES: SucursalValues = {
  clave: '',
  nombre: '',
  tipo: TipoSucursal.Taller,
  claveAw: '',
};

export function SucursalInlineForm({
  empresaId,
  sucursal,
  onCancel,
  onSaved,
}: SucursalInlineFormProps) {
  const esEditar = sucursal != null;
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useCrearSucursal();
  const actualizar = useActualizarSucursal();

  const form = useForm<SucursalValues>({
    resolver: zodResolver(SucursalSchema),
    defaultValues: esEditar
      ? {
          clave: sucursal.clave,
          nombre: sucursal.nombre,
          tipo: sucursal.tipo ?? TipoSucursal.Taller,
          claveAw: sucursal.claveAw ?? '',
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
      if (error.code === 'SUCURSAL_CLAVE_DUPLICADA') {
        form.setError('clave', {
          type: error.code,
          message: 'Ya existe una sucursal con esa clave.',
        });
        return;
      }
      if (error.code === 'SUCURSAL_CLAVE_AW_DUPLICADA') {
        form.setError('claveAw', {
          type: error.code,
          message: 'Otra sucursal ya usa esa clave A+W.',
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
    toast.error('Error inesperado al guardar la sucursal.');
  }

  function onSubmit(values: SucursalValues) {
    const claveAw = values.claveAw ?? '';
    if (esEditar && sucursal != null) {
      // Campo vaciado por el usuario = desasociar de A+W (LimpiarClaveAw);
      // con valor = set/replace. El PATCH usa null como "no tocar".
      const limpiarClaveAw = claveAw === '' && sucursal.claveAw != null;
      actualizar.mutate(
        {
          empresaId,
          id: sucursal.id,
          payload: {
            nombre: values.nombre,
            tipo: values.tipo,
            claveAw: claveAw === '' ? null : claveAw,
            limpiarClaveAw,
          },
          idempotencyKey,
        },
        {
          onSuccess: () => {
            toast.success('Sucursal actualizada');
            onSaved?.();
          },
          onError,
        },
      );
      return;
    }
    crear.mutate(
      {
        empresaId,
        command: {
          clave: values.clave,
          nombre: values.nombre,
          tipo: values.tipo,
          claveAw: claveAw === '' ? null : claveAw,
        },
        idempotencyKey,
      },
      {
        onSuccess: (resp) => {
          toast.success(`Sucursal ${resp.clave} agregada`);
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
        esEditar ? `Editar sucursal ${sucursal?.clave}` : 'Agregar sucursal'
      }
    >
      <div className="grid grid-cols-1 gap-2 md:grid-cols-12">
        <Field
          label="Clave"
          required
          error={form.formState.errors.clave?.message}
          className="md:col-span-2"
        >
          <Input
            maxLength={20}
            placeholder="MID"
            readOnly={esEditar}
            aria-readonly={esEditar}
            {...form.register('clave')}
          />
        </Field>

        <Field
          label="Nombre"
          required
          error={form.formState.errors.nombre?.message}
          className="md:col-span-4"
        >
          <Input
            maxLength={254}
            placeholder="Sucursal Mérida"
            {...form.register('nombre')}
          />
        </Field>

        <Field
          label="Tipo"
          required
          error={form.formState.errors.tipo?.message}
          className="md:col-span-3"
        >
          <select
            aria-label="Tipo de sucursal"
            className="flex h-9 w-full rounded-md border border-input bg-background px-3 py-1 text-sm shadow-sm transition-colors focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50"
            {...form.register('tipo', { valueAsNumber: true })}
          >
            <option value={TipoSucursal.Taller}>Taller (Corte local)</option>
            <option value={TipoSucursal.Planta}>Planta (Maquila central)</option>
          </select>
        </Field>

        <Field
          label="Clave A+W"
          error={form.formState.errors.claveAw?.message}
          className="md:col-span-3"
        >
          <Input
            maxLength={40}
            placeholder="CONKAL"
            title="Clave con la que A+W refiere esta sucursal en sus pedidos. Vacía = no recibe pedidos de A+W."
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
              {isPending ? 'Agregando…' : 'Agregar sucursal'}
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
