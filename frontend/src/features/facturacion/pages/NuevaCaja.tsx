import { useEffect } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { useNavigate } from '@tanstack/react-router';
import { z } from 'zod';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { useCrearCaja } from '@/features/facturacion/api/useCajas';
import { applyServerErrors, esApiError, useFormIdempotencyKey } from '@/lib/api';

const NuevaCajaSchema = z.object({
  nombre: z.string().trim().min(1, 'El nombre es obligatorio.').max(100),
  descripcion: z.string().trim().max(254).optional(),
});

type NuevaCajaForm = z.infer<typeof NuevaCajaSchema>;

/**
 * <c>Form "Nueva caja"</c> (CAJAS-PR5, P4). Solo datos generales — la caja
 * nace activa y sin alcance (12-cajas.md §7); en success navega al detalle
 * para asignar sucursales/canales/cajeros y cierra el Sheet con
 * <c>force: true</c>.
 */
export function NuevaCaja(props: {
  onClose: (opts?: { force?: boolean }) => void;
  onDirtyChange: (dirty: boolean) => void;
}) {
  const navigate = useNavigate();
  const idempotencyKey = useFormIdempotencyKey();
  const crear = useCrearCaja();

  const form = useForm<NuevaCajaForm>({
    resolver: zodResolver(NuevaCajaSchema),
    defaultValues: { nombre: '', descripcion: '' },
  });

  const { isDirty } = form.formState;
  const { onDirtyChange } = props;
  useEffect(() => {
    onDirtyChange(isDirty);
  }, [isDirty, onDirtyChange]);

  function onSubmit(values: NuevaCajaForm) {
    crear.mutate(
      {
        command: {
          nombre: values.nombre,
          descripcion: values.descripcion === '' || values.descripcion == null ? null : values.descripcion,
        },
        idempotencyKey,
      },
      {
        onSuccess: (caja) => {
          toast.success('Caja creada. Asigna su alcance.');
          props.onClose({ force: true });
          void navigate({ to: '/facturacion/cajas/$id', params: { id: caja.id } });
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (
              applyServerErrors(
                form as unknown as Parameters<typeof applyServerErrors>[0],
                error,
              )
            ) {
              return;
            }
            toast.error(error.problem.title);
          } else {
            toast.error('No se pudo crear la caja.');
          }
        },
      },
    );
  }

  return (
    <form onSubmit={form.handleSubmit(onSubmit)} className="mt-4 space-y-4" noValidate>
      <div className="space-y-1">
        <Label htmlFor="caja-nombre">
          Nombre <span className="text-destructive">*</span>
        </Label>
        <Input id="caja-nombre" maxLength={100} autoFocus {...form.register('nombre')} />
        {form.formState.errors.nombre && (
          <p className="text-xs text-destructive">{form.formState.errors.nombre.message}</p>
        )}
      </div>

      <div className="space-y-1">
        <Label htmlFor="caja-descripcion">Descripción</Label>
        <Input id="caja-descripcion" maxLength={254} {...form.register('descripcion')} />
        {form.formState.errors.descripcion && (
          <p className="text-xs text-destructive">
            {form.formState.errors.descripcion.message}
          </p>
        )}
      </div>

      <div className="flex justify-end gap-2 pt-2">
        <Button type="button" variant="ghost" onClick={() => props.onClose()}>
          Cancelar
        </Button>
        <Button type="submit" disabled={crear.isPending}>
          Crear caja
        </Button>
      </div>
    </form>
  );
}
