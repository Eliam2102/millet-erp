import { FormGrid, FormCampo } from '@/components/erp/forms/FormGrid';
import { SheetBody } from '@/components/ui/sheet';
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
import { esApiError, codigoSoporte } from '@/lib/api';
import {
  RegularizarValeSchema,
  type RegularizarValeValues,
} from '@/features/almacen/schemas/salida';
import { useRegularizarVale } from '@/features/almacen/api/useSalidas';
import { Field } from '@/features/almacen/components/internal/Field';

/**
 * <c>&lt;RegularizarValeSheet/&gt;</c> — sheet para vincular una RQ
 * aprobada posterior a una salida por vale (A14, SLA 48h). El
 * backend valida que la RQ esté aprobada y aplica el vínculo.
 *
 * <para>Permiso: <c>almacen.salidas.por-vale</c> (mismo que registra
 * el vale inicial — quien regulariza es típicamente el supervisor
 * del solicitante).</para>
 */
export interface RegularizarValeSheetProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  salidaId: string;
  folio: string;
}

export function RegularizarValeSheet({
  open,
  onOpenChange,
  salidaId,
  folio,
}: RegularizarValeSheetProps) {
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent side="right" size="form">
        <SheetHeader>
          <SheetTitle>Regularizar vale {folio}</SheetTitle>
          <SheetDescription>
            Vincula una RQ aprobada posterior al vale para cerrar el ciclo. La RQ debe pertenecer al
            mismo sub-almacén/persona destinataria/material del vale.
          </SheetDescription>
        </SheetHeader>

        {open && (
          <FormBody
            salidaId={salidaId}
            onSuccess={() => onOpenChange(false)}
            onCancel={() => onOpenChange(false)}
          />
        )}
      </SheetContent>
    </Sheet>
  );
}

function FormBody({
  salidaId,
  onSuccess,
  onCancel,
}: {
  salidaId: string;
  onSuccess: () => void;
  onCancel: () => void;
}) {
  const regularizar = useRegularizarVale();
  const form = useForm<RegularizarValeValues>({
    resolver: zodResolver(RegularizarValeSchema),
    defaultValues: { rqRegularizadoraId: '' },
  });

  function onSubmit(values: RegularizarValeValues) {
    regularizar.mutate(
      {
        command: {
          salidaId,
          rqRegularizadoraId: values.rqRegularizadoraId,
        },
      },
      {
        onSuccess: () => {
          toast.success('Vale regularizado');
          onSuccess();
          form.reset({ rqRegularizadoraId: '' });
        },
        onError: (error) => {
          if (esApiError(error)) {
            if (error.code === 'VALE_YA_REGULARIZADO') {
              toast.error('Este vale ya fue regularizado previamente.');
              return;
            }
            if (error.code === 'VALE_RQ_REGULARIZADORA_INVALIDA') {
              form.setError('rqRegularizadoraId', {
                type: error.code,
                message: error.problem.detail ?? 'La RQ no está aprobada o no cubre el vale.',
              });
              return;
            }
            if (error.code === 'VALE_SUCURSAL_DISTINTA') {
              form.setError('rqRegularizadoraId', {
                type: error.code,
                message: error.problem.detail ?? 'La RQ es de otra sucursal.',
              });
              return;
            }
            toast.error(error.problem.title, {
              description: codigoSoporte(error),
            });
            return;
          }
          toast.error('Error inesperado al regularizar.');
        },
      },
    );
  }

  return (
    <>
      <SheetBody>
        <form
          id="regularizar-vale-form"
          onSubmit={form.handleSubmit(onSubmit)}
          noValidate
          className="space-y-4"
        >
          <FormGrid>
            <FormCampo span={12}>
              <Field
                label="RQ regularizadora"
                required
                error={form.formState.errors.rqRegularizadoraId?.message}
              >
                <Controller
                  name="rqRegularizadoraId"
                  control={form.control}
                  render={({ field }) => (
                    <Input
                      {...field}
                      value={field.value ?? ''}
                      placeholder="GUID de la RQ aprobada"
                      spellCheck={false}
                      autoFocus
                    />
                  )}
                />
              </Field>
            </FormCampo>
          </FormGrid>
          <p className="text-xs text-ink-muted">
            La RQ debe haber sido aprobada con posterioridad al vale y cubrir el mismo
            material/cantidad. El sistema valida el vínculo y, si pasa, marca el vale como
            regularizado.
          </p>
        </form>
      </SheetBody>

      <SheetFooter>
        <Button type="button" variant="ghost" onClick={onCancel} disabled={regularizar.isPending}>
          Cancelar
        </Button>
        <Button type="submit" form="regularizar-vale-form" disabled={regularizar.isPending}>
          {regularizar.isPending ? 'Regularizando…' : 'Regularizar'}
        </Button>
      </SheetFooter>
    </>
  );
}
