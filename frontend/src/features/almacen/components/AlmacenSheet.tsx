import { useEffect } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { SucursalSelector } from '@/components/erp';
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import {
  CrearAlmacenSchema,
  type CrearAlmacenValues,
} from '@/features/almacen/schemas/almacen';
import {
  useCrearAlmacen,
  useEditarAlmacen,
} from '@/features/almacen/api/useAlmacenes';
import {
  EstatusCatalogo,
  EstatusCatalogoLabels,
  type AlmacenListItem,
} from '@/features/almacen/api/types';

/**
 * <c>&lt;AlmacenSheet/&gt;</c> — slide-from-right para crear o editar
 * un almacén (F1-PR1, doc 07 §FE-F1-PR1). Reusa el mismo schema Zod
 * para ambos modos; la diferencia es qué mutation se dispara y los
 * <c>defaultValues</c>.
 *
 * <para>Errores manejados:</para>
 * <list>
 *   <item><b>ALMACEN_CLAVE_DUPLICADA</b>: inline en <c>clave</c>.</item>
 *   <item><b>ALMACEN_SUCURSAL_INACTIVA</b>: inline en <c>sucursalId</c>.</item>
 *   <item><b>otros</b>: toast genérico con traceId.</item>
 * </list>
 */
export interface AlmacenSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Si viene, modo edición; si es null, modo crear. */
  editando: AlmacenListItem | null;
}

const ESTATUS_OPTIONS: readonly EstatusCatalogo[] = [
  EstatusCatalogo.Activo,
  EstatusCatalogo.Inactivo,
  EstatusCatalogo.Borrador,
];

export function AlmacenSheet({
  open,
  onOpenChange,
  editando,
}: AlmacenSheetProps) {
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useCrearAlmacen();
  const editar = useEditarAlmacen();

  const form = useForm<CrearAlmacenValues>({
    resolver: zodResolver(CrearAlmacenSchema),
    defaultValues: {
      clave: '',
      nombre: '',
      sucursalId: '',
      estatus: EstatusCatalogo.Activo,
    },
  });

  useEffect(() => {
    if (open) {
      form.reset(
        editando
          ? {
              clave: editando.clave,
              nombre: editando.nombre,
              sucursalId: editando.sucursalId,
              estatus: editando.estatus,
            }
          : {
              clave: '',
              nombre: '',
              sucursalId: '',
              estatus: EstatusCatalogo.Activo,
            },
      );
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, editando?.id]);

  function onSubmit(values: CrearAlmacenValues) {
    if (editando) {
      editar.mutate(
        { id: editando.id, ...values },
        {
          onSuccess: () => {
            toast.success('Almacén actualizado');
            onOpenChange(false);
          },
          onError: (error) => manejarError(error),
        },
      );
    } else {
      crear.mutate(
        { command: values, idempotencyKey },
        {
          onSuccess: () => {
            toast.success('Almacén creado');
            onOpenChange(false);
          },
          onError: (error) => manejarError(error),
        },
      );
    }
  }

  function manejarError(error: unknown) {
    if (esApiError(error)) {
      if (error.code === 'ALMACEN_CLAVE_DUPLICADA') {
        form.setError('clave', {
          type: error.code,
          message: 'Ya existe un almacén con esa clave.',
        });
        return;
      }
      if (error.code === 'ALMACEN_SUCURSAL_INACTIVA') {
        form.setError('sucursalId', {
          type: error.code,
          message: 'La sucursal está inactiva.',
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
    toast.error('Error inesperado al guardar el almacén.');
  }

  const isPending = crear.isPending || editar.isPending;

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="sm:max-w-md">
        <SheetHeader>
          <SheetTitle>
            {editando ? 'Editar almacén' : 'Nuevo almacén'}
          </SheetTitle>
          <SheetDescription>
            {editando
              ? 'Modifica la clave, nombre, sucursal o estatus del almacén.'
              : 'Captura los datos del nuevo almacén. La clave es única en todo el módulo.'}
          </SheetDescription>
        </SheetHeader>

        <form
          id="almacen-form"
          onSubmit={form.handleSubmit(onSubmit)}
          noValidate
          className="flex-1 space-y-3 overflow-y-auto px-6"
        >
          <Field
            label="Clave"
            required
            error={form.formState.errors.clave?.message}
          >
            <Controller
              name="clave"
              control={form.control}
              render={({ field }) => (
                <Input
                  {...field}
                  value={field.value ?? ''}
                  maxLength={20}
                  placeholder="Ej. ALM-MTY"
                  autoFocus={!editando}
                />
              )}
            />
          </Field>

          <Field
            label="Nombre"
            required
            error={form.formState.errors.nombre?.message}
          >
            <Controller
              name="nombre"
              control={form.control}
              render={({ field }) => (
                <Input
                  {...field}
                  value={field.value ?? ''}
                  maxLength={254}
                  placeholder="Nombre descriptivo"
                />
              )}
            />
          </Field>

          <Field
            label="Sucursal"
            required
            error={form.formState.errors.sucursalId?.message}
          >
            <Controller
              name="sucursalId"
              control={form.control}
              render={({ field }) => (
                <SucursalSelector
                  value={field.value || null}
                  onChange={(id) => field.onChange(id ?? '')}
                />
              )}
            />
          </Field>

          <Field
            label="Estatus"
            required
            error={form.formState.errors.estatus?.message}
          >
            <Controller
              name="estatus"
              control={form.control}
              render={({ field }) => (
                <Select
                  value={String(field.value)}
                  onValueChange={(v) => field.onChange(Number(v))}
                >
                  <SelectTrigger>
                    <SelectValue placeholder="Seleccionar estatus" />
                  </SelectTrigger>
                  <SelectContent>
                    {ESTATUS_OPTIONS.map((e) => (
                      <SelectItem key={e} value={String(e)}>
                        {EstatusCatalogoLabels[e]}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            />
          </Field>
        </form>

        <SheetFooter>
          <Button
            type="button"
            variant="ghost"
            onClick={() => onOpenChange(false)}
            disabled={isPending}
          >
            Cancelar
          </Button>
          <Button type="submit" form="almacen-form" disabled={isPending}>
            {isPending
              ? 'Guardando…'
              : editando
                ? 'Guardar cambios'
                : 'Crear almacén'}
          </Button>
        </SheetFooter>
      </SheetContent>
    </Sheet>
  );
}

function Field({
  label,
  required,
  error,
  children,
}: {
  label: string;
  required?: boolean;
  error?: string;
  children: React.ReactNode;
}) {
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
      {error != null && (
        <p role="alert" className="text-xs text-rose-600">
          {error}
        </p>
      )}
    </div>
  );
}
