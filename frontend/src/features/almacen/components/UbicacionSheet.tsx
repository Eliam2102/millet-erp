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
import { SubAlmacenSelector } from '@/components/erp/selectors/SubAlmacenSelector';
import { applyServerErrors, esApiError } from '@/lib/api';
import {
  UbicacionFormSchema,
  type UbicacionFormValues,
} from '@/features/almacen/schemas/ubicacion';
import {
  useCrearUbicacion,
  useEditarUbicacion,
} from '@/features/almacen/api/useUbicaciones';
import {
  EstatusCatalogo,
  type UbicacionListItem,
} from '@/features/almacen/api/types';

/**
 * <c>&lt;UbicacionSheet/&gt;</c> — slide-from-right para crear o editar una
 * ubicación N4 (rack/pasillo, ADR-0047 PR C7.1). En edición, el sub-almacén va
 * readonly (una ubicación no se muda; el backend conserva su padre y el PATCH
 * solo acepta clave/nombre). Una ubicación nueva nace Activa; la baja/reactivación
 * va por sus botones dedicados en la bandeja. Idempotency-Key fresco por submit
 * (fix PR B). Molde <c>SubAlmacenSheet</c>.
 */
export interface UbicacionSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  editando: UbicacionListItem | null;
  /** Sub-almacén pre-seleccionado para modo crear (del filtro activo). */
  subAlmacenIdInicial?: string;
}

export function UbicacionSheet({
  open,
  onOpenChange,
  editando,
  subAlmacenIdInicial,
}: UbicacionSheetProps) {
  const crear = useCrearUbicacion();
  const editar = useEditarUbicacion();

  const form = useForm<UbicacionFormValues>({
    resolver: zodResolver(UbicacionFormSchema),
    defaultValues: {
      subAlmacenId: subAlmacenIdInicial ?? '',
      clave: '',
      nombre: '',
    },
  });

  useEffect(() => {
    if (open) {
      form.reset(
        editando
          ? {
              subAlmacenId: editando.subAlmacenId,
              clave: editando.clave,
              nombre: editando.nombre,
            }
          : {
              subAlmacenId: subAlmacenIdInicial ?? '',
              clave: '',
              nombre: '',
            },
      );
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, editando?.id, subAlmacenIdInicial]);

  function onSubmit(values: UbicacionFormValues) {
    // Key fresca por submit: un reintento tras error NO reusa la key (evita 409).
    const idempotencyKey = crypto.randomUUID();
    if (editando) {
      editar.mutate(
        {
          command: {
            id: editando.id,
            clave: values.clave,
            nombre: values.nombre,
          },
          idempotencyKey,
        },
        {
          onSuccess: () => {
            toast.success('Ubicación actualizada');
            onOpenChange(false);
          },
          onError: (error) => manejarError(error),
        },
      );
    } else {
      crear.mutate(
        {
          command: {
            subAlmacenId: values.subAlmacenId,
            clave: values.clave,
            nombre: values.nombre,
            estatus: EstatusCatalogo.Activo,
          },
          idempotencyKey,
        },
        {
          onSuccess: () => {
            toast.success('Ubicación creada');
            onOpenChange(false);
          },
          onError: (error) => manejarError(error),
        },
      );
    }
  }

  function manejarError(error: unknown) {
    if (esApiError(error)) {
      if (error.code === 'UBICACION_CLAVE_DUPLICADA') {
        form.setError('clave', {
          type: error.code,
          message: 'Ya existe una ubicación con esa clave en el sub-almacén.',
        });
        return;
      }
      if (error.code === 'SUBALMACEN_NO_ENCONTRADO') {
        form.setError('subAlmacenId', {
          type: error.code,
          message: 'El sub-almacén ya no existe.',
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
    toast.error('Error inesperado al guardar la ubicación.');
  }

  const isPending = crear.isPending || editar.isPending;
  const isEdit = editando != null;

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="sm:max-w-md">
        <SheetHeader>
          <SheetTitle>
            {isEdit ? 'Editar ubicación' : 'Nueva ubicación'}
          </SheetTitle>
          <SheetDescription>
            {isEdit
              ? 'La clave es única dentro del sub-almacén. El sub-almacén no se puede cambiar; crea una nueva si necesitas moverla.'
              : 'Define el sub-almacén, la clave (única dentro del sub-almacén) y un nombre descriptivo del rack/pasillo.'}
          </SheetDescription>
        </SheetHeader>

        <form
          id="ubicacion-form"
          onSubmit={form.handleSubmit(onSubmit)}
          noValidate
          className="flex-1 space-y-3 overflow-y-auto px-6"
        >
          <Field
            label="Sub-almacén"
            required
            error={form.formState.errors.subAlmacenId?.message}
          >
            <Controller
              name="subAlmacenId"
              control={form.control}
              render={({ field }) => (
                <SubAlmacenSelector
                  value={field.value || null}
                  onChange={(id) => field.onChange(id ?? '')}
                  disabled={isEdit}
                />
              )}
            />
          </Field>

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
                  placeholder="Ej. HG1-84"
                  autoFocus={!isEdit}
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
                  placeholder="Nombre descriptivo (zona / rack / pasillo)"
                />
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
          <Button type="submit" form="ubicacion-form" disabled={isPending}>
            {isPending
              ? 'Guardando…'
              : isEdit
                ? 'Guardar cambios'
                : 'Crear ubicación'}
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
