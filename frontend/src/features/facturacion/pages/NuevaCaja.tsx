import { FormCampo } from '@/components/erp/forms/FormGrid';
import { FormSeccion } from '@/components/erp/forms/FormSeccion';
import { SucursalSelector } from '@/components/erp/selectors/SucursalSelector';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { useCrearCaja } from '@/features/facturacion/api/useCajas';
import { applyServerErrors, esApiError, useFormIdempotencyKey } from '@/lib/api';
import { zodResolver } from '@hookform/resolvers/zod';
import { useNavigate } from '@tanstack/react-router';
import { useEffect } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { toast } from 'sonner';
import { z } from 'zod';

const NuevaCajaSchema = z.object({
  nombre: z.string().trim().min(1, 'El nombre es obligatorio.').max(100),
  descripcion: z.string().trim().max(254).optional(),
  sucursalId: z.string().min(1, 'Selecciona la sucursal de la caja.'),
});

type NuevaCajaForm = z.infer<typeof NuevaCajaSchema>;

/**
 * <c>Form "Nueva caja"</c> (CAJAS-PR5, P4). Datos generales y sucursal
 * obligatoria (86e3t97uy): sin ella la caja quedaba visible para todas las
 * sucursales. En success navega al detalle para asignar canales/cajeros y
 * cierra el Sheet con <c>force: true</c>.
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
    defaultValues: { nombre: '', descripcion: '', sucursalId: '' },
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
          descripcion:
            values.descripcion === '' || values.descripcion == null ? null : values.descripcion,
          sucursalId: values.sucursalId,
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
              applyServerErrors(form as unknown as Parameters<typeof applyServerErrors>[0], error)
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
    <form onSubmit={form.handleSubmit(onSubmit)} className="mt-4 space-y-5" noValidate>
      <FormSeccion titulo="Datos de la caja">
        <FormCampo span={6} className="space-y-1">
          <Label htmlFor="caja-nombre">
            Nombre <span className="text-danger-fg">*</span>
          </Label>
          <Input id="caja-nombre" maxLength={100} autoFocus {...form.register('nombre')} />
          {form.formState.errors.nombre && (
            <p className="text-xs text-danger-fg">{form.formState.errors.nombre.message}</p>
          )}
        </FormCampo>

        <FormCampo span={6} className="space-y-1">
          <Label htmlFor="caja-descripcion">Descripción</Label>
          <Input id="caja-descripcion" maxLength={254} {...form.register('descripcion')} />
          {form.formState.errors.descripcion && (
            <p className="text-xs text-danger-fg">{form.formState.errors.descripcion.message}</p>
          )}
        </FormCampo>

        <FormCampo span={6} className="space-y-1">
          <Label>
            Sucursal <span className="text-danger-fg">*</span>
          </Label>
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
          {form.formState.errors.sucursalId && (
            <p className="text-xs text-danger-fg">{form.formState.errors.sucursalId.message}</p>
          )}
        </FormCampo>

        <FormCampo span={12} className="flex justify-end gap-2 pt-2">
          <Button type="button" variant="ghost" onClick={() => props.onClose()}>
            Cancelar
          </Button>
          <Button type="submit" disabled={crear.isPending}>
            Crear caja
          </Button>
        </FormCampo>
      </FormSeccion>
    </form>
  );
}
