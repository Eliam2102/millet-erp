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
import { ArticuloSelector, UbicacionSelector } from '@/components/erp';
import { applyServerErrors, esApiError } from '@/lib/api';
import {
  AsignarSchema,
  type AsignarValues,
} from '@/features/almacen/schemas/asignacion';
import { useCrearAsignacion } from '@/features/almacen/api/useAsignaciones';

/**
 * <c>&lt;AsignacionSheet/&gt;</c> — slide-from-right para asignar un artículo a
 * una ubicación (pantalla "Ubicación de artículos", ADR-0047 PR C). Molde
 * <c>ReordenSheet</c> pero SOLO 2 campos (artículo + ubicación): tras PR C la
 * asignación N4 no tiene min/máx/reorden. No hay editar (el PATCH se eliminó);
 * reactivar lo hace el propio Asignar. Idempotency-Key FRESCO por submit (fix
 * PR B): un reintento tras error usa llave nueva → no choca con la quemada.
 */
export interface AsignacionSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

const DEFAULTS: AsignarValues = { articuloId: '', ubicacionId: '' };

export function AsignacionSheet({ open, onOpenChange }: AsignacionSheetProps) {
  const crear = useCrearAsignacion();

  const form = useForm<AsignarValues>({
    resolver: zodResolver(AsignarSchema),
    defaultValues: DEFAULTS,
  });

  useEffect(() => {
    if (open) form.reset(DEFAULTS);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  function onSubmit(values: AsignarValues) {
    const idempotencyKey = crypto.randomUUID();
    crear.mutate(
      { command: values, idempotencyKey },
      {
        onSuccess: () => {
          toast.success('Artículo asignado a la ubicación');
          onOpenChange(false);
        },
        onError: manejarError,
      },
    );
  }

  function manejarError(error: unknown) {
    if (esApiError(error)) {
      if (error.code === 'ASIGNACION_DUPLICADA') {
        form.setError('ubicacionId', {
          type: error.code,
          message: 'El artículo ya está asignado a esta ubicación.',
        });
        return;
      }
      if (error.code === 'ARTICULO_INACTIVO') {
        form.setError('articuloId', {
          type: error.code,
          message: 'El artículo está inactivo; no se puede asignar.',
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
    toast.error('Error inesperado al asignar el artículo.');
  }

  const isPending = crear.isPending;

  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" className="sm:max-w-md">
        <SheetHeader>
          <SheetTitle>Asignar artículo a una ubicación</SheetTitle>
          <SheetDescription>
            Elige el artículo y la ubicación donde vivirá. Se creará su existencia
            en 0 ahí.
          </SheetDescription>
        </SheetHeader>

        <form
          id="asignacion-form"
          onSubmit={form.handleSubmit(onSubmit)}
          noValidate
          className="flex-1 space-y-3 overflow-y-auto px-6"
        >
          <Field
            label="Artículo"
            required
            error={form.formState.errors.articuloId?.message}
          >
            <Controller
              name="articuloId"
              control={form.control}
              render={({ field }) => (
                <ArticuloSelector
                  value={field.value || null}
                  onChange={(id) => field.onChange(id ?? '')}
                />
              )}
            />
          </Field>

          <Field
            label="Ubicación"
            required
            error={form.formState.errors.ubicacionId?.message}
          >
            <Controller
              name="ubicacionId"
              control={form.control}
              render={({ field }) => (
                <UbicacionSelector
                  value={field.value || null}
                  onChange={(id) => field.onChange(id ?? '')}
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
          <Button type="submit" form="asignacion-form" disabled={isPending}>
            {isPending ? 'Asignando…' : 'Asignar artículo'}
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
