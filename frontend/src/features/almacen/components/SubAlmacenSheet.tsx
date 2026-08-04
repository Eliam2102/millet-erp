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
import {
  applyServerErrors,
  esApiError,
  useFormIdempotencyKey,
} from '@/lib/api';
import {
  CrearSubAlmacenSchema,
  type CrearSubAlmacenValues,
} from '@/features/almacen/schemas/sub-almacen';
import {
  useAlmacenes,
  useCrearSubAlmacen,
  useEditarSubAlmacen,
} from '@/features/almacen/api/useAlmacenes';
import {
  EstatusCatalogo,
  EstatusCatalogoLabels,
  TipoSubAlmacen,
  TipoSubAlmacenLabels,
  type SubAlmacenListItem,
} from '@/features/almacen/api/types';

/**
 * <c>&lt;SubAlmacenSheet/&gt;</c> — slide-from-right para crear o
 * editar un sub-almacén (F1-PR1). En modo edición, el campo
 * <c>almacenId</c> se muestra como label readonly (el backend no
 * permite mover un sub-almacén entre almacenes).
 */
export interface SubAlmacenSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  editando: SubAlmacenListItem | null;
  /** Almacén pre-seleccionado para modo crear (opcional). */
  almacenIdInicial?: string;
}

const ESTATUS_OPTIONS: readonly EstatusCatalogo[] = [
  EstatusCatalogo.Activo,
  EstatusCatalogo.Inactivo,
  EstatusCatalogo.Borrador,
];

const TIPO_OPTIONS: readonly TipoSubAlmacen[] = [
  TipoSubAlmacen.Insumos,
  TipoSubAlmacen.MaterialesDirectos,
  TipoSubAlmacen.MaterialEnRevision,
  TipoSubAlmacen.Transitorio,
];

export function SubAlmacenSheet({
  open,
  onOpenChange,
  editando,
  almacenIdInicial,
}: SubAlmacenSheetProps) {
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useCrearSubAlmacen();
  const editar = useEditarSubAlmacen();
  const almacenesQuery = useAlmacenes({ limit: 500 });
  const almacenes = almacenesQuery.data?.items ?? [];

  const form = useForm<CrearSubAlmacenValues>({
    resolver: zodResolver(CrearSubAlmacenSchema),
    defaultValues: {
      almacenId: almacenIdInicial ?? '',
      clave: '',
      nombre: '',
      tipo: TipoSubAlmacen.Insumos,
      estatus: EstatusCatalogo.Activo,
    },
  });

  useEffect(() => {
    if (open) {
      form.reset(
        editando
          ? {
              almacenId: editando.almacenId,
              clave: editando.clave,
              nombre: editando.nombre,
              tipo: editando.tipo,
              estatus: editando.estatus,
            }
          : {
              almacenId: almacenIdInicial ?? '',
              clave: '',
              nombre: '',
              tipo: TipoSubAlmacen.Insumos,
              estatus: EstatusCatalogo.Activo,
            },
      );
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, editando?.id, almacenIdInicial]);

  function onSubmit(values: CrearSubAlmacenValues) {
    if (editando) {
      editar.mutate(
        {
          id: editando.id,
          clave: values.clave,
          nombre: values.nombre,
          tipo: values.tipo,
          estatus: values.estatus,
        },
        {
          onSuccess: () => {
            toast.success('Sub-almacén actualizado');
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
            toast.success('Sub-almacén creado');
            onOpenChange(false);
          },
          onError: (error) => manejarError(error),
        },
      );
    }
  }

  function manejarError(error: unknown) {
    if (esApiError(error)) {
      if (error.code === 'SUBALMACEN_CLAVE_DUPLICADA') {
        form.setError('clave', {
          type: error.code,
          message: 'Ya existe sub-almacén con esa clave en el almacén.',
        });
        return;
      }
      if (error.code === 'ALMACEN_NO_ENCONTRADO') {
        form.setError('almacenId', {
          type: error.code,
          message: 'El almacén ya no existe.',
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
    toast.error('Error inesperado al guardar el sub-almacén.');
  }

  const isPending = crear.isPending || editar.isPending;
  const isEdit = editando != null;

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="sm:max-w-md">
        <SheetHeader>
          <SheetTitle>
            {isEdit ? 'Editar sub-almacén' : 'Nuevo sub-almacén'}
          </SheetTitle>
          <SheetDescription>
            {isEdit
              ? 'La clave es única dentro del almacén padre. El almacén padre no se puede cambiar; crea uno nuevo si necesitas mover el sub-almacén.'
              : 'Define la clave (única dentro del almacén), nombre, tipo y estatus.'}
          </SheetDescription>
        </SheetHeader>

        <form
          id="sub-almacen-form"
          onSubmit={form.handleSubmit(onSubmit)}
          noValidate
          className="flex-1 space-y-3 overflow-y-auto px-6"
        >
          <Field
            label="Almacén"
            required
            error={form.formState.errors.almacenId?.message}
          >
            <Controller
              name="almacenId"
              control={form.control}
              render={({ field }) => (
                <Select
                  value={field.value || ''}
                  onValueChange={(v) => field.onChange(v)}
                  disabled={isEdit || almacenesQuery.isLoading}
                >
                  <SelectTrigger>
                    <SelectValue placeholder="Selecciona almacén" />
                  </SelectTrigger>
                  <SelectContent>
                    {almacenes.map((a) => (
                      <SelectItem key={a.id} value={a.id}>
                        {a.clave} · {a.nombre}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
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
                  placeholder="Ej. INS"
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
                  placeholder="Nombre descriptivo"
                />
              )}
            />
          </Field>

          <Field
            label="Tipo"
            required
            error={form.formState.errors.tipo?.message}
          >
            <Controller
              name="tipo"
              control={form.control}
              render={({ field }) => (
                <Select
                  value={String(field.value)}
                  onValueChange={(v) => field.onChange(Number(v))}
                >
                  <SelectTrigger>
                    <SelectValue placeholder="Seleccionar tipo" />
                  </SelectTrigger>
                  <SelectContent>
                    {TIPO_OPTIONS.map((t) => (
                      <SelectItem key={t} value={String(t)}>
                        {TipoSubAlmacenLabels[t]}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
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
          <Button type="submit" form="sub-almacen-form" disabled={isPending}>
            {isPending
              ? 'Guardando…'
              : isEdit
                ? 'Guardar cambios'
                : 'Crear sub-almacén'}
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
